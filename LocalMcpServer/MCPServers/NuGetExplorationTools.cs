using MCP.Core.Services;
using ModelContextProtocol.Server;
using NRedisStack.Search;
using NuGet.Packaging.Signing;
using NuGetExplorer.Services;
using System.ComponentModel;
using System.Runtime.Intrinsics.X86;

namespace RisingTideAI.Trade.MCP.Host.MCPServers;

/// <summary>
/// NuGet package discovery and API exploration toolkit.
/// 
/// WHY THIS EXISTS:
/// - Third-party library APIs change constantly between versions
/// - AI models have outdated knowledge of library signatures
/// - Prevents hallucinated method names, wrong parameters, deprecated APIs
/// - Provides production-ready, copy-paste C# signatures
/// 
/// CRITICAL WORKFLOW (3-STEP PROCESS):
/// 1. search_nu_get_packages → Find package by name/keywords
/// 2. get_nu_get_package_namespaces → Discover ALL namespaces in package
/// 3. get_namespace_summary → Get complete type signatures (classes/methods/properties)
/// 4. (Optional) get_method_overloads → Expand collapsed overloads
/// 
/// TOKEN EFFICIENCY:
/// - Each namespace exploration costs ~2000-5000 tokens
/// - USE MAXIMUM ONCE PER CONVERSATION per package
/// - Cache results mentally - don't re-explore same namespace
/// - Batch namespace exploration when possible
/// 
/// WHEN TO USE:
/// - User mentions ANY third-party library (Serilog, Entity Framework, Newtonsoft.Json)
/// - Before writing code using NuGet packages
/// - When user asks "how to use X library"
/// - To verify method signatures before suggesting code
/// - NEVER assume API knowledge - always verify current version
/// 
/// WHEN NOT TO USE:
/// - .NET BCL types (System.*, Microsoft.Extensions.*) - these are stable
/// - Custom project code - use ProjectSkeletonTools instead
/// - Already explored in current conversation - reuse cached knowledge
/// </summary>
[McpServerToolType]
public class NuGetExplorationTools
{
    private readonly INuGetSearchService _nugetService;
    private readonly INuGetPackageExplorer _packageExplorer;
    private readonly ITomlSerializerService _tomlSerializer;

    public NuGetExplorationTools(
        INuGetSearchService nugetService,
        INuGetPackageExplorer packageExplorer,
        ITomlSerializerService tomlSerializer)
    {
        _nugetService = nugetService;
        _packageExplorer = packageExplorer;
        _tomlSerializer = tomlSerializer;
    }

    [McpServerTool]
    [Description("CRITICAL: Query must be the EXACT NuGet package ID for best results.Common mistakes:"+
 "❌ WRONG: 'EntityFrameworkCore', 'Json', 'Dependency Injection'"+
  "✅ CORRECT: 'Microsoft.EntityFrameworkCore', 'Newtonsoft.Json', 'Microsoft.Extensions.DependencyInjection'"+
"Use full package IDs from NuGet.org.For keyword searches, results may be incomplete.")]
    public async Task<string> SearchNuGetPackages(
        [Description("Required: Package name or keywords (e.g., 'Serilog', 'logging', 'json serializer')")]
        string query,
        [Description("Optional: Maximum results to return (default: 20)")]
        int take = 20,
        [Description("Optional: Include prerelease packages (default: false, stable only)")]
        bool includePrerelease = false)
    {
        var results = await _nugetService.SearchPackagesAsync(query, take, includePrerelease);
        return _tomlSerializer.Serialize(results);
    }

    [McpServerTool]
    [Description("STEP 1 of NuGet Exploration: Lists ALL available namespaces in a NuGet package. " +
        "USE THIS FIRST to discover what namespaces exist before exploring types. " +
        "Example: 'Newtonsoft.Json' returns ['Newtonsoft.Json', 'Newtonsoft.Json.Linq', 'Newtonsoft.Json.Schema']. " +
        "Then call get_namespace_summary for each namespace.")]
    public async Task<string> GetNuGetPackageNamespaces(
        [Description("Required: NuGet package ID (e.g., 'Newtonsoft.Json', 'Microsoft.EntityFrameworkCore')")]
        string packageId,
        [Description("Optional: Specific version (e.g., '13.0.3'). Omit for latest stable")]
        string? version = null,
        [Description("Optional: Target framework (e.g., 'net8.0', 'net6.0'). Defaults to net10.0")]
        string? targetFramework = null,
        [Description("Optional: Include prerelease versions (default: false)")]
        bool includePrerelease = false)
    {
        try
        {
            var namespaces = await _packageExplorer.GetNamespaces(
                packageId, version, targetFramework, includePrerelease);
            return _tomlSerializer.Serialize(new { Namespaces = namespaces });
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to retrieve namespaces: {ex.Message}", ex);
        }
    }

    [McpServerTool]
    [Description("STEP 2 of NuGet Exploration: Gets complete namespace summary with C#-formatted type signatures. " +
        "Returns production-ready, copy-paste signatures for classes, interfaces, enums, structs with all methods and properties. " +
        "TOKEN-OPTIMIZED: Collapsed view with expandable overloads. " +
        "WARNING: Heavy token cost (2000-5000 tokens) - use maximum once per namespace per conversation.")]
    public async Task<string> GetNamespaceSummary(
        [Description("Required: NuGet package ID")]
        string packageId,
        [Description("Required: Namespace to explore (from get_nu_get_package_namespaces)")]
        string @namespace,
        [Description("Optional: Specific version")]
        string? version = null,
        [Description("Optional: Target framework")]
        string? targetFramework = null,
        [Description("Optional: Include prerelease")]
        bool includePrerelease = false)
    {
        try
        {
            var summary = await _packageExplorer.FilterMetadataByNamespace(
                packageId, @namespace, version, targetFramework, includePrerelease);
            return summary;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to retrieve namespace summary: {ex.Message}", ex);
        }
    }

    [McpServerTool]
    [Description("STEP 3 (OPTIONAL): Fetches ALL overloads for a specific method with complete signature details. " +
        "Use ONLY when get_namespace_summary shows '+ N overloads' and you need to see all parameter variations. " +
        "Expands collapsed method signatures into full list.")]
    public async Task<string> GetMethodOverloads(
        [Description("Required: NuGet package ID")]
        string packageId,
        [Description("Required: Namespace containing the type (e.g., 'Serilog')")]
        string @namespace,
        [Description("Required: Type name (e.g., 'ILogger', 'Log')")]
        string typeName,
        [Description("Required: Method name to expand (e.g., 'Write', 'Debug')")]
        string methodName,
        [Description("Optional: Specific version")]
        string? version = null,
        [Description("Optional: Target framework")]
        string? targetFramework = null,
        [Description("Optional: Include prerelease")]
        bool includePrerelease = false)
    {
        try
        {
            var metadata = await _packageExplorer.GetMethodOverloads(
                packageId, @namespace, typeName, methodName,
                version, targetFramework, includePrerelease);
            return metadata;
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Failed to retrieve method overloads: {ex.Message}", ex);
        }
    }
}