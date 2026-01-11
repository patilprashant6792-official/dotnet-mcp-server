using MCP.Core.Configuration;
using MCP.Core.Services;
using Microsoft.AspNetCore.RateLimiting;
using ModelContextProtocol.Server;
using NuGetExplorer.Services;
using StackExchange.Redis;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<NuGetServiceConfig>(
    builder.Configuration.GetSection("NuGetService"));

// Add services
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Add in-memory caching with size limit
builder.Services.AddMemoryCache();

// Register NuGetService as singleton (thread-safe with semaphore)
builder.Services.AddSingleton<INuGetSearchService, NuGetSearchService>();
builder.Services.AddSingleton<IProjectSkeletonService, ProjectSkeletonService>();
builder.Services.AddSingleton<ITomlSerializerService, TomlSerializerService>();
builder.Services.AddSingleton<IMarkdownFormatterService, MarkdownFormatterService>();

// ✨ NEW: Add method formatter
builder.Services.AddSingleton<IMethodFormatterService, MethodFormatterService>();

builder.Services.AddSingleton<INuGetPackageLoader, NuGetPackageLoader>();
builder.Services.AddSingleton<INuGetPackageExplorer>(sp =>
{
    var loader = sp.GetRequiredService<INuGetPackageLoader>();
    var logger = sp.GetRequiredService<ILogger<NuGetPackageExplorer>>();
    return new NuGetPackageExplorer(loader, logger);
});

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    var configuration = ConfigurationOptions.Parse("localhost:6379");
    configuration.AbortOnConnectFail = false;
    configuration.ConnectTimeout = 5000;
    configuration.SyncTimeout = 5000;
    return ConnectionMultiplexer.Connect(configuration);
});

// Replace MemoryPackageMetadataCache with Redis
builder.Services.AddSingleton<IPackageMetadataCache, RedisPackageMetadataCache>();
// Rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 100,
            Window = TimeSpan.FromHours(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 10
        });
    });

    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Rate limit exceeded",
            retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
                ? retryAfter.TotalSeconds
                : 3600
        }, token);
    };
});

// MCP Server
builder.Services.AddMcpServer()
    .WithHttpTransport()
      .WithToolsFromAssembly();

// Logging
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["X-XSS-Protection"] = "1; mode=block";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

// Middleware pipeline
app.UseRateLimiter();
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapMcp();

app.Run();