namespace MCP.Core.Models;

// TypeSnapshot.cs
public class TypeSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsInterface { get; set; }
    public bool IsEnum { get; set; }
    public bool IsValueType { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsSealed { get; set; }
    public bool IsPublic { get; set; }
    public string? BaseType { get; set; }
    public List<string> Interfaces { get; set; } = new();
    public List<MethodSnapshot> Constructors { get; set; } = new();
    public List<PropertySnapshot> Properties { get; set; } = new();
    public List<MethodSnapshot> Methods { get; set; } = new();
}

// MethodSnapshot.cs
public class MethodSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = "void";
    public List<ParameterSnapshot> Parameters { get; set; } = new();
}

// ParameterSnapshot.cs
public class ParameterSnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}

// PropertySnapshot.cs
public class PropertySnapshot
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
}
/// <summary>
/// Result of the atomic install-analyze-cleanup operation
/// </summary>
public class PackageAnalysisResult
{
    public string PackageId { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? InstallPath { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// List of package dependencies (format: "PackageId VersionRange")
    /// </summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>
    /// Metadata for all analyzed assemblies in the package
    /// </summary>
    public List<AssemblyMetadata> Assemblies { get; set; } = new();
}
public class NuGetInstallResult
{
    public bool Success { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public List<string> AssemblyPaths { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public List<string> Dependencies { get; set; } = new();
}

public class NuGetPackageInfo
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Authors { get; set; }
    public string? ProjectUrl { get; set; }
    public string? LicenseUrl { get; set; }
    public long? TotalDownloads { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<PackageDependency> Dependencies { get; set; } = new();
    public List<string> TargetFrameworks { get; set; } = new();
    public DateTime? Published { get; set; }
}

public class PackageDependency
{
    public string PackageId { get; set; } = string.Empty;
    public string? VersionRange { get; set; }
    public string? TargetFramework { get; set; }
}

public class InstalledPackageInfo
{
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
}

public class PackageSearchResult
{
    public string PackageId { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string? Description { get; set; }
    public long? TotalDownloads { get; set; }
    public List<string> Tags { get; set; } = new();
}

public class AssemblyMetadata
{
    /// <summary>
    /// Assembly name
    /// </summary>
    public string AssemblyName { get; set; } = string.Empty;

    /// <summary>
    /// Assembly version
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Target framework
    /// </summary>
    public string TargetFramework { get; set; } = string.Empty;

    /// <summary>
    /// Complete API documentation in markdown format.
    /// This is the PRIMARY output - a developer-friendly reference guide
    /// containing all public types, methods, properties, and their signatures.
    /// </summary>
    public string MarkdownDocumentation { get; set; } = string.Empty;

    /// <summary>
    /// Total count of public types
    /// </summary>
    public int TotalTypes { get; set; }

    /// <summary>
    /// Referenced assemblies
    /// </summary>
    public List<string> ReferencedAssemblies { get; set; } = new();
}

public class TypeMetadata
{
    public string Namespace { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public bool IsClass { get; set; }
    public bool IsInterface { get; set; }
    public bool IsEnum { get; set; }
    public bool IsAbstract { get; set; }
    public bool IsSealed { get; set; }
    public string? BaseType { get; set; }
    public List<string> Interfaces { get; set; } = new();
    public List<MethodMetadata> Methods { get; set; } = new();
    public List<PropertyMetadata> Properties { get; set; } = new();
    public List<ConstructorMetadata> Constructors { get; set; } = new();
    public string? XmlDocumentation { get; set; }
}

public class MethodMetadata
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAsync { get; set; }
    public bool IsVirtual { get; set; }
    public bool IsAbstract { get; set; }
    public List<ParameterMetadata> Parameters { get; set; } = new();
    public string? XmlDocumentation { get; set; }
    public List<string> Attributes { get; set; } = new();
}

public class PropertyMetadata
{
    public string Name { get; set; } = string.Empty;
    public string PropertyType { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public bool CanRead { get; set; }
    public bool CanWrite { get; set; }
    public bool IsStatic { get; set; }
    public string? XmlDocumentation { get; set; }
}

public class ConstructorMetadata
{
    public bool IsPublic { get; set; }
    public List<ParameterMetadata> Parameters { get; set; } = new();
    public string? XmlDocumentation { get; set; }
}

public class ParameterMetadata
{
    public string Name { get; set; } = string.Empty;
    public string ParameterType { get; set; } = string.Empty;
    public bool HasDefaultValue { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsOptional { get; set; }
    public bool IsParams { get; set; }
}