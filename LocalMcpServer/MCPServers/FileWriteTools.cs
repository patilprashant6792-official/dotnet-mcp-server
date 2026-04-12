using MCP.Core.FileModificationService;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Text;
using System.Text.Json;

namespace RisingTideAI.Trade.MCP.Host.MCPServers;

[McpServerToolType]
public class FileWriteTools
{
    private readonly IFileModificationService _svc;

    public FileWriteTools(IFileModificationService svc) => _svc = svc;


    [McpServerTool]
    [Description(
        "Create or overwrite one or more files. Each entry specifies its own mode:\n" +
        "  • create    — fails if file already exists (safe default for new files)\n" +
        "  • overwrite — fails if file does NOT exist (explicit replacement)\n" +
        "  • upsert    — create or overwrite unconditionally\n" +
        "Parent directories are created automatically.\n" +
        "BATCH: pass multiple entries to scaffold an entire feature in one call.\n" +
        "WHEN TO USE: new files or full rewrites. For targeted edits use edit_lines.\n" +
        "RETURNS: per-file success/error, final line count, size in bytes, and last-modified timestamp.")]
    public async Task<string> WriteFile(
        [Description("Project name (e.g. 'RisingTideAPI')")]
        string projectName,
        [Description(
            "JSON array of files to write. Each entry:\n" +
            "  { \"relativeFilePath\": \"Services/OrderService.cs\",\n" +
            "    \"content\": \"<full file content>\",\n" +
            "    \"mode\": \"create|overwrite|upsert\" }\n" +
            "mode defaults to upsert when omitted.")]
        string filesJson)
    {
        var files = Deserialize<List<WriteFileEntry>>(filesJson, "filesJson");
        var result = await _svc.WriteFilesAsync(projectName, files);
        await EnrichWithFileInfo(projectName, result);
        return FormatBatch("write_file", result);
    }

    [McpServerTool]
    [Description(
        "Apply one or more edits to a single file in one atomic operation.\n" +
        "All patches are validated then applied bottom-up — supply ORIGINAL line numbers from your last read.\n\n" +
        "actions:\n" +
        "  • patch  — replace startLine..endLine with content (both inclusive, 1-based)\n" +
        "  • insert — insert content after startLine; use startLine=0 to prepend\n" +
        "  • delete — remove startLine..endLine; content is ignored\n" +
        "  • append — add content at end of file; line params ignored\n\n" +
        "🔴 MANDATORY BATCHING RULE — violations cause wrong-place edits:\n" +
        "   When making multiple changes to the same file, you MUST send ALL patches in a SINGLE\n" +
        "   edit_lines call. The service applies them bottom-up internally, so line numbers stay\n" +
        "   correct across all patches. Splitting into separate calls means the second call uses\n" +
        "   stale cached line numbers and lands at the WRONG LINE every time.\n" +
        "   Correct:  one call with patches=[{lines 10-12}, {lines 45-50}, {lines 80-85}]\n" +
        "   Wrong:    three separate edit_lines calls — NEVER do this for the same file.\n\n" +
        "OVERLAP RULE: ranges must not overlap — the service validates this before any write.\n" +
        "RETURNS: final line count, size in bytes, last-modified timestamp, and lines affected.")]
    public async Task<string> EditLines(
        [Description("Project name")]
        string projectName,
        [Description("Relative file path from project root (e.g. 'Services/OrderService.cs')")]
        string relativeFilePath,
        [Description(
            "JSON array of patch operations. Each entry:\n" +
            "  { \"action\": \"patch|insert|delete|append\",\n" +
            "    \"startLine\": 45,\n" +
            "    \"endLine\": 89,\n" +
            "    \"content\": \"<new content>\" }\n" +
            "startLine/endLine are 1-based. content is ignored for delete.")]
        string patchesJson)
    {
        var patches = Deserialize<List<PatchOperation>>(patchesJson, "patchesJson");
        var result = await _svc.EditLinesAsync(projectName, relativeFilePath, patches);
        await EnrichWithFileInfo(projectName, result);
        return FormatBatch("edit_lines", result);
    }

    [McpServerTool]
    [Description(
        "Move or rename one or more files within the same project.\n" +
        "Same-folder destination = rename. Cross-folder destination = move.\n" +
        "ATOMIC SAFETY: ALL destinations are validated before ANY file is moved.\n" +
        "If one entry fails validation the entire batch is aborted — no partial moves.\n" +
        "NOTE: does not update C# namespaces. Call update_namespace separately if needed.\n" +
        "RETURNS: per-file success/error.")]
    public async Task<string> MoveFile(
        [Description("Project name")]
        string projectName,
        [Description(
            "JSON array of move entries:\n" +
            "  [ { \"from\": \"Services/OldName.cs\", \"to\": \"Core/Services/NewName.cs\" } ]")]
        string movesJson)
    {
        var moves = Deserialize<List<MoveFileEntry>>(movesJson, "movesJson");
        var result = await _svc.MoveFilesAsync(projectName, moves);
        return FormatBatch("move_file", result);
    }

    [McpServerTool]
    [Description(
        "Delete one or more files. Each file is independent — partial success is possible.\n" +
        "UNDO: use git. No backups are created.\n" +
        "BLOCKED: files in bin, obj, .git, backups and files matching secret/password/token patterns.\n" +
        "RETURNS: per-file success/error.")]
    public async Task<string> DeleteFile(
        [Description("Project name")]
        string projectName,
        [Description(
            "JSON array of relative file paths to delete:\n" +
            "  [\"Services/OldService.cs\", \"Services/IOldService.cs\"]")]
        string pathsJson)
    {
        var paths = Deserialize<List<string>>(pathsJson, "pathsJson");
        var result = await _svc.DeleteFilesAsync(projectName, paths);
        return FormatBatch("delete_file", result);
    }

    [McpServerTool]
    [Description(
        "Create one or more folders (including nested paths) in one call.\n" +
        "Idempotent — already-existing folders are not an error.\n" +
        "EXAMPLE: [\"Features/Orders\", \"Features/Orders/Models\", \"Features/Orders/Services\"]\n" +
        "RETURNS: per-folder success/error.")]
    public async Task<string> CreateFolder(
        [Description("Project name")]
        string projectName,
        [Description(
            "JSON array of relative folder paths to create:\n" +
            "  [\"Features/Orders\", \"Features/Orders/Models\"]")]
        string pathsJson)
    {
        var paths = Deserialize<List<string>>(pathsJson, "pathsJson");
        var result = await _svc.CreateFoldersAsync(projectName, paths);
        return FormatBatch("create_folder", result);
    }

    [McpServerTool]
    [Description(
        "Move or rename a folder and its entire contents.\n" +
        "Intentionally single-operation — folder moves are high-impact and should be deliberate.\n" +
        "Same-parent destination = rename. Cross-parent = move.\n" +
        "CACHE: all Redis cache entries under the old path are evicted before the move.\n" +
        "       The file watcher re-indexes the new location automatically.\n" +
        "NOTE: does not update C# namespaces of moved files.\n" +
        "RETURNS: success or error.")]
    public async Task<string> MoveFolder(
        [Description("Project name")]
        string projectName,
        [Description("Current relative folder path (e.g. 'Services')")]
        string from,
        [Description("New relative folder path (e.g. 'Core/Services')")]
        string to)
    {
        var result = await _svc.MoveFolderAsync(projectName, from, to);
        return result.Success
            ? $"✅ move_folder\n   {from} → {to}\n   Cache: invalidated"
            : $"❌ move_folder\n   {from} → {to}\n   Error: {result.Error}";
    }

    [McpServerTool]
    [Description(
        "Delete one or more folders recursively.\n" +
        "Folders are deleted deepest-first — safe to include both parent and child in one call.\n" +
        "Non-existent folders are silently skipped (not an error).\n" +
        "BLOCKED: bin, obj, .git, .vs, .ssh, node_modules, backups, logs, packages.\n" +
        "UNDO: use git. No backups are created.\n" +
        "RETURNS: per-folder success/error.")]
    public async Task<string> DeleteFolder(
        [Description("Project name")]
        string projectName,
        [Description(
            "JSON array of relative folder paths to delete:\n" +
            "  [\"Features/OldFeature\", \"Features/OldFeature/Models\"]")]
        string pathsJson)
    {
        var paths = Deserialize<List<string>>(pathsJson, "pathsJson");
        var result = await _svc.DeleteFoldersAsync(projectName, paths);
        return FormatBatch("delete_folder", result);
    }

    private static string FormatBatch(string toolName, BatchOperationResult batch)
    {
        var sb = new StringBuilder();
        var allOk = batch.FailureCount == 0;

        sb.AppendLine(allOk
            ? $"✅ {toolName} — {batch.SuccessCount} succeeded"
            : $"⚠️  {toolName} — {batch.SuccessCount} succeeded, {batch.FailureCount} failed");

        foreach (var r in batch.Results)
        {
            if (r.Success)
            {
                var meta = new List<string>();
                if (r.LineCount.HasValue)      meta.Add($"{r.LineCount} lines");
                if (r.LinesAffected.HasValue)  meta.Add($"{r.LinesAffected} lines affected");
                if (r.SizeBytes.HasValue)      meta.Add($"{r.SizeBytes} bytes");
                if (r.LastModifiedUtc != null) meta.Add($"modified: {r.LastModifiedUtc}");
                var suffix = meta.Count > 0 ? $" ({string.Join(", ", meta)})" : "";
                sb.AppendLine($"   ✅ {r.RelativeFilePath}{suffix}");
            }
            else
            {
                sb.AppendLine($"   ❌ {r.RelativeFilePath} — {r.Error}");
            }
        }

        if (batch.SuccessCount > 0)
            sb.AppendLine($"   Cache: {batch.CacheStatus}");

        return sb.ToString().TrimEnd();
    }


    /// <summary>
    /// Calls GetFileInfoAsync for every succeeded result and merges disk-fresh
    /// metadata (line count, size, modified) back in-place — eliminating the
    /// trailing-newline line-count inconsistency and saving a round trip.
    /// Enrichment is best-effort: a failure here never fails the write.
    /// </summary>
    private async Task EnrichWithFileInfo(string projectName, BatchOperationResult batch)
    {
        var successPaths = batch.Results
            .Where(r => r.Success)
            .Select(r => r.RelativeFilePath)
            .ToList();

        if (successPaths.Count == 0) return;

        BatchOperationResult infoResult;
        try { infoResult = await _svc.GetFileInfoAsync(projectName, successPaths); }
        catch { return; }

        var infoByPath = infoResult.Results
            .Where(r => r.Success && r.Exists == true)
            .ToDictionary(r => r.RelativeFilePath, StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < batch.Results.Count; i++)
        {
            var r = batch.Results[i];
            if (!r.Success) continue;
            if (!infoByPath.TryGetValue(r.RelativeFilePath, out var info)) continue;

            batch.Results[i] = r with
            {
                LineCount       = info.LineCount,
                SizeBytes       = info.SizeBytes,
                LastModifiedUtc = info.LastModifiedUtc,
                Extension       = info.Extension
            };
        }
    }

    private static T Deserialize<T>(string json, string paramName)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
            }) ?? throw new ArgumentException($"{paramName} deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"Invalid JSON for '{paramName}': {ex.Message}");
        }
    }
}
