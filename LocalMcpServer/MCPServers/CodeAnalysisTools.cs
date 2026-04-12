using MCP.Core.Configuration;
using MCP.Core.Models;
using MCP.Core.Services;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;

namespace RisingTideAI.Trade.MCP.Host.MCPServers;

[McpServerToolType]
public class CodeAnalysisTools
{
    private readonly IProjectSkeletonService _projectSkeletonService;
    private readonly IMarkdownFormatterService _markdownFormatter;
    private readonly IMethodFormatterService _methodFormatter;
    private readonly ITomlSerializerService _tomlSerializer;

    public CodeAnalysisTools(
        IProjectSkeletonService projectSkeletonService,
        IMarkdownFormatterService markdownFormatter,
        IMethodFormatterService methodFormatter,
        ITomlSerializerService tomlSerializer)
    {
        _projectSkeletonService = projectSkeletonService;
        _markdownFormatter = markdownFormatter;
        _methodFormatter = methodFormatter;
        _tomlSerializer = tomlSerializer;
    }

    [McpServerTool]
    [Description("PREFERRED METHOD: Analyzes C# file(s) and returns structured metadata WITHOUT loading full content. " +
    "Returns: namespace, using directives, classes, methods, properties, fields, attributes, constructor dependencies, file classification. " +
    "BATCH MODE: Pass 'File1.cs,File2.cs,File3.cs' (comma-separated, NO SPACES) to analyze multiple files efficiently (saves ~300 tokens per additional file). " +
    "Use this BEFORE fetch_method_implementation to understand structure. " +
    "TOKEN-EFFICIENT: Always prioritise this if its a class based c# file, else use read File Content,Set includePrivateMembers=true when:" +
    " - Debugging internal implementation details\r\n  - Analyzing dependency injection patterns\r\n  - Refactoring private methods\r\n  - Understanding full class architecture\r\nDefault: false (public API surface only)")]
    public async Task<string> AnalyzeCSharpFile(
    [Description("Required: Project name")]
    string projectName,
    [Description("Required: Relative path(s) to C# file from project root. " +
        "Single: 'Services/UserService.cs' | " +
        "Batch: 'Services/UserService.cs,Controllers/UserController.cs,Repositories/UserRepository.cs' (NO SPACES)")]
    string relativeFilePath,
    [Description("Optional: Include private members (default: false, public only)")]
    bool includePrivateMembers = false)
    {
        try
        {
            var filePaths = relativeFilePath
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToArray();

            if (filePaths.Length == 1)
            {
                var analysis = await _projectSkeletonService.AnalyzeCSharpFileAsync(
                    projectName,
                    filePaths[0],
                    includePrivateMembers);
                return _markdownFormatter.FormatCSharpAnalysis(analysis);
            }

            var fetchTasks = filePaths.Select(async path =>
            {
                try
                {
                    var analysis = await _projectSkeletonService.AnalyzeCSharpFileAsync(
                        projectName,
                        path,
                        includePrivateMembers);
                    return (Analysis: analysis, Error: (string?)null);
                }
                catch (FileNotFoundException)
                {
                    return (Analysis: (CSharpFileAnalysis?)null, Error: $"File not found: {path}");
                }
            });

            var results = await Task.WhenAll(fetchTasks);

            var analyses = results
                .Where(r => r.Analysis != null)
                .Select(r => r.Analysis!)
                .ToList();

            var errors = results
                .Where(r => r.Error != null)
                .Select(r => r.Error!)
                .ToList();

            if (analyses.Count == 0)
                throw new ArgumentException($"No valid files found.\n\nErrors:\n{string.Join("\n", errors)}");

            var sb = new StringBuilder();
            sb.AppendLine($"# Batch C# File Analysis: {analyses.Count} file(s)");
            sb.AppendLine();
            sb.AppendLine($"**Project:** {analyses[0].ProjectName}");
            sb.AppendLine();

            if (errors.Count > 0)
            {
                sb.AppendLine("⚠️ **Warnings:**");
                foreach (var error in errors)
                    sb.AppendLine($"- {error}");
                sb.AppendLine();
            }

            sb.AppendLine("## Files Index");
            sb.AppendLine();
            for (int i = 0; i < analyses.Count; i++)
            {
                var a = analyses[i];
                var classCount = a.Classes.Count;
                var methodCount = a.Classes.Sum(c => c.Methods.Count);
                sb.AppendLine($"{i + 1}. **{a.FileName}** → {classCount} class{(classCount != 1 ? "es" : "")}, {methodCount} method{(methodCount != 1 ? "s" : "")}");
            }
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();

            for (int i = 0; i < analyses.Count; i++)
            {
                sb.AppendLine($"## File {i + 1}: `{analyses[i].FileName}`");
                sb.AppendLine();

                var formatted = _markdownFormatter.FormatCSharpAnalysis(analyses[i]);

                var lines = formatted.Split('\n');
                var contentStart = Array.FindIndex(lines, l => l.StartsWith("**Project:**"));
                sb.AppendLine(contentStart > 0
                    ? string.Join("\n", lines.Skip(contentStart))
                    : formatted);

                if (i < analyses.Count - 1)
                {
                    sb.AppendLine();
                    sb.AppendLine("---");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }
        catch (KeyNotFoundException)
        {
            var availableProjects = _projectSkeletonService.GetAvailableProjectsWithInfo();
            var projectList = string.Join("\n", availableProjects.Select(p =>
                $"• {p.Key} - {p.Value.Description}"));
            throw new ArgumentException(
                $"Project '{projectName}' not found.\n\nAvailable projects:\n{projectList}");
        }
        catch (FileNotFoundException ex)
        {
            throw new ArgumentException($"File not found: {ex.Message}");
        }
    }

    [McpServerTool]
    [Description("PREFERRED METHOD: Fetches complete method implementation(s) with line numbers. " +
        "Returns: signature, full body with line numbers, attributes, XML documentation. " +
        "BATCH MODE: Pass 'Method1,Method2,Method3' (comma-separated, NO SPACES) to fetch multiple methods efficiently (saves ~500 tokens per additional method). " +
        "Use AFTER analyze_c_sharp_file to drill into specific methods. " +
        "CRITICAL: Returns line numbers for precise code change suggestions (e.g., 'replace lines 45-60 with...')")]
    public async Task<string> FetchMethodImplementation(
        [Description("Required: Project name")]
        string projectName,
        [Description("Required: Relative path to C# file from project root (e.g., 'Services/UserService.cs')")]
        string relativeFilePath,
        [Description("Required: Method name(s) to fetch. " +
            "Single: 'GetUsers' | " +
            "Batch: 'GetUsers,UpdateUser,DeleteUser' (NO SPACES)")]
        string methodName,
        [Description("Optional: Class name if file has multiple classes with same method name")]
        string? className = null)
    {
        try
        {
            var methodNames = methodName.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (methodNames.Length == 1)
            {
                var implementation = await _projectSkeletonService.FetchMethodImplementationAsync(
                    projectName, relativeFilePath, methodNames[0], className);

                return _methodFormatter.FormatMethodImplementation(implementation);
            }
            else
            {
                var implementations = await _projectSkeletonService.FetchMethodImplementationsBatchAsync(
                    projectName, relativeFilePath, methodNames, className);

                return _methodFormatter.FormatMethodImplementationsBatch(implementations);
            }
        }
        catch (KeyNotFoundException)
        {
            var availableProjects = _projectSkeletonService.GetAvailableProjectsWithInfo();
            var projectList = string.Join("\n", availableProjects.Select(p =>
                $"• {p.Key} - {p.Value.Description}"));

            throw new ArgumentException(
                $"Project '{projectName}' not found.\n\n" +
                $"Available projects:\n{projectList}");
        }
        catch (FileNotFoundException ex)
        {
            throw new ArgumentException($"File not found: {ex.Message}");
        }
        catch (ArgumentException)
        {
            throw;
        }
    }

    [McpServerTool]
    [Description(
        "LAST RESORT: Reads raw file content for source code and safe configuration files.\n" +
        "SECURITY: Blocks sensitive files (appsettings.json, secrets, credentials, .env).\n" +
        "DECISION RULE: Use ONLY for files ≤15 KB or non-C# files (Dockerfile, launchSettings.json).\n" +
        "For C# files >15 KB, use analyze_c_sharp_file + fetch_method_implementation instead (massive token savings).\n\n" +
        "MODES (mutually exclusive — pick one):\n" +
        "  • Full file      — omit all optional params (current default behaviour)\n" +
        "  • Line range     — provide startLine + endLine to fetch a specific slice (1-based, inclusive)\n" +
        "  • Search / grep  — provide query to return all matching lines with their line numbers (case-insensitive)\n\n" +
        "EXAMPLES:\n" +
        "  read_file_content(proj, 'Program.cs')                          → full file\n" +
        "  read_file_content(proj, 'Program.cs', startLine=10, endLine=40) → lines 10-40 only\n" +
        "  read_file_content(proj, 'Program.cs', query='AddSingleton')    → all lines containing 'AddSingleton'\n\n" +
        "RETURNS: numbered lines. Range/search modes save tokens when you only need part of a file.")]
    public async Task<string> ReadFileContent(
        [Description("Required: Project name")]
        string projectName,
        [Description("Required: Relative file path from project root (e.g., 'Program.cs', 'Dockerfile'). " +
            "⚠️ BLOCKED: appsettings.json, secrets.json, .env, credentials, bin/, obj/, node_modules/")]
        string relativeFilePath,
        [Description("Optional (range mode): First line to return, 1-based inclusive. Must be paired with endLine.")]
        int? startLine = null,
        [Description("Optional (range mode): Last line to return, 1-based inclusive. Must be paired with startLine.")]
        int? endLine = null,
        [Description("Optional (search mode): Case-insensitive substring. Returns only lines that contain this text, each prefixed with its line number. Mutually exclusive with startLine/endLine.")]
        string? query = null)
    {
        // Guard: mutually exclusive modes
        if (query != null && (startLine != null || endLine != null))
            return "Error: 'query' and 'startLine'/'endLine' are mutually exclusive. Use one mode at a time.";

        // Guard: startLine/endLine must be paired
        if ((startLine == null) != (endLine == null))
            return "Error: 'startLine' and 'endLine' must both be provided together.";

        try
        {
            var result = await _projectSkeletonService.ReadFileContentAsync(projectName, relativeFilePath);
            var rawLines = result.RawContent.Split('\n');
            var totalLines = rawLines.Length;

            // ── Search mode ────────────────────────────────────────────────
            if (query != null)
            {
                var sb = new System.Text.StringBuilder();
                var matchCount = 0;

                for (var i = 0; i < totalLines; i++)
                {
                    if (rawLines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.Append($"{i + 1,4} | {rawLines[i].TrimEnd('\r')}\n");
                        matchCount++;
                    }
                }

                return matchCount == 0
                    ? $"No lines found matching '{query}' in {relativeFilePath} ({totalLines} lines searched)."
                    : $"// {matchCount} match(es) for '{query}' in {relativeFilePath}\n" + sb;
            }

            // ── Range mode ─────────────────────────────────────────────────
            if (startLine != null)
            {
                var start = startLine.Value;
                var end = endLine!.Value;

                if (start < 1 || end < 1)
                    return "Error: 'startLine' and 'endLine' must be >= 1.";

                if (start > end)
                    return $"Error: 'startLine' ({start}) must be <= 'endLine' ({end}).";

                if (start > totalLines)
                    return $"Error: 'startLine' ({start}) exceeds file length ({totalLines} lines).";

                // Clamp end to actual file length (lenient — no error for over-read)
                end = Math.Min(end, totalLines);

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"// Lines {start}-{end} of {totalLines} — {relativeFilePath}");
                for (var i = start - 1; i < end; i++)
                    sb.Append($"{i + 1,4} | {rawLines[i].TrimEnd('\r')}\n");

                return sb.ToString();
            }

            // ── Full file mode (default) ────────────────────────────────────
            var output = new System.Text.StringBuilder();
            for (var i = 0; i < totalLines; i++)
                output.Append($"{i + 1,4} | {rawLines[i].TrimEnd('\r')}\n");
            return output.ToString();
        }
        catch (FileAccessDeniedException ex)
        {
            var errorResult = new
            {
                Success = false,
                ErrorType = "FileAccessDenied",
                ProjectName = projectName,
                FilePath = relativeFilePath,
                Reason = ex.Reason,
                Message = ex.Message,
                Suggestions = new[]
                {
                    "This file contains sensitive data and cannot be accessed for security reasons.",
                    "If you need configuration structure (not values), use 'get_project_skeleton' instead.",
                    "For code analysis, use 'analyze_c_sharp_file' for semantic understanding.",
                    "Allowed file types: .cs, .csproj, .md, .txt, Program.cs, Dockerfile, etc.",
                    "Blocked files: appsettings.json, secrets.json, .env, credential files, database files"
                }
            };

            return _tomlSerializer.Serialize(errorResult);
        }
        catch (FileNotFoundException ex)
        {
            var errorResult = new
            {
                Success = false,
                ErrorType = "FileNotFound",
                ProjectName = projectName,
                FilePath = relativeFilePath,
                Message = ex.Message,
                Suggestions = new[]
                {
                    "Use 'get_project_skeleton' to see all available files in the project.",
                    "Verify the file path is correct and uses forward slashes (/) or backslashes (\\\\).",
                    "Check if the file exists in the project directory."
                }
            };

            return _tomlSerializer.Serialize(errorResult);
        }
    }
}
