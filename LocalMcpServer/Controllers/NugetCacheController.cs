using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace MCP.Host.Controllers;

[ApiController]
[Route("api/nuget-cache")]
public class NugetCacheController : ControllerBase
{
    // NuGet cache keys are stored as: {packageId}@{version}@{framework}
    // Analysis/methods/index keys use ":" — so *@*@* exclusively matches NuGet entries.
    private const string KeyPattern = "*@*@*";

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<NugetCacheController> _logger;

    public NugetCacheController(
        IConnectionMultiplexer redis,
        ILogger<NugetCacheController> logger)
    {
        _redis  = redis  ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>List all cached NuGet packages with remaining TTL</summary>
    [HttpGet]
    public async Task<IActionResult> GetCachedPackages()
    {
        try
        {
            var db     = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys   = server.Keys(pattern: KeyPattern).ToList();

            var tasks = keys.Select(async k =>
            {
                var ttl = await db.KeyTimeToLiveAsync(k);
                return BuildEntry((string)k!, ttl);
            });

            var entries = await Task.WhenAll(tasks);
            return Ok(entries.OrderBy(e => e.PackageId).ThenBy(e => e.Version));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list NuGet cache entries");
            return StatusCode(500, new { error = "Failed to list cache entries" });
        }
    }

    /// <summary>Delete a single cached package by its full cache key</summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> DeleteEntry(string key)
    {
        try
        {
            var db      = _redis.GetDatabase();
            var deleted = await db.KeyDeleteAsync(key);
            if (!deleted)
                return NotFound(new { error = $"Cache key '{key}' not found" });

            _logger.LogInformation("NuGet cache entry deleted: {Key}", key);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete NuGet cache entry {Key}", key);
            return StatusCode(500, new { error = "Failed to delete cache entry" });
        }
    }

    /// <summary>Clear all NuGet package cache entries</summary>
    [HttpDelete]
    public async Task<IActionResult> ClearAll()
    {
        try
        {
            var db     = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints().First());
            var keys   = server.Keys(pattern: KeyPattern).ToArray();

            if (keys.Length == 0)
                return Ok(new { cleared = 0 });

            var deleted = await db.KeyDeleteAsync(keys);
            _logger.LogInformation("NuGet cache cleared: {Count} entries removed", deleted);
            return Ok(new { cleared = (int)deleted });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear NuGet cache");
            return StatusCode(500, new { error = "Failed to clear cache" });
        }
    }

    private static NugetCacheEntry BuildEntry(string key, TimeSpan? ttl)
    {
        var parts     = key.Split('@');
        var packageId = parts.Length > 0 ? parts[0] : key;
        var version   = parts.Length > 1 ? parts[1] : "unknown";
        var framework = parts.Length > 2 ? parts[2] : "unknown";
        var expiresAt = ttl.HasValue ? DateTime.UtcNow.Add(ttl.Value) : (DateTime?)null;

        return new NugetCacheEntry
        {
            Key       = key,
            PackageId = packageId,
            Version   = version,
            Framework = framework,
            ExpiresAt = expiresAt
        };
    }
}

public class NugetCacheEntry
{
    public string    Key       { get; init; } = string.Empty;
    public string    PackageId { get; init; } = string.Empty;
    public string    Version   { get; init; } = string.Empty;
    public string    Framework { get; init; } = string.Empty;
    public DateTime? ExpiresAt { get; init; }
}
