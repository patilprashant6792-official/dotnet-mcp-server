using MCP.Core.Services;

namespace MCP.Core.Configuration;

public class NuGetServiceConfig
{
    /// <summary>
    /// Maximum package size in bytes (default: 100MB)
    /// </summary>
    public long MaxPackageSizeBytes { get; set; } = 100 * 1024 * 1024;

    /// <summary>
    /// Maximum concurrent exploration operations (default: 5)
    /// </summary>
    public int MaxConcurrentOperations { get; set; } = 5;

    /// <summary>
    /// Operation timeout (default: 2 minutes)
    /// </summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Maximum assemblies per package to analyze (default: 50)
    /// </summary>
    public int MaxAssembliesPerPackage { get; set; } = 50;

    /// <summary>
    /// Download timeout (default: 30 seconds)
    /// </summary>
    public TimeSpan DownloadTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum package ID length (default: 200)
    /// </summary>
    public int MaxPackageIdLength { get; set; } = 200;

    /// <summary>
    /// Supported target frameworks
    /// </summary>
    public HashSet<string> SupportedFrameworks { get; set; } = new()
    {
        "net8.0", "net7.0", "net6.0", "net5.0","net9.0","net10.0",
        "netstandard2.1", "netstandard2.0", "netstandard1.6"
    };

    /// <summary>
    /// Framework priority order for auto-selection
    /// </summary>
    public List<string> FrameworkPriority { get; set; } = new()
    {
        "net10.0","net9.0","net8.0", "net7.0", "net6.0",
        "netstandard2.1", "netstandard2.0"
    };

    /// <summary>
    /// Enable cleanup of temporary directories (default: true)
    /// </summary>
    public bool EnableTempCleanup { get; set; } = true;

    /// <summary>
    /// Retry configuration for transient failures
    /// </summary>
    public RetryConfig Retry { get; set; } = new();
}

public class FileContentResponse
{
    public string ProjectName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public long FileSizeBytes { get; set; }
    public string Encoding { get; set; } = "UTF-8";
    public string RawContent { get; set; } = string.Empty;
    public FileMetadata Metadata { get; set; } = new();
}


/// <summary>
/// Security configuration for file access control
/// </summary>
public class FileAccessPolicy
{
    /// <summary>
    /// Files that are completely blocked from access
    /// </summary>
    public static readonly HashSet<string> BlockedFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        // Configuration files with sensitive data
        "appsettings.json",
        "appsettings.Development.json",
        "appsettings.Production.json",
        "appsettings.Staging.json",
        "secrets.json",
        "user-secrets.json",
        
        // Connection strings and credentials
        "connectionstrings.config",
        "web.config",
        "app.config",
        
        // Environment files
        ".env",
        ".env.local",
        ".env.development",
        ".env.production",
        
        // SSH and certificates
        "id_rsa",
        "id_rsa.pub",
        "*.pem",
        "*.pfx",
        "*.p12",
        "*.key",
        "*.cert",
        
        // Cloud provider credentials
        ".aws/credentials",
        ".azure/credentials",
        "gcp-key.json",
        
        // Database files
        "*.db",
        "*.sqlite",
        "*.mdf",
        "*.ldf"
    };

    /// <summary>
    /// File patterns that are blocked (using wildcards)
    /// </summary>
    public static readonly string[] BlockedPatterns = new[]
    {
        "*secret*",
        "*password*",
        "*apikey*",
        "*credential*",
        "*token*",
        "*.private.*",
        ".git/config"
    };

    /// <summary>
    /// Directory patterns that are completely off-limits
    /// </summary>
    public static readonly HashSet<string> BlockedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".ssh",
        "node_modules",
        "bin",
        "obj",
        "packages",
        ".vs",
        ".idea",
        "backups",
        "logs"
    };

    /// <summary>
    /// Allowed file extensions (whitelist approach)
    /// </summary>
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Source code
        ".cs", ".vb", ".fs",
        
        // Project files
        ".csproj", ".vbproj", ".fsproj", ".sln", ".slnx",
        
        // Documentation
        ".md", ".txt", ".rst",
        
        // Web files
        ".html", ".htm", ".css", ".js", ".ts", ".jsx", ".tsx",
        
        // Scripts
        ".ps1", ".sh", ".bat", ".cmd",
        
        // Build/deployment (non-sensitive)
        "Dockerfile", ".dockerignore",
        
        // Data formats (safe ones)
        ".xml", ".yml", ".yaml", ".toml"
    };
}

/// <summary>
/// Exception thrown when file access is denied for security reasons
/// </summary>
public class FileAccessDeniedException : UnauthorizedAccessException
{
    public string FilePath { get; }
    public string Reason { get; }

    public FileAccessDeniedException(string filePath, string reason)
        : base($"Access denied to file '{filePath}': {reason}")
    {
        FilePath = filePath;
        Reason = reason;
    }
}

/// <summary>
/// Metadata about the file content
/// </summary>
public class FileMetadata
{
    public bool HasClasses { get; set; }
    public bool HasTopLevelStatements { get; set; }
    public bool ContainsDIRegistration { get; set; }
    public bool ContainsMCPServerConfiguration { get; set; }
    public bool IsConfigurationFile { get; set; }
    public List<string> DetectedPatterns { get; set; } = new();
}

public class MethodImplementationInfo
{
    public string ProjectName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string MethodName { get; set; } = string.Empty;
    public string FullSignature { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public List<ParameterInfo> Parameters { get; set; } = new();
    public List<AttributeInfo> Attributes { get; set; } = new();
    public string? XmlDocumentation { get; set; }
    public string MethodBody { get; set; } = string.Empty;
    public string FullMethodCode { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public bool IsAsync { get; set; }
    public bool IsStatic { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsOverride { get; set; }
    public bool IsAbstract { get; set; }
}

public class RetryConfig
{
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(5);
}