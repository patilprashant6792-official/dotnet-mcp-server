using MCP.Core.Configuration;
using MCP.Core.Exceptions;
using MCP.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using NullLogger = NuGet.Common.NullLogger;

namespace MCP.Core.Services;

public class NuGetSearchService : INuGetSearchService
{
    private readonly ILogger<INuGetSearchService> _logger;
    private readonly NuGetServiceConfig _config;
    private readonly string _packagesPath;
    private readonly SourceCacheContext _cache;
    private readonly SourceRepository _repository;
    private readonly SemaphoreSlim _concurrencyLimiter;
    private readonly IMemoryCache _memoryCache;
    private bool _disposed;
    
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

    public NuGetSearchService(
        ILogger<INuGetSearchService> logger,
        IOptions<NuGetServiceConfig> config,
        IMemoryCache memoryCache)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config?.Value ?? throw new ArgumentNullException(nameof(config));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _packagesPath = Path.Combine(userProfile, ".nuget", "packages");

        if (!Directory.Exists(_packagesPath))
        {
            Directory.CreateDirectory(_packagesPath);
        }

        _cache = new SourceCacheContext();
        var packageSource = new PackageSource("https://api.nuget.org/v3/index.json");
        _repository = Repository.Factory.GetCoreV3(packageSource);
        _concurrencyLimiter = new SemaphoreSlim(_config.MaxConcurrentOperations);

        _logger.LogInformation(
            "NuGetService initialized. MaxConcurrent: {MaxConcurrent}, MaxPackageSize: {MaxSize}MB, Timeout: {Timeout}s, Caching: Enabled",
            _config.MaxConcurrentOperations,
            _config.MaxPackageSizeBytes / (1024 * 1024),
            _config.OperationTimeout.TotalSeconds);
    }

    

    public async Task<List<PackageSearchResult>> SearchPackagesAsync(
     string query,
     int take = 20,
     bool includePrerelease = false,
     CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Search query cannot be empty", nameof(query));
        }

        if (take < 1 || take > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(take), "Take must be between 1 and 100");
        }

        try
        {
            var searchResource = await _repository.GetResourceAsync<PackageSearchResource>(cancellationToken);
            var searchFilter = new SearchFilter(includePrerelease: includePrerelease);

            var results = await searchResource.SearchAsync(
                query,
                searchFilter,
                skip: 0,
                take: take,
                NullLogger.Instance,
                cancellationToken);

            return results.Select(r => new PackageSearchResult
            {
                PackageId = r.Identity.Id,
                LatestVersion = r.Identity.Version.ToString(),
                Description = r.Description,
                TotalDownloads = r.DownloadCount,
                Tags = r.Tags?.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrEmpty(t))
                    .ToList() ?? new List<string>()
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching packages: {Query}", query);
            throw new NuGetServiceException($"Package search failed: {ex.Message}", ex);
        }
    }

    #region Validation

    private static bool IsTransientError(Exception ex)
    {
        return ex is HttpRequestException
            || ex is TaskCanceledException
            || ex is TimeoutException
            || (ex.InnerException != null && IsTransientError(ex.InnerException));
    }

    #endregion
}