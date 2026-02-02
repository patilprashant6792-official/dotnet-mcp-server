using MCP.Core.Configuration;
using MCP.Core.Models;
using MCP.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace RisingTideAI.Trade.MCP.Host.MCPServers;

/// <summary>
/// Global code search across single or multiple projects simultaneously.
/// 
/// WHY THIS EXISTS:
/// - Find classes, methods, properties, fields, interfaces by name or keyword
/// - WILDCARD SUPPORT: Search ALL projects at once with projectName='*'
/// - Security audits: Find all usages of sensitive APIs (Authorize, Encryption, etc.)
/// - Dependency analysis: Locate all consumers of an interface/service
/// - Refactoring impact: Find all references before renaming
/// - Architectural analysis: Track patterns across entire codebase
/// 
/// CRITICAL USE CASES:
/// 1. **Security Audits**:
///    - search_code_globally('*', 'Authorize') → Find all authorization points
///    - search_code_globally('*', 'Password') → Locate password handling
///    - search_code_globally('*', 'ConnectionString') → Find DB connection usage
/// 
/// 2. **Dependency Analysis**:
///    - search_code_globally('*', 'IUserService') → Find all service consumers
///    - search_code_globally('*', 'Redis') → Locate all Redis usage
///    - search_code_globally('RisingTideAPI', 'DbContext') → Find all EF contexts
/// 
/// 3. **Refactoring Impact**:
///    - Before renaming: Find ALL usages across ALL projects
///    - Before deleting: Verify no references exist
///    - Before changing interface: See all implementations
/// 
/// WILDCARD MODE (POWERFUL):
/// - projectName='*' → Search EVERYTHING simultaneously
/// - Returns ranked results across ALL configured projects
/// - Perfect for cross-project analysis (microservices, shared libraries)
/// 
/// FILTERING OPTIONS:
/// - memberType: 'Class', 'Interface', 'Method', 'Property', 'Field', 'All'
/// - caseSensitive: true/false (default: false for broader matches)
/// - topK: Limit results (default: 20, increase for comprehensive analysis)
/// 
/// RETURNS:
/// - Ranked results by relevance score
/// - Exact file locations (project + path)
/// - Member type (Class/Method/Property/etc.)
/// - Surrounding context (file, namespace, class)
/// 
/// TOKEN COST: ~1000-3000 tokens depending on result count
/// 
/// WHEN TO USE:
/// - User mentions "find all", "where is", "show me all"
/// - Security review tasks
/// - Understanding cross-project dependencies
/// - Before major refactoring
/// - Investigating architectural patterns
/// 
/// WHEN NOT TO USE:
/// - You already know exact file location - use CodeAnalysisTools directly
/// - Method implementation needed - use FetchMethodImplementation after finding location
/// - Understanding method callers - use MethodCallGraphTools
/// - Project structure exploration - use ProjectSkeletonTools
/// </summary>
[McpServerToolType]
public class CodeSearchTools
{
    private readonly ICodeSearchService _codeSearchService;
    private readonly ICodeSearchFormatterService _codeSearchFormatter;

    public CodeSearchTools(
        ICodeSearchService codeSearchService,
        ICodeSearchFormatterService codeSearchFormatter)
    {
        _codeSearchService = codeSearchService;
        _codeSearchFormatter = codeSearchFormatter;
    }

    [McpServerTool]
    [Description("Global code search across project(s) - finds classes, methods, properties, fields, interfaces by name or keyword. " +
        "WILDCARD SUPPORT: Pass projectName='*' to search across ALL configured projects simultaneously. " +
        "Returns ranked results with file locations and member types. " +
        "Examples: " +
        "• Single project: search_code_globally('RisingTideAPI', 'Redis') " +
        "• ALL projects: search_code_globally('*', 'Redis') ← Search everything! " +
        "Use cases: " +
        "• Security audits: search_code_globally('*', 'Authorize') " +
        "• Dependency analysis: search_code_globally('*', 'IUserService') " +
        "• Refactoring impact: Find all usages before renaming")]
    public async Task<string> SearchCodeGlobally(
        [Description("Required: Search query (class name, method name, keyword, e.g., 'Redis', 'UserService', 'Authorize')")]
        string query,

        [Description("Required: Project name OR '*' for ALL projects. Use '*' for cross-project analysis.")]
        string projectName,

        [Description("Optional: Filter by member type (Class/Interface/Method/Property/Field/All). Default: 'All'")]
        string memberType = "All",

        [Description("Optional: Case-sensitive search (default: false for broader matches)")]
        bool caseSensitive = false,

        [Description("Optional: Maximum results to return (default: 20). Increase for comprehensive analysis.")]
        int topK = 20)
    {
        try
        {
            if (!Enum.TryParse<CodeMemberType>(memberType, ignoreCase: true, out var parsedMemberType))
            {
                return $"❌ Invalid member type: '{memberType}'. Valid values: {string.Join(", ", Enum.GetNames<CodeMemberType>())}";
            }

            var request = new CodeSearchRequest
            {
                ProjectName = projectName,
                Query = query,
                MemberType = parsedMemberType,
                CaseSensitive = caseSensitive,
                TopK = topK
            };

            var response = await _codeSearchService.SearchGloballyAsync(request);
            return _codeSearchFormatter.FormatSearchResults(response);
        }
        catch (Exception ex)
        {
            return $"❌ Search failed: {ex.Message}";
        }
    }
}