using MCP.Core.Configuration;
using MCP.Core.Models;
using MCP.Core.Services;

namespace MCP.Core.Services;

public class CachedProjectSkeletonService : IProjectSkeletonService
{
    private readonly ProjectSkeletonService _inner;
    private readonly IAnalysisCacheService _cache;
    private readonly ILogger<CachedProjectSkeletonService> _logger;

    public CachedProjectSkeletonService(
        ProjectSkeletonService inner,
        IAnalysisCacheService cache,
        ILogger<CachedProjectSkeletonService> logger)
    {
        _inner = inner;
        _cache = cache;
        _logger = logger;
    }

    public async Task<CSharpFileAnalysis> AnalyzeCSharpFileAsync(
    string projectName,
    string relativeFilePath,
    bool includePrivateMembers = false,
    CancellationToken cancellationToken = default)
    {
        var cacheKey = includePrivateMembers ? $"{relativeFilePath}::private" : relativeFilePath;

        var cached = await _cache.GetAsync(projectName, cacheKey);
        if (cached != null)
        {
            _logger.LogDebug("Cache HIT analysis: {Project}:{Path} private={Flag}",
                projectName, relativeFilePath, includePrivateMembers);
            return cached;
        }

        _logger.LogDebug("Cache MISS analysis: {Project}:{Path} private={Flag} — live parse",
            projectName, relativeFilePath, includePrivateMembers);

        var analysis = await _inner.AnalyzeCSharpFileAsync(
            projectName, relativeFilePath, includePrivateMembers, cancellationToken);

        if (includePrivateMembers)
        {
            // Write scoped key directly — don't pass to BackfillAsync which would poison the index
            _ = Task.Run(async () =>
            {
                try
                {
                    await _cache.SetAsync(projectName, cacheKey, analysis);
                    _logger.LogDebug("Cache backfilled private analysis: {Project}:{Path}", projectName, relativeFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Cache backfill (private) failed: {Project}:{Path}", projectName, relativeFilePath);
                }
            }, CancellationToken.None);
        }
        else
        {
            _ = BackfillAsync(projectName, relativeFilePath, analysis, cancellationToken: CancellationToken.None);
        }

        return analysis;
    }

    public async Task<MethodImplementationInfo> FetchMethodImplementationAsync(
        string projectName,
        string relativeFilePath,
        string methodName,
        string? className = null,
        CancellationToken cancellationToken = default)
    {
        var batch = await FetchMethodImplementationsBatchAsync(
            projectName, relativeFilePath, [methodName], className, cancellationToken);

        return batch[0];
    }

    public async Task<List<MethodImplementationInfo>> FetchMethodImplementationsBatchAsync(
        string projectName,
        string relativeFilePath,
        string[] methodNames,
        string? className = null,
        CancellationToken cancellationToken = default)
    {
        var methodCache = await _cache.GetMethodsAsync(projectName, relativeFilePath);

        if (methodCache != null)
        {
            _logger.LogDebug("Cache HIT methods: {Project}:{Path}", projectName, relativeFilePath);
            return ResolveMethods(methodCache, methodNames, className, relativeFilePath);
        }

        _logger.LogDebug("Cache MISS methods: {Project}:{Path} — live parse", projectName, relativeFilePath);

        // Live parse — returns all methods, we filter below
        var all = await _inner.FetchMethodImplementationsBatchAsync(
            projectName, relativeFilePath, methodNames, className, cancellationToken);

        // Backfill: fetch ALL methods in the file so the cache is complete for future callers
        _ = BackfillMethodsAsync(projectName, relativeFilePath, cancellationToken: CancellationToken.None);

        return all;
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static List<MethodImplementationInfo> ResolveMethods(
        Dictionary<string, MethodImplementationInfo> methodCache,
        string[] methodNames,
        string? className,
        string filePath)
    {
        var results = new List<MethodImplementationInfo>(methodNames.Length);
        var notFound = new List<string>();

        foreach (var name in methodNames)
        {
            // Find all cache entries matching MethodName (and optionally ClassName)
            // Key format: "ClassName::MethodName::LineNumber"
            var matches = methodCache.Values
                .Where(m => m.MethodName == name &&
                            (string.IsNullOrWhiteSpace(className) || m.ClassName == className))
                .ToList();

            if (matches.Count == 0)
            {
                notFound.Add(name);
                continue;
            }

            if (matches.Count > 1 && string.IsNullOrWhiteSpace(className))
            {
                var classNames = matches.Select(m => m.ClassName).Distinct();
                throw new ArgumentException(
                    $"Multiple methods named '{name}' found in classes: {string.Join(", ", classNames)}. " +
                    $"Please specify the className parameter.");
            }

            results.AddRange(matches);
        }

        if (notFound.Count > 0)
        {
            var available = methodCache.Values
                .Select(m => $"{m.ClassName}.{m.MethodName}")
                .Distinct()
                .OrderBy(n => n);

            throw new ArgumentException(
                $"Methods not found: {string.Join(", ", notFound)}.\n" +
                $"Available in '{filePath}': {string.Join(", ", available)}");
        }

        return results;
    }

    /// <summary>
    /// Writes analysis + method bodies to cache as a fire-and-forget.
    /// Fetches all methods in the file for a complete cache population.
    /// </summary>
    private async Task BackfillAsync(
        string projectName,
        string relativeFilePath,
        CSharpFileAnalysis analysis,
        CancellationToken cancellationToken)
    {
        try
        {
            await _cache.SetAsync(projectName, relativeFilePath, analysis);
            await _cache.AddToIndexAsync(projectName, relativeFilePath);
            _logger.LogDebug("Cache backfilled analysis: {Project}:{Path}", projectName, relativeFilePath);

            await BackfillMethodsAsync(projectName, relativeFilePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache backfill failed: {Project}:{Path}", projectName, relativeFilePath);
        }
    }

    /// <summary>
    /// Fetches every method in the file from live Roslyn and writes to method cache.
    /// Called both from analysis backfill and method-cache miss paths.
    /// </summary>
    private async Task BackfillMethodsAsync(
        string projectName,
        string relativeFilePath,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get all method names from file analysis to build a complete cache entry
            var analysis = await _cache.GetAsync(projectName, relativeFilePath)
                        ?? await _inner.AnalyzeCSharpFileAsync(
                               projectName, relativeFilePath,
                               includePrivateMembers: true, cancellationToken);

            var allMethodNames = analysis.Classes
                .SelectMany(c => c.Methods.Select(m => (Class: c.Name, Method: m.Name)))
                .ToArray();

            if (allMethodNames.Length == 0) return;

            // Fetch all method implementations in one Roslyn parse pass
            var allImpls = await _inner.FetchMethodImplementationsBatchAsync(
                projectName, relativeFilePath,
                allMethodNames.Select(x => x.Method).Distinct().ToArray(),
                className: null, cancellationToken);

            // Build lookup: "ClassName::MethodName" for methods that appear in multiple classes,
            // plain "MethodName" for unique ones — matches ResolveMethods lookup strategy
            var methodNameCounts = allImpls
                .GroupBy(m => m.MethodName)
                .ToDictionary(g => g.Key, g => g.Count());

            var dict = allImpls.ToDictionary(
                m => methodNameCounts[m.MethodName] > 1
                    ? $"{m.ClassName}::{m.MethodName}"
                    : m.MethodName,
                m => m);

            await _cache.SetMethodsAsync(projectName, relativeFilePath, dict);
            _logger.LogDebug("Cache backfilled {Count} methods: {Project}:{Path}",
                dict.Count, projectName, relativeFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Method cache backfill failed: {Project}:{Path}",
                projectName, relativeFilePath);
        }
    }

    // ── Pass-through delegates ───────────────────────────────────────────────

    public Task<FileContentResponse> ReadFileContentAsync(
        string projectName, string relativeFilePath,
        CancellationToken cancellationToken = default)
        => _inner.ReadFileContentAsync(projectName, relativeFilePath, cancellationToken);

    public IReadOnlyDictionary<string, string> GetAvailableProjects()
        => _inner.GetAvailableProjects();

    public IReadOnlyDictionary<string, ProjectInfo> GetAvailableProjectsWithInfo()
        => _inner.GetAvailableProjectsWithInfo();

    public Task<string> GetProjectSkeletonAsync(
        string projectName, string? sinceTimestamp = null,
        CancellationToken cancellationToken = default)
        => _inner.GetProjectSkeletonAsync(projectName, sinceTimestamp, cancellationToken);

    public Task<FolderSearchResponse> SearchFolderFilesAsync(
        string projectName, string folderPath, string? searchPattern = null,
        int page = 1, int pageSize = 50,
        CancellationToken cancellationToken = default)
        => _inner.SearchFolderFilesAsync(projectName, folderPath, searchPattern, page, pageSize, cancellationToken);

    public string GetToolDescription() => _inner.GetToolDescription();
}