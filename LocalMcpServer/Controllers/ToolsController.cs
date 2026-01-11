using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using NuGetExplorer.Services;

namespace LocalMcpServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class ToolsController : ControllerBase
{
    private readonly INuGetPackageExplorer _nugetService;
    private readonly ILogger<ToolsController> _logger;

    public ToolsController(
        INuGetPackageExplorer nugetService,
        ILogger<ToolsController> logger)
    {
        _nugetService = nugetService ?? throw new ArgumentNullException(nameof(nugetService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    [HttpPost("get-namespaces")]
    public async Task<IActionResult> GetNamespaces([FromBody] GetNamespacesRequest request)
    {
        try
        {
            _logger.LogInformation(
                "Getting namespaces for package: {PackageId}, Version: {Version}, Framework: {Framework}",
                request.PackageId,
                request.Version ?? "latest",
                request.TargetFramework ?? "net10.0");

            var namespaces = await _nugetService.GetNamespaces(
                request.PackageId,
                request.Version,
                request.TargetFramework,
                request.IncludePrerelease,
                HttpContext.RequestAborted);

            return Ok(new GetNamespacesResponse
            {
                PackageId = request.PackageId,
                Version = request.Version ?? "latest",
                TargetFramework = request.TargetFramework ?? "net10.0",
                Namespaces = namespaces,
                Count = namespaces.Count
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Package not found: {PackageId}", request.PackageId);
            return NotFound(new ErrorResponse
            {
                Error = "PackageNotFound",
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting namespaces for package: {PackageId}", request.PackageId);
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalError",
                Message = "An error occurred while processing the request"
            });
        }
    }

    [HttpPost("filter-metadata-by-namespace")]
    public async Task<IActionResult> FilterMetadataByNamespace([FromBody] FilterMetadataRequest request)
    {
        try
        {
            _logger.LogInformation(
                "Filtering metadata for package: {PackageId}, Namespace: {Namespace}, Version: {Version}, Framework: {Framework}",
                request.PackageId,
                request.Namespace,
                request.Version ?? "latest",
                request.TargetFramework ?? "net10.0");

            var metadata = await _nugetService.FilterMetadataByNamespace(
                request.PackageId,
                request.Namespace,
                request.Version,
                request.TargetFramework,
                request.IncludePrerelease,
                HttpContext.RequestAborted);

            return Ok(metadata);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Package not found: {PackageId}", request.PackageId);
            return NotFound(new ErrorResponse
            {
                Error = "PackageNotFound",
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error filtering metadata for package: {PackageId}, namespace: {Namespace}",
                request.PackageId, request.Namespace);
            return StatusCode(500, new ErrorResponse
            {
                Error = "InternalError",
                Message = "An error occurred while processing the request"
            });
        }
    }

    [HttpGet("health")]
    public IActionResult Health()
    {
        return Ok(new
        {
            Status = "Healthy",
            Service = "NuGet Package Explorer",
            Timestamp = DateTime.UtcNow
        });
    }
}

// Request Models
public class GetNamespacesRequest
{
    [Required]
    [MinLength(1)]
    public string PackageId { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? TargetFramework { get; set; }

    public bool IncludePrerelease { get; set; } = false;
}

public class FilterMetadataRequest
{
    [Required]
    [MinLength(1)]
    public string PackageId { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    public string Namespace { get; set; } = string.Empty;

    public string? Version { get; set; }

    public string? TargetFramework { get; set; }

    public bool IncludePrerelease { get; set; } = false;
}

// Response Models
public class GetNamespacesResponse
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public List<string> Namespaces { get; set; } = new();
    public int Count { get; set; }
}

public class FilterMetadataResponse
{
    public string PackageId { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string TargetFramework { get; set; } = string.Empty;
    public NamespaceMetadata Metadata { get; set; } = new();
    public int TypeCount { get; set; }
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}