using MCP.Core.Configuration;
using MCP.Core.Services;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Text.Json;

namespace MCP.Core.Services;

public class RedisAnalysisCacheService : IAnalysisCacheService
{
    private readonly IDatabase _db;
    private readonly TimeSpan _ttl;
    private readonly ILogger<RedisAnalysisCacheService> _logger;
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public RedisAnalysisCacheService(
        IConnectionMultiplexer redis,
        IOptions<AnalysisCacheConfig> options,
        ILogger<RedisAnalysisCacheService> logger)
    {
        _db = redis.GetDatabase();
        _ttl =new TimeSpan( options.Value.TtlHours,0,0);
        _logger = logger;
    }

    // ── Key helpers ─────────────────────────────────────────────────────────

    private static string AnalysisKey(string project, string path)
        => $"analysis:{Normalize(project)}:{Normalize(path)}";

    private static string MethodsKey(string project, string path)
        => $"methods:{Normalize(project)}:{Normalize(path)}";

    private static string IndexKey(string project)
        => $"index:{Normalize(project)}";

    private static string Normalize(string s)
        => s.ToLowerInvariant().Replace('\\', '/');

    // ── File analysis ────────────────────────────────────────────────────────

    public async Task<CSharpFileAnalysis?> GetAsync(string projectName, string relativePath)
    {
        try
        {
            var val = await _db.StringGetAsync(AnalysisKey(projectName, relativePath));
            if (val.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<CSharpFileAnalysis>(val.ToString(), _json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache GET failed for {Project}:{Path}", projectName, relativePath);
            return null;
        }
    }

    public async Task SetAsync(string projectName, string relativePath, CSharpFileAnalysis analysis)
    {
        try
        {
            var json = JsonSerializer.Serialize(analysis, _json);
            await _db.StringSetAsync(AnalysisKey(projectName, relativePath), json, _ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache SET failed for {Project}:{Path}", projectName, relativePath);
        }
    }

    public async Task DeleteAsync(string projectName, string relativePath)
    {
        try
        {
            await _db.KeyDeleteAsync([
                AnalysisKey(projectName, relativePath),
                MethodsKey(projectName, relativePath)
            ]);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache DELETE failed for {Project}:{Path}", projectName, relativePath);
        }
    }

    // ── Method-body cache ────────────────────────────────────────────────────

    public async Task<bool> MethodsExistAsync(string projectName, string relativePath)
    {
        try
        {
            return await _db.KeyExistsAsync(MethodsKey(projectName, relativePath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Methods EXISTS check failed for {Project}:{Path}", projectName, relativePath);
            return false;
        }
    }

    public async Task<Dictionary<string, MethodImplementationInfo>?> GetMethodsAsync(
        string projectName, string relativePath)
    {
        try
        {
            var val = await _db.StringGetAsync(MethodsKey(projectName, relativePath));
            if (val.IsNullOrEmpty) return null;
            return JsonSerializer.Deserialize<Dictionary<string, MethodImplementationInfo>>(
                val.ToString(), _json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Methods cache GET failed for {Project}:{Path}", projectName, relativePath);
            return null;
        }
    }

    public async Task SetMethodsAsync(string projectName, string relativePath,
        Dictionary<string, MethodImplementationInfo> methods)
    {
        try
        {
            var json = JsonSerializer.Serialize(methods, _json);
            await _db.StringSetAsync(MethodsKey(projectName, relativePath), json, _ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Methods cache SET failed for {Project}:{Path}", projectName, relativePath);
        }
    }

    // ── Index management ─────────────────────────────────────────────────────

    public async Task<IReadOnlyList<string>> GetIndexAsync(string projectName)
    {
        try
        {
            var members = await _db.SetMembersAsync(IndexKey(projectName));
            return members.Select(m => m.ToString()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache INDEX GET failed for {Project}", projectName);
            return [];
        }
    }

    public async Task SetIndexAsync(string projectName, IEnumerable<string> relativePaths)
    {
        try
        {
            var key = IndexKey(projectName);
            var members = relativePaths.Select(p => (RedisValue)p).ToArray();

            var tx = _db.CreateTransaction();
            _ = tx.KeyDeleteAsync(key);
            if (members.Length > 0)
                _ = tx.SetAddAsync(key, members);
            _ = tx.KeyExpireAsync(key, _ttl);
            await tx.ExecuteAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache INDEX SET failed for {Project}", projectName);
        }
    }

    public async Task RemoveFromIndexAsync(string projectName, string relativePath)
    {
        try
        {
            await _db.SetRemoveAsync(IndexKey(projectName), relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache INDEX REMOVE failed for {Project}:{Path}", projectName, relativePath);
        }
    }

    public async Task AddToIndexAsync(string projectName, string relativePath)
    {
        try
        {
            await _db.SetAddAsync(IndexKey(projectName), relativePath);
            await _db.KeyExpireAsync(IndexKey(projectName), _ttl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache INDEX ADD failed for {Project}:{Path}", projectName, relativePath);
        }
    }

    public async Task PurgeProjectAsync(string projectName)
    {
        try
        {
            var index = await GetIndexAsync(projectName);

            // Delete analysis keys, method keys, and the index key in one batch
            var keys = index
                .SelectMany(p => new RedisKey[]
                {
                    AnalysisKey(projectName, p),
                    MethodsKey(projectName, p)
                })
                .Append((RedisKey)IndexKey(projectName))
                .ToArray();

            if (keys.Length > 0)
                await _db.KeyDeleteAsync(keys);

            _logger.LogInformation(
                "Purged {Count} cache keys for project '{Project}'", keys.Length, projectName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache PURGE failed for project '{Project}'", projectName);
        }
    }
}