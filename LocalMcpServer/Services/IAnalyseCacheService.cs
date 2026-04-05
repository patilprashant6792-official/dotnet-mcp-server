using MCP.Core.Configuration;
using MCP.Core.Models;

namespace MCP.Core.Services;

public interface IAnalysisCacheService
{
    /// <summary>Retrieves a cached file analysis. Returns null on miss.</summary>
    Task<CSharpFileAnalysis?> GetAsync(string projectName, string relativePath);

    /// <summary>Stores a file analysis with the configured TTL.</summary>
    Task SetAsync(string projectName, string relativePath, CSharpFileAnalysis analysis);

    /// <summary>Removes a single file's analysis and method cache (e.g. on deletion).</summary>
    Task DeleteAsync(string projectName, string relativePath);

    /// <summary>Returns all relative paths currently indexed for a project.</summary>
    Task<IReadOnlyList<string>> GetIndexAsync(string projectName);

    /// <summary>Replaces the full file index for a project atomically.</summary>
    Task SetIndexAsync(string projectName, IEnumerable<string> relativePaths);

    /// <summary>Removes a path from the project index without clearing other entries.</summary>
    Task RemoveFromIndexAsync(string projectName, string relativePath);

    /// <summary>Adds a path to the project index.</summary>
    Task AddToIndexAsync(string projectName, string relativePath);

    /// <summary>
    /// Removes ALL cached analysis keys and the index for a project.
    /// Call this when a project is deleted from config.
    /// </summary>
    Task PurgeProjectAsync(string projectName);

    // ── Method-body cache ───────────────────────────────────────────────────

    /// <summary>Returns true if a method cache entry exists for the file. Cheaper than GetMethodsAsync — no deserialization.</summary>
    Task<bool> MethodsExistAsync(string projectName, string relativePath);

    /// <summary>
    /// Retrieves all cached method implementations for a file.
    /// Key is method name. Returns null on miss.
    /// </summary>
    Task<Dictionary<string, MethodImplementationInfo>?> GetMethodsAsync(string projectName, string relativePath);

    /// <summary>
    /// Stores all method implementations for a file with the configured TTL.
    /// </summary>
    Task SetMethodsAsync(string projectName, string relativePath,
        Dictionary<string, MethodImplementationInfo> methods);
}
