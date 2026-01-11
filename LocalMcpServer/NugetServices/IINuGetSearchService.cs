using MCP.Core.Models;

namespace MCP.Core.Services;

public interface INuGetSearchService
{

    Task<List<PackageSearchResult>> SearchPackagesAsync(
        string query,
        int take = 20,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);
}