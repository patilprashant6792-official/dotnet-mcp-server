namespace MCP.Core.FileModificationService;

public enum WriteMode
{
    Create,    // Fail if file already exists
    Overwrite, // Fail if file does NOT exist
    Upsert     // Create or overwrite unconditionally
}

public enum PatchAction
{
    Patch,   // Replace startLine..endLine with content (1-based)
    Insert,  // Insert content after startLine; startLine=0 means prepend
    Delete,  // Remove startLine..endLine; content ignored
    Append   // Add content at EOF; line params ignored
}

// ── Request types ─────────────────────────────────────────────────────────────

public record WriteFileEntry(
    string RelativeFilePath,
    string Content,
    WriteMode Mode = WriteMode.Upsert);

public record PatchOperation(
    PatchAction Action,
    int StartLine = 0,
    int EndLine = 0,
    string? Content = null);

public record MoveFileEntry(string From, string To);

// ── Result types ──────────────────────────────────────────────────────────────

public record FileOperationResult
{
    public required string RelativeFilePath { get; init; }
    public required bool Success { get; init; }
    public string? Error { get; init; }

    // Content write ops
    public int? LineCount { get; init; }
    public int? LinesAffected { get; init; }

    // get_file_info only
    public bool? Exists { get; init; }
    public long? SizeBytes { get; init; }
    public string? LastModifiedUtc { get; init; }
    public string? Extension { get; init; }
}

public record BatchOperationResult
{
    public required List<FileOperationResult> Results { get; init; }
    public int SuccessCount => Results.Count(r => r.Success);
    public int FailureCount => Results.Count(r => !r.Success);
    public string CacheStatus { get; init; } = "invalidating — re-index in ~300ms";
}