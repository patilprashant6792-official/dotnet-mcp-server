namespace MCP.Core.Models;

public class TomlCollectionWrapper<T> where T : class
{
    public List<T> Items { get; set; } = new();
}
public class ProjectDependencyGraph
{
    public string ProjectName { get; set; } = string.Empty;
    public DateTime AnalyzedAt { get; set; } = DateTime.UtcNow;
    public List<ClassDependencyNode> Nodes { get; set; } = new();
    public List<DependencyEdge> Edges { get; set; } = new();
    public List<CircularDependency> CircularDependencies { get; set; } = new();
    public GraphStatistics Statistics { get; set; } = new();
    public Dictionary<string, List<string>> ArchitectureIssues { get; set; } = new();
}

public class ClassDependencyNode
{
    public string FullyQualifiedName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Namespace { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public ClassType Type { get; set; }
    public List<string> Interfaces { get; set; } = new();
    public string? BaseClass { get; set; }
    public List<DependencyInfo> DirectDependencies { get; set; } = new();
    public List<string> Dependents { get; set; } = new();
    public int DependencyDepth { get; set; }
    public int TransitiveDependencyCount { get; set; }
    public bool IsPartOfCircularDependency { get; set; }
    public DependencyResolutionStrategy ResolutionStrategy { get; set; }
}

public class DependencyInfo
{
    public string DependencyType { get; set; } = string.Empty;
    public string? ParameterName { get; set; }
    public bool IsInterface { get; set; }
    public DependencySource Source { get; set; }
    public string? SourceLocation { get; set; }
}

public class DependencyEdge
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public EdgeType Type { get; set; }
    public string? Context { get; set; }
}

public class CircularDependency
{
    public List<string> CyclePath { get; set; } = new();
    public int CycleLength { get; set; }
    public Severity Severity { get; set; }
}

public class GraphStatistics
{
    public int TotalClasses { get; set; }
    public int TotalDependencies { get; set; }
    public int CircularDependenciesCount { get; set; }
    public int MaxDependencyDepth { get; set; }
    public double AverageDependenciesPerClass { get; set; }
    public Dictionary<DependencySource, int> DependencySourceBreakdown { get; set; } = new();
    public List<string> MostDependedUpon { get; set; } = new();
    public List<string> MostDependent { get; set; } = new();
    public List<string> GodClasses { get; set; } = new();
}

public enum ClassType
{
    Unknown, Controller, Service, Repository, Middleware,
    Configuration, Model, Interface, StaticUtility, McpTool
}

public enum DependencySource
{
    ConstructorInjection, PropertyInjection, MethodParameter,
    DirectInstantiation, StaticMethodCall, ServiceLocator, FieldReference
}

public enum DependencyResolutionStrategy
{
    Unknown, ConstructorInjection, PropertyInjection, ServiceLocator,
    DirectInstantiation, StaticUsage, Mixed
}

public enum EdgeType
{
    Unknown, ConstructorInjection, PropertyInjection, MethodParameter,
    DirectInstantiation, StaticMethodCall, ServiceLocator
}

public enum Severity
{
    Low, Medium, High, Critical
}