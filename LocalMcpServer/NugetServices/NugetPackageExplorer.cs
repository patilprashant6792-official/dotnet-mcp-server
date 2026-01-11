using Microsoft.Extensions.Logging;
using NuGetExplorer.Extensions;

namespace NuGetExplorer.Services;

public class NuGetPackageExplorer : INuGetPackageExplorer
{
    private readonly INuGetPackageLoader _loader;
    private readonly ILogger<NuGetPackageExplorer> _logger;

    public NuGetPackageExplorer(
        INuGetPackageLoader loader,
        ILogger<NuGetPackageExplorer> logger)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<string>> GetNamespaces(
        string packageId,
        string? version = null,
        string? targetFramework = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        }

        try
        {
            var metadata = await _loader.LoadPackageMetadata(
                packageId,
                version,
                targetFramework,
                includePrerelease,
                cancellationToken);

            return metadata.Namespaces;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get namespaces for package: {PackageId}@{Version}",
                packageId, version ?? "latest");
            throw;
        }
    }

    public async Task<string> FilterMetadataByNamespace(
        string packageId,
        string @namespace,
        string? version = null,
        string? targetFramework = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        }

        if (string.IsNullOrWhiteSpace(@namespace))
        {
            throw new ArgumentException("Namespace cannot be null or empty", nameof(@namespace));
        }

        try
        {
            var metadata = await _loader.LoadPackageMetadata(
                packageId,
                version,
                targetFramework,
                includePrerelease,
                cancellationToken);

            var namespaceMetadata = metadata.MetadataByNamespace.GetValueOrDefault(
                @namespace,
                new NamespaceMetadata());

            return namespaceMetadata.FormatAsMarkdown(@namespace, packageId, version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to filter metadata for package: {PackageId}@{Version}, namespace: {Namespace}",
                packageId, version ?? "latest", @namespace);
            throw;
        }
    }
}