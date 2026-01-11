namespace NuGetExplorer.Services;

public interface INuGetPackageLoader
{
    Task<PackageMetadata> LoadPackageMetadata(
        string packageId,
        string? version = null,
        string? targetFramework = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);
}