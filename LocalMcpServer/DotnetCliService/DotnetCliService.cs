using MCP.Core.Services;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace MCP.Core.DotnetCliService;

public sealed partial class DotnetCliService(IProjectConfigService config, ILogger<DotnetCliService> logger)
    : IDotnetCliService
{
    // MSBuild diagnostic line:
    // src\File.cs(12,5): error CS0246: The type ... [...\Project.csproj]
    [GeneratedRegex(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s*(?<sev>error|warning)\s+(?<code>\w+):\s*(?<msg>.+?)(?:\s+\[.+\])?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DiagnosticLineRegex();

    // ── public API ────────────────────────────────────────────────────────────

    public async Task<DotnetCommandResult> BuildAsync(
        string projectName,
        string? buildTarget = null,
        int page = 1,
        int pageSize = 50,
        bool includeWarnings = false,
        bool clean = true,
        CancellationToken ct = default)
    {
        var root   = ResolveRoot(projectName);
        var target = ResolveBuildTarget(root, buildTarget);

        if (target is null)
        {
            var available = DiscoverBuildTargets(root);
            return new DotnetCommandResult
            {
                Success          = false,
                Command          = "dotnet build",
                DurationMs       = 0,
                Summary          = "Ambiguous build target: multiple .csproj files found and no solution file. Re-call with args: ['--target', '<relative-path>']",
                AvailableTargets = available
            };
        }

        var relative = Path.GetRelativePath(root, target).Replace('\\', '/');

        if (clean)
        {
            var label    = $"dotnet clean {Path.GetFileName(target)}";
            var cleanPsi = new ProcessStartInfo("dotnet", $"clean \"{target}\" --nologo")
            {
                WorkingDirectory       = root,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };
            using var cleanProc = Process.Start(cleanPsi)
                ?? throw new InvalidOperationException("Failed to start dotnet clean process.");
            cleanProc.BeginOutputReadLine();
            cleanProc.BeginErrorReadLine();
            await cleanProc.WaitForExitAsync(ct);
            logger.LogInformation("[dotnet] {Command} done", label);
        }

        var cliArgs  = $"build \"{target}\" --nologo";
        var buildLabel = clean
            ? $"dotnet clean + build {Path.GetFileName(target)}"
            : $"dotnet build {Path.GetFileName(target)}";
        return await RunBuildAsync(cliArgs, root, buildLabel, relative, page, pageSize, includeWarnings, ct);
    }




    public Task<DotnetCommandResult> AddPackageAsync(
        string projectName, string packageId, string? version, CancellationToken ct = default)
    {
        ValidatePackageId(packageId);
        if (version is not null) ValidateVersion(version);

        var root   = ResolveRoot(projectName);
        var csproj = ResolveSingleCsproj(root);
        var versionArg = version is not null ? $" --version {version}" : string.Empty;
        var args   = $"add \"{csproj}\" package {packageId}{versionArg}";
        var label  = $"dotnet add package {packageId}{versionArg.Trim()}";
        return RunAsync(args, root, label, resolvedTarget: null, ParsePackageOutput, ct);

    }

    // ── core runner ───────────────────────────────────────────────────────────


    private async Task<DotnetCommandResult> RunAsync(
        string args,
        string workingDir,
        string label,
        string? resolvedTarget,
        Func<string, string, (bool success, string summary, List<DotnetDiagnostic> diagnostics)> parser,
        CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo("dotnet", args)
        {
            WorkingDirectory    = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute    = false,
            CreateNoWindow     = true
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start dotnet process.");

            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(cts.Token);
            sw.Stop();

            var combined = stdout.ToString() + stderr.ToString();
            logger.LogInformation("[dotnet] {Command} exited {Code} in {Ms}ms", label, proc.ExitCode, sw.ElapsedMilliseconds);

            var (success, summary, diagnostics) = parser(combined, label);
            return new DotnetCommandResult
            {
                Success        = success,
                Command        = label,
                DurationMs     = sw.ElapsedMilliseconds,
                Summary        = summary,
                Diagnostics    = diagnostics,
                ResolvedTarget = resolvedTarget
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return Fail(label, sw.ElapsedMilliseconds, "Command timed out after 120 seconds.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[dotnet] Failed to run: {Command}", label);
            return Fail(label, sw.ElapsedMilliseconds, ex.Message);
        }
    }

    /// Build-specific runner — runs the process then applies pagination + summary on top.
    private async Task<DotnetCommandResult> RunBuildAsync(
        string cliArgs,
        string workingDir,
        string label,
        string resolvedTarget,
        int page,
        int pageSize,
        bool includeWarnings,
        CancellationToken ct)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo("dotnet", cliArgs)
        {
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(120));

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start dotnet process.");

            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived  += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync(cts.Token);
            sw.Stop();

            var combined = stdout.ToString() + stderr.ToString();
            logger.LogInformation("[dotnet] {Command} exited in {Ms}ms", label, sw.ElapsedMilliseconds);

            var (success, buildSummary, rawDiagnostics) = ParseBuildOutput(combined, label);
            var diagnostics = includeWarnings
                ? rawDiagnostics
                : rawDiagnostics.Where(d => d.Severity == "error").ToList();

            var total      = diagnostics.Count;
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));

            return new DotnetCommandResult
            {
                Success          = success,
                Command          = label,
                DurationMs       = sw.ElapsedMilliseconds,
                Summary          = buildSummary,
                ResolvedTarget   = resolvedTarget,
                Diagnostics      = diagnostics.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                TotalDiagnostics = total,
                Page             = page,
                PageSize         = pageSize,
                TotalPages       = totalPages
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return Fail(label, sw.ElapsedMilliseconds, "Command timed out after 120 seconds.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[dotnet] Failed to run: {Command}", label);
            return Fail(label, sw.ElapsedMilliseconds, ex.Message);
        }
    }


    // ── output parsers ────────────────────────────────────────────────────────

    private (bool, string, List<DotnetDiagnostic>) ParseBuildOutput(string output, string label)
    {
        var diagnostics = new List<DotnetDiagnostic>();

        foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            var m = DiagnosticLineRegex().Match(line);
            if (!m.Success) continue;

            diagnostics.Add(new DotnetDiagnostic
            {
                Severity = m.Groups["sev"].Value.ToLowerInvariant(),
                Code     = m.Groups["code"].Value,
                File     = NormalisePath(m.Groups["file"].Value.Trim()),
                Line     = int.TryParse(m.Groups["line"].Value, out var l) ? l : null,
                Column   = int.TryParse(m.Groups["col"].Value,  out var c) ? c : null,
                Message  = m.Groups["msg"].Value.Trim()
            });
        }

        var deduped  = diagnostics
            .GroupBy(d => (d.File, d.Line, d.Code))
            .Select(g => g.First())
            .ToList();

        var errors   = deduped.Count(d => d.Severity == "error");
        var warnings = deduped.Count(d => d.Severity == "warning");
        var success  = errors == 0;
        var summary  = success
            ? $"Build succeeded — 0 errors, {warnings} warning(s)"
            : $"Build FAILED — {errors} error(s), {warnings} warning(s)";

        return (success, summary, deduped);
    }

    private static (bool, string, List<DotnetDiagnostic>) ParsePackageOutput(string output, string label)
    {
        // dotnet add package writes a single info line on success; errors go to stderr
        var lower   = output.ToLowerInvariant();
        var success = !lower.Contains("error") && !lower.Contains("unrecognized");
        var summary = success
            ? $"Package added successfully"
            : $"Package install failed";

        // Surface any error lines as diagnostics for visibility
        var diagnostics = new List<DotnetDiagnostic>();
        if (!success)
        {
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                if (t.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    t.Contains("warn",  StringComparison.OrdinalIgnoreCase))
                {
                    diagnostics.Add(new DotnetDiagnostic
                    {
                        Severity = t.Contains("error", StringComparison.OrdinalIgnoreCase) ? "error" : "warning",
                        Message  = t
                    });
                }
            }
        }

        return (success, summary, diagnostics);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private string ResolveRoot(string projectName)
    {
        var entry = config.LoadProjects().Projects
            .FirstOrDefault(p => p.Name.Equals(projectName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException(
                $"Project '{projectName}' not found. Use get_project_skeleton('*') to list available projects.");
        return Path.GetFullPath(entry.Path);
    }

    /// Priority: (1) explicit buildTarget, (2) solution file at root, (3) single csproj anywhere.
    /// Returns null when ambiguous (multiple csproj, no solution).
    private static string? ResolveBuildTarget(string root, string? buildTarget)
    {
        // Explicit override — validate it exists inside root and is .sln/.slnx/.csproj
        if (buildTarget is not null)
        {
            var abs = Path.GetFullPath(Path.Combine(root, buildTarget));
            if (!abs.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("buildTarget must be a relative path inside the project root.");
            var ext = Path.GetExtension(abs).ToLowerInvariant();
            if (ext is not (".sln" or ".slnx" or ".csproj"))
                throw new ArgumentException("buildTarget must be a .sln, .slnx, or .csproj file.");
            if (!File.Exists(abs))
                throw new FileNotFoundException($"buildTarget not found: {buildTarget}");
            return abs;
        }

        // Prefer solution file at root — builds entire solution, all references resolved
        var sln = Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(root, "*.slnx", SearchOption.TopDirectoryOnly))
            .FirstOrDefault();
        if (sln is not null) return sln;

        // No solution — find all csproj files (excluding bin/obj)
        var csprojs = DiscoverBuildTargets(root);
        return csprojs.Count == 1 ? Path.GetFullPath(Path.Combine(root, csprojs[0])) : null;
    }

    /// Returns relative paths of all csproj files, excluding build output dirs.
    private static List<string> DiscoverBuildTargets(string root) =>
        Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !IsInBuildOutputDir(f))
            .Select(f => Path.GetRelativePath(root, f))
            .OrderBy(f => f)
            .ToList();

    private static bool IsInBuildOutputDir(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals("bin", StringComparison.OrdinalIgnoreCase)
                           || p.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    /// Used by add-package — needs a single csproj to attach the package reference to.
    private static string ResolveSingleCsproj(string root)
    {
        var all = DiscoverBuildTargets(root);
        return all.Count switch
        {
            0 => throw new FileNotFoundException($"No .csproj found under: {root}"),
            1 => Path.GetFullPath(Path.Combine(root, all[0])),
            _ => throw new InvalidOperationException(
                $"Multiple .csproj files found. Specify which project to add the package to via args: ['--target', '<relative-path>'].\nAvailable: {string.Join(", ", all)}")
        };
    }


    private static void ValidatePackageId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 200)
            throw new ArgumentException("Invalid package ID.");

        // NuGet IDs: alphanumeric, dots, hyphens, underscores only
        if (!Regex.IsMatch(id, @"^[A-Za-z0-9._\-]+$"))
            throw new ArgumentException($"Package ID '{id}' contains invalid characters.");
    }

    private static void ValidateVersion(string version)
    {
        // Semver-ish: digits, dots, hyphens, alphanumeric pre-release
        if (!Regex.IsMatch(version, @"^[0-9]+\.[0-9]+(\.[0-9]+)?(\.[0-9]+)?(-[A-Za-z0-9\.]+)?$"))
            throw new ArgumentException($"Version '{version}' is not a valid NuGet version.");
    }

    private static string NormalisePath(string p) =>
        p.Replace('\\', '/').TrimStart('/');

    private static DotnetCommandResult Fail(string label, long ms, string reason) =>
        new()
        {
            Success    = false,
            Command    = label,
            DurationMs = ms,
            Summary    = reason,
            Diagnostics = []
        };
}
