namespace NuGetExplorer.Services;

public interface INuGetPackageExplorer
{
    Task<List<string>> GetNamespaces(
        string packageId,
        string? version = null,
        string? targetFramework = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);

    Task<string> FilterMetadataByNamespace(
    string packageId,
    string @namespace,
    string? version = null,
    string? targetFramework = null,
    bool includePrerelease = false,
    CancellationToken cancellationToken = default);

    // NugetServices/INugetPackageExplorer.cs - Add interface method

    Task<string> GetMethodOverloads(
        string packageId,
        string @namespace,
        string typeName,
        string methodName,
        string? version = null,
        string? targetFramework = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);
}