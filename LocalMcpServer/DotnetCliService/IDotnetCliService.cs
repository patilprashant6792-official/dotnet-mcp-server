namespace MCP.Core.DotnetCliService;

public record DotnetDiagnostic
{
    public required string Severity { get; init; }  // error | warning
    public string? Code    { get; init; }            // CS0246
    public string? File    { get; init; }            // relative path
    public int?    Line    { get; init; }
    public int?    Column  { get; init; }
    public required string Message { get; init; }
}



public record DotnetCommandResult
{
    public required bool          Success          { get; init; }
    public required string        Command          { get; init; }
    public required long          DurationMs       { get; init; }
    public required string        Summary          { get; init; }
    public string?                ResolvedTarget   { get; init; }
    public List<string>?          AvailableTargets { get; init; }

    // Diagnostics — always present, paginated
    public List<DotnetDiagnostic> Diagnostics      { get; init; } = [];
    public int                    TotalDiagnostics { get; init; }
    public int                    Page             { get; init; } = 1;
    public int                    PageSize         { get; init; } = 50;
    public int                    TotalPages       { get; init; }


}

public interface IDotnetCliService
{
    Task<DotnetCommandResult> BuildAsync(
        string projectName,
        string? buildTarget = null,
        int page = 1,
        int pageSize = 50,
        bool includeWarnings = false,
        bool clean = true,
        CancellationToken ct = default);
    Task<DotnetCommandResult> AddPackageAsync(string projectName, string packageId, string? version, CancellationToken ct = default);
}


