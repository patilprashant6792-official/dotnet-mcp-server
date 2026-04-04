using MCP.Core.Exceptions;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace MCP.Core.Middlewares;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = Activity.Current?.Id ?? context.TraceIdentifier;

        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            await HandleExceptionAsync(context, ex, requestId, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task HandleExceptionAsync(
        HttpContext context,
        Exception exception,
        string requestId,
        long elapsedMilliseconds)
    {
        var (statusCode, errorResponse) = MapExceptionToResponse(exception, requestId);

        // Log with appropriate severity
        LogException(exception, context, requestId, elapsedMilliseconds, statusCode);

        context.Response.Clear();
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        // Add correlation headers
        context.Response.Headers["X-Request-ID"] = requestId;
        context.Response.Headers["X-Error-Type"] = exception.GetType().Name;

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = _environment.IsDevelopment()
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, options));
    }

    private (HttpStatusCode statusCode, ErrorResponse response) MapExceptionToResponse(
        Exception exception,
        string requestId)
    {
        return exception switch
        {
            // NuGet-specific exceptions
            PackageTooLargeException ex => (
                HttpStatusCode.RequestEntityTooLarge,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Package Too Large",
                    Message = ex.Message,
                    Details = new Dictionary<string, object>
                    {
                        ["packageId"] = ex.PackageId ?? "unknown",
                        ["actualSizeMB"] = ex.ActualSize / (1024 * 1024),
                        ["maxSizeMB"] = ex.MaxSize / (1024 * 1024)
                    }
                }),

            ServiceCapacityException ex => (
                HttpStatusCode.ServiceUnavailable,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Service Unavailable",
                    Message = ex.Message,
                    Details = new Dictionary<string, object>
                    {
                        ["retryAfterSeconds"] = 60
                    }
                }),

            InvalidPackageException ex => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Invalid Package",
                    Message = ex.Message,
                    Details = new Dictionary<string, object>
                    {
                        ["packageId"] = ex.PackageId ?? "unknown"
                    }
                }),

            PackageDownloadException ex => (
                HttpStatusCode.BadGateway,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Package Download Failed",
                    Message = ex.Message,
                    Details = new Dictionary<string, object>
                    {
                        ["packageId"] = ex.PackageId ?? "unknown",
                        ["version"] = ex.Version ?? "unknown"
                    }
                }),

            NuGetServiceException ex => (
                HttpStatusCode.InternalServerError,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "NuGet Service Error",
                    Message = ex.Message,
                    Details = new Dictionary<string, object>
                    {
                        ["packageId"] = ex.PackageId ?? "unknown",
                        ["version"] = ex.Version ?? "unknown"
                    }
                }),

            // Standard exceptions
            ArgumentNullException ex => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Invalid Request",
                    Message = $"Required parameter '{ex.ParamName}' is missing",
                    Details = _environment.IsDevelopment()
                        ? new Dictionary<string, object> { ["stackTrace"] = ex.StackTrace ?? "" }
                        : null
                }),

            ArgumentException ex => (
                HttpStatusCode.BadRequest,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Invalid Argument",
                    Message = ex.Message,
                    Details = _environment.IsDevelopment()
                        ? new Dictionary<string, object>
                        {
                            ["paramName"] = ex.ParamName ?? "unknown",
                            ["stackTrace"] = ex.StackTrace ?? ""
                        }
                        : null
                }),

            InvalidOperationException ex => (
                HttpStatusCode.Conflict,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Invalid Operation",
                    Message = ex.Message,
                    Details = _environment.IsDevelopment()
                        ? new Dictionary<string, object> { ["stackTrace"] = ex.StackTrace ?? "" }
                        : null
                }),

            TimeoutException ex => (
                HttpStatusCode.RequestTimeout,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Request Timeout",
                    Message = "The operation timed out. Please try again.",
                    Details = _environment.IsDevelopment()
                        ? new Dictionary<string, object> { ["stackTrace"] = ex.StackTrace ?? "" }
                        : null
                }),

            UnauthorizedAccessException ex => (
                HttpStatusCode.Forbidden,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Access Denied",
                    Message = "You do not have permission to perform this operation",
                    Details = _environment.IsDevelopment()
                        ? new Dictionary<string, object> { ["stackTrace"] = ex.StackTrace ?? "" }
                        : null
                }),
            // Add these two cases BEFORE the final catch-all `_ =>` arm
            // in the MapExceptionToResponse switch expression:

            FileNotFoundException ex => (
                HttpStatusCode.NotFound,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "File Not Found",
                    Message = ex.Message,
                    Details = _environment.IsDevelopment()
                        ? new Dictionary<string, object> { ["stackTrace"] = ex.StackTrace ?? "" }
                        : null
                }),

            IOException ex => (
                HttpStatusCode.InternalServerError,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "IO Error",
                    Message = ex.Message,
                    Details = _environment.IsDevelopment()
                        ? new Dictionary<string, object>
                        {
                            ["ioErrorType"] = ex.GetType().Name,
                            ["stackTrace"] = ex.StackTrace ?? ""
                        }
                        : null
                }),

            // Catch-all for unexpected exceptions
            _ => (
                HttpStatusCode.InternalServerError,
                new ErrorResponse
                {
                    RequestId = requestId,
                    Error = "Internal Server Error",
                    Message = _environment.IsDevelopment()
                        ? exception.Message
                        : "An unexpected error occurred. Please contact support if the problem persists.",
                    Details = _environment.IsDevelopment()
                        ? new Dictionary<string, object>
                        {
                            ["exceptionType"] = exception.GetType().FullName ?? "Unknown",
                            ["stackTrace"] = exception.StackTrace ?? "",
                            ["innerException"] = exception.InnerException?.Message ?? ""
                        }
                        : null
                })
        };
    }

    private void LogException(
        Exception exception,
        HttpContext context,
        string requestId,
        long elapsedMilliseconds,
        HttpStatusCode statusCode)
    {
        var logLevel = statusCode switch
        {
            HttpStatusCode.BadRequest => LogLevel.Warning,
            HttpStatusCode.Unauthorized => LogLevel.Warning,
            HttpStatusCode.Forbidden => LogLevel.Warning,
            HttpStatusCode.NotFound => LogLevel.Information,
            HttpStatusCode.RequestTimeout => LogLevel.Warning,
            HttpStatusCode.Conflict => LogLevel.Warning,
            HttpStatusCode.RequestEntityTooLarge => LogLevel.Warning,
            HttpStatusCode.ServiceUnavailable => LogLevel.Error,
            HttpStatusCode.BadGateway => LogLevel.Error,
            _ => LogLevel.Critical
        };

        _logger.Log(
            logLevel,
            exception,
            "Unhandled exception during request processing. " +
            "Method: {Method}, Path: {Path}, StatusCode: {StatusCode}, " +
            "RequestId: {RequestId}, Duration: {DurationMs}ms, " +
            "ClientIP: {ClientIP}, UserAgent: {UserAgent}",
            context.Request.Method,
            context.Request.Path,
            (int)statusCode,
            requestId,
            elapsedMilliseconds,
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            context.Request.Headers["User-Agent"].ToString() ?? "unknown"
        );
    }
}

public class ErrorResponse
{
    public string RequestId { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Dictionary<string, object>? Details { get; set; }
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}