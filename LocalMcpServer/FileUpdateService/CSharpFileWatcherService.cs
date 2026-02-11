using MCP.Core.Configuration;
using MCP.Core.Services;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace MCP.Core.FileUpdateService;

/// <summary>
/// Watches all configured project paths for .cs file changes.
/// On any Create/Change/Delete/Rename event:
///   - Debounces 300ms (VS fires multiple events per save)
///   - Re-analyses and updates Redis, or removes stale keys
/// Also implements IFileWatcherRegistry so the project config controller
/// can register/deregister paths at runtime without restarting the service.
/// </summary>
public class CSharpFileWatcherService : BackgroundService, IFileWatcherRegistry
{
    private readonly IServiceProvider _sp;
    private readonly AnalysisCacheConfig _config;
    private readonly ILogger<CSharpFileWatcherService> _logger;

    // projectName → FileSystemWatcher
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();

    // filePath → pending debounce cancellation
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounce = new();

    // projectPath → projectName (reverse lookup for watcher events)
    private readonly ConcurrentDictionary<string, string> _pathToName = new();

    public CSharpFileWatcherService(
        IServiceProvider sp,
        IOptions<AnalysisCacheConfig> config,
        ILogger<CSharpFileWatcherService> logger)
    {
        _sp = sp;
        _config = config.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Yield();

        // Load initial project list
        using (var scope = _sp.CreateScope())
        {
            var configService = scope.ServiceProvider.GetRequiredService<IProjectConfigService>();
            var projects = configService.LoadProjects().Projects.Where(p => p.Enabled);
            foreach (var p in projects)
                RegisterProject(p.Name, p.Path);
        }

        _logger.LogInformation("CSharpFileWatcherService watching {Count} project(s)", _watchers.Count);

        // Keep alive until cancellation
        try { await Task.Delay(Timeout.Infinite, ct); }
        catch (OperationCanceledException) { }

        // Cleanup
        foreach (var w in _watchers.Values)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }

        _logger.LogInformation("CSharpFileWatcherService stopped.");
    }

    // ── IFileWatcherRegistry ──────────────────────────────────────────────────

    public void RegisterProject(string projectName, string projectPath)
    {
        if (!Directory.Exists(projectPath))
        {
            _logger.LogWarning("Cannot watch non-existent path for '{Project}': {Path}", projectName, projectPath);
            return;
        }

        // Remove any stale watcher for the same project
        UnregisterProject(projectName);

        var watcher = new FileSystemWatcher(projectPath, "*.cs")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName,
            EnableRaisingEvents = true
        };

        watcher.Changed += (_, e) => ScheduleReanalysis(projectName, projectPath, e.FullPath);
        watcher.Created += (_, e) => ScheduleReanalysis(projectName, projectPath, e.FullPath);
        watcher.Deleted += (_, e) => ScheduleDelete(projectName, projectPath, e.FullPath);
        watcher.Renamed += (_, e) =>
        {
            ScheduleDelete(projectName, projectPath, e.OldFullPath);
            ScheduleReanalysis(projectName, projectPath, e.FullPath);
        };
        watcher.Error += (_, e) =>
            _logger.LogError(e.GetException(), "FileSystemWatcher error for project '{Project}'", projectName);

        _watchers[projectName] = watcher;
        _pathToName[projectPath] = projectName;

        _logger.LogInformation("Watching '{Project}' at {Path}", projectName, projectPath);
    }

    public void UnregisterProject(string projectName)
    {
        if (_watchers.TryRemove(projectName, out var old))
        {
            old.EnableRaisingEvents = false;
            old.Dispose();
            _logger.LogInformation("Stopped watching '{Project}'", projectName);
        }
    }

    // ── Debounce ──────────────────────────────────────────────────────────────

    private void ScheduleReanalysis(string projectName, string projectPath, string fullPath)
    {
        if (!fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;
        if (IsExcluded(fullPath)) return;

        DebounceAction(fullPath, async ct =>
        {
            var rel = Path.GetRelativePath(projectPath, fullPath);
            _logger.LogDebug("File changed: {Project}:{RelPath}", projectName, rel);
            await ReanalyseFileAsync(projectName, rel, ct);
        });
    }

    private void ScheduleDelete(string projectName, string projectPath, string fullPath)
    {
        if (!fullPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;

        DebounceAction(fullPath, async ct =>
        {
            var rel = Path.GetRelativePath(projectPath, fullPath);
            _logger.LogDebug("File deleted: {Project}:{RelPath}", projectName, rel);
            await DeleteFileAsync(projectName, rel);
        });
    }

    private void DebounceAction(string key, Func<CancellationToken, Task> action)
    {
        // Cancel any pending debounce for this file
        if (_debounce.TryRemove(key, out var old))
        {
            old.Cancel();
            old.Dispose();
        }

        var cts = new CancellationTokenSource();
        _debounce[key] = cts;

        var delayMs = _config.FileWatcherDebounceMs;
        var ct = cts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delayMs, ct);
                await action(ct);
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer event — normal, ignore
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Debounced file action failed for {Key}", key);
            }
            finally
            {
                _debounce.TryRemove(key, out _);
            }
        }, CancellationToken.None);
    }

    // ── Cache operations ──────────────────────────────────────────────────────

    // ── Replace only these two methods inside CSharpFileWatcherService ─────

    // ── Replace only these two methods inside CSharpFileWatcherService ─────

    private async Task ReanalyseFileAsync(string projectName, string relativePath, CancellationToken ct)
    {
        using var scope = _sp.CreateScope();
        // Concrete type — bypasses decorator so watcher always writes fresh data, never reads its own output
        var skeleton = scope.ServiceProvider.GetRequiredService<ProjectSkeletonService>();
        var cache = scope.ServiceProvider.GetRequiredService<IAnalysisCacheService>();

        try
        {
            var analysis = await skeleton.AnalyzeCSharpFileAsync(
                projectName, relativePath, includePrivateMembers: true, ct);

            var methodNames = analysis.Classes
                .SelectMany(c => c.Methods.Select(m => m.Name))
                .Distinct()
                .ToArray();

            var setAnalysisTask = cache.SetAsync(projectName, relativePath, analysis);
            var setMethodsTask = methodNames.Length > 0
                ? IndexMethodsAsync(projectName, relativePath, methodNames, skeleton, cache, ct)
                : Task.CompletedTask;

            await Task.WhenAll(
                setAnalysisTask,
                setMethodsTask,
                cache.AddToIndexAsync(projectName, relativePath));

            _logger.LogInformation("Re-indexed {Project}:{Path}", projectName, relativePath);
        }
        catch (FileNotFoundException)
        {
            // File deleted between event and now — treat as delete
            await DeleteFileAsync(projectName, relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Re-analysis failed for {Project}:{Path}", projectName, relativePath);
        }
    }

    private async Task DeleteFileAsync(string projectName, string relativePath)
    {
        using var scope = _sp.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<IAnalysisCacheService>();

        // DeleteAsync now removes both analysis + methods keys in one call
        await Task.WhenAll(
            cache.DeleteAsync(projectName, relativePath),
            cache.RemoveFromIndexAsync(projectName, relativePath));

        _logger.LogInformation("Removed from cache {Project}:{Path}", projectName, relativePath);
    }

    /// <summary>
    /// Shared helper — same key strategy as the background indexer.
    /// </summary>
    private static async Task IndexMethodsAsync(
        string projectName,
        string relativePath,
        string[] methodNames,
        ProjectSkeletonService skeleton,
        IAnalysisCacheService cache,
        CancellationToken ct)
    {
        var impls = await skeleton.FetchMethodImplementationsBatchAsync(
            projectName, relativePath, methodNames, className: null, ct);

        var counts = impls.GroupBy(m => m.MethodName).ToDictionary(g => g.Key, g => g.Count());
        var dict = impls.ToDictionary(
            m => counts[m.MethodName] > 1 ? $"{m.ClassName}::{m.MethodName}" : m.MethodName,
            m => m);

        await cache.SetMethodsAsync(projectName, relativePath, dict);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly HashSet<string> ExcludedDirs = new(StringComparer.OrdinalIgnoreCase)
        { "bin", "obj", ".vs", ".git", "node_modules", "packages" };

    private static bool IsExcluded(string filePath)
    {
        var parts = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => ExcludedDirs.Contains(p));
    }
}