using System.Threading.Channels;

namespace MCP.Core.Services;

/// <summary>
/// Channel-backed trigger service. Unbounded so callers never block.
/// Duplicates are intentionally allowed — the background service deduplicates
/// via the project name before indexing.
/// </summary>
public sealed class AnalysisTriggerService : IAnalysisTriggerService
{
    // Unbounded: callers (HTTP request threads) must never block on write.
    // Backpressure is not a concern — project additions are infrequent.
    private readonly Channel<string> _channel =
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,   // only CSharpAnalysisBackgroundService reads
            SingleWriter = false   // controller + any future callers write
        });

    public void TriggerProjectIndexing(string projectName)
    {
        // TryWrite on an unbounded channel never fails
        _channel.Writer.TryWrite(projectName);
    }

    public async Task<string?> WaitForTriggerAsync(CancellationToken ct)
    {
        try
        {
            return await _channel.Reader.ReadAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (ChannelClosedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Non-blocking drain — used by the background service to batch up
    /// duplicates that arrived while the previous trigger was being processed.
    /// </summary>
    public bool TryRead([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? name)
        => _channel.Reader.TryRead(out name);
}