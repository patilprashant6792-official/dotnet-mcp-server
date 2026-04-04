namespace MCP.Core.FileModificationService;

public interface IFileModificationService
{
    /// <summary>
    /// Resolves projectName → absolute path + runs all security guards.
    /// Throws KeyNotFoundException for unknown project.
    /// Throws FileAccessDeniedException for blocked paths/patterns.
    /// </summary>
    string ResolveAndGuard(string projectName, string relativeFilePath);

    /// <summary>Batch file create/overwrite/upsert. Each entry is independent.</summary>
    Task<BatchOperationResult> WriteFilesAsync(string projectName, List<WriteFileEntry> files);

    /// <summary>
    /// Apply multiple patches to a single file atomically.
    /// Patches are validated for overlaps then applied bottom-up (descending startLine).
    /// One file read, one file write, one cache invalidation.
    /// </summary>
    Task<BatchOperationResult> EditLinesAsync(string projectName, string relativeFilePath, List<PatchOperation> patches);

    /// <summary>
    /// Batch file move/rename. ALL destinations validated before ANY move executes.
    /// Same-folder destination = rename. Cross-folder = move.
    /// </summary>
    Task<BatchOperationResult> MoveFilesAsync(string projectName, List<MoveFileEntry> moves);

    /// <summary>Batch file delete. Partial success allowed — each file is independent.</summary>
    Task<BatchOperationResult> DeleteFilesAsync(string projectName, List<string> relativeFilePaths);

    /// <summary>Batch folder create. Directory.CreateDirectory handles nesting.</summary>
    Task<BatchOperationResult> CreateFoldersAsync(string projectName, List<string> relativeFolderPaths);

    /// <summary>
    /// Single folder move/rename. Intentionally not batched — high-impact operation.
    /// Cleans up orphaned Redis cache keys for all files under the old path.
    /// </summary>
    Task<FileOperationResult> MoveFolderAsync(string projectName, string from, string to);

    /// <summary>
    /// Batch folder delete (recursive). Sorted deepest-first internally.
    /// Non-existent folders are skipped, not failed.
    /// </summary>
    Task<BatchOperationResult> DeleteFoldersAsync(string projectName, List<string> relativeFolderPaths);

    /// <summary>Batch file metadata query. Never loads file content.</summary>
    Task<BatchOperationResult> GetFileInfoAsync(string projectName, List<string> relativeFilePaths);
}