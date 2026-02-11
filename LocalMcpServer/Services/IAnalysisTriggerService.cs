namespace MCP.Core.Services;

/// <summary>
/// Allows any component to request an immediate full index of a specific project,
/// bypassing the background service's scheduled refresh interval.
/// </summary>
public interface IAnalysisTriggerService
{
    /// <summary>Enqueues projectName for immediate re-indexing.</summary>
    void TriggerProjectIndexing(string projectName);

    /// <summary>
    /// Waits for the next available trigger. Returns the project name, or null if cancelled.
    /// Consumed exclusively by CSharpAnalysisBackgroundService.
    /// </summary>
    Task<string?> WaitForTriggerAsync(CancellationToken ct);
}