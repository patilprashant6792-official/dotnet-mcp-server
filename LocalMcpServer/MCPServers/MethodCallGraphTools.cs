using MCP.Core.Services;
using ModelContextProtocol.Server;
using StackExchange.Redis;
using System.ComponentModel;
using System.Reflection.Metadata;
using System.Threading.Channels;

namespace RisingTideAI.Trade.MCP.Host.MCPServers;

/// <summary>
/// Method dependency analysis - WHO calls this method and WHERE.
/// 
/// WHY THIS EXISTS:
/// - CRITICAL for understanding impact before modifying methods
/// - Prevents breaking changes by revealing hidden dependencies
/// - Shows exact caller locations (file path, line number, class)
/// - Identifies refactoring ripple effects
/// 
/// WHEN TO USE (HIGH PRIORITY):
/// 1. Before modifying public method signatures
/// 2. Before deleting methods (ensure nothing calls it)
/// 3. Before changing method behavior (understand all usage contexts)
/// 4. Analyzing architectural dependencies (controller → service → repository)
/// 5. Finding "God methods" called from everywhere
/// 
/// WHAT IT RETURNS:
/// - Caller file paths (exact location)
/// - Line numbers where method is called
/// - Calling class names (with resolution hints)
/// - Test file callers (optional, disabled by default)
/// - Class resolution hints for fetching caller implementations
/// 
/// WORKFLOW INTEGRATION:
/// Step 1: analyze_method_call_graph → Identify all callers
/// Step 2: For each caller → fetch_method_implementation → Understand caller context
/// Step 3: Modify original method + update all callers if needed
/// 
/// REAL-WORLD EXAMPLE:
/// You want to add a parameter to UserService.GetUser(int id)
/// 1. analyze_method_call_graph('UserService.cs', 'GetUser')
///    → Returns: UserController.cs:45, AdminController.cs:89, UserRepository.cs:120
/// 2. fetch_method_implementation for each caller → See how GetUser is currently used
/// 3. Safely add parameter, update all 3 callers with correct context
/// 
/// TOKEN COST: ~500-2000 tokens depending on method popularity
/// 
/// WHEN NOT TO USE:
/// - Understanding method implementation - use CodeAnalysisTools.FetchMethodImplementation
/// - Finding where method is defined - use CodeSearchTools
/// - Analyzing private methods with no external callers (waste of tokens)
/// </summary>
[McpServerToolType]
public class MethodCallGraphTools
{
    private readonly IMethodCallGraphService _callGraphService;
    private readonly IMethodFormatterService _methodFormatter;

    public MethodCallGraphTools(
        IMethodCallGraphService callGraphService,
        IMethodFormatterService methodFormatter)
    {
        _callGraphService = callGraphService;
        _methodFormatter = methodFormatter;
    }

    [McpServerTool]
    [Description("Analyzes method call graph - shows WHO calls this method and WHERE exactly. CRITICAL for understanding impact before modifying methods. Returns caller locations with exact file paths, line numbers, and class resolution hints. Use this BEFORE:"
 +" - Changing method signatures(prevents breaking changes)"
 +" - Modifying method behavior(impact analysis)"
 +" - Deleting methods(dependency verification)"
 +" - Renaming methods(find all references)"
+"Example: Before changing GetUser() signature, verify no other services depend on specific parameter order.")]
    public async Task<string> AnalyzeMethodCallGraph(
        [Description("Required: Project name")]
        string projectName,

        [Description("Required: Relative file path where method is defined (e.g., 'Services/UserService.cs')")]
        string relativeFilePath,

        [Description("Required: Method name to analyze (e.g., 'GetUser', 'ProcessOrder')")]
        string methodName,

        [Description("Optional: Class name if file has multiple classes")]
        string? className = null,

        [Description("Optional: Include test files in analysis (default: false, production code only)")]
        bool includeTests = false)
    {
        try
        {
            var graph = await _callGraphService.AnalyzeMethodDependenciesAsync(
                projectName,
                relativeFilePath,
                methodName,
                className,
                includeTests,
                depth: 1);

            return _methodFormatter.FormatMethodCallGraph(graph);
        }
        catch (Exception ex)
        {
            return $"❌ Error analyzing method call graph: {ex.Message}";
        }
    }
}