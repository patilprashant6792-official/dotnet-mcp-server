using MCP.Core.DotnetCliService;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text.Json;

namespace RisingTideAI.Trade.MCP.Host.MCPServers;

[McpServerToolType]
public sealed class DotnetCommandTools(IDotnetCliService cli)
{
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private static readonly HashSet<string> _valuedFlags = ["--target", "--page", "--page-size"];

    [McpServerTool]
    [Description(
        "Builds a project (dotnet clean+build) and returns paginated Roslyn diagnostics.\n" +
        "Errors only by default (Visual Studio Error List behaviour). Use --warnings to include warnings.\n\n" +
        "FLAGS: --no-clean | --warnings | --page <n> | --page-size <n> | --target <path>\n\n" +
        "--no-clean: skips clean, incremental MSBuild (~5x faster). Use when iterating on a fix.\n" +
        "           Always start with full build first. If --no-clean shows 0 errors but full build shows errors, trust full build (stale cache).\n\n" +
        "RETURNS: success, summary, totalDiagnostics, page, pageSize, totalPages, diagnostics[]\n" +
        "  diagnostic fields: severity, code, file, line, column, message")]
    public async Task<string> ExecuteDotnetCommand(
        string projectName,
        string command,
        string[]? args = null)
    {
        args ??= [];
        var target          = ExtractFlag(args, "--target");
        var pageStr         = ExtractFlag(args, "--page");
        var pageSizeStr     = ExtractFlag(args, "--page-size");
        var includeWarnings = args.Any(a => a.Equals("--warnings",  StringComparison.OrdinalIgnoreCase));
        var noClean         = args.Any(a => a.Equals("--no-clean",  StringComparison.OrdinalIgnoreCase));
        var page            = int.TryParse(pageStr,     out var p) ? p : 1;
        var pageSize        = int.TryParse(pageSizeStr, out var s) ? s : 50;

        var result = command.ToLowerInvariant() switch
        {
            "build"       => await cli.BuildAsync(projectName, target, page, pageSize, includeWarnings, clean: !noClean),
            "add-package" => await HandleAddPackage(projectName, args),
            _             => throw new ArgumentException(
                $"Unknown command '{command}'. Supported: build, add-package")
        };

        return JsonSerializer.Serialize(result, _json);
    }

    private Task<DotnetCommandResult> HandleAddPackage(string projectName, string[] args)
    {
        var filtered = FilterFlagArgs(args);
        if (filtered.Length == 0)
            throw new ArgumentException("add-package requires args[0] = PackageId.");
        var packageId = filtered[0].Trim();
        var version   = filtered.Length > 1 ? filtered[1].Trim() : null;
        return cli.AddPackageAsync(projectName, packageId, version);
    }

    private static string? ExtractFlag(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static string[] FilterFlagArgs(string[] args)
    {
        var result = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--warnings",  StringComparison.OrdinalIgnoreCase)) continue;
            if (args[i].Equals("--no-clean",  StringComparison.OrdinalIgnoreCase)) continue;
            if (_valuedFlags.Contains(args[i], StringComparer.OrdinalIgnoreCase))
            { i++; continue; }
            result.Add(args[i]);
        }
        return [.. result];
    }
}
