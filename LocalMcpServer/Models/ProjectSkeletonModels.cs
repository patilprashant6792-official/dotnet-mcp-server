namespace MCP.Core.Models;

public class ProjectMappingsConfiguration
{
    public const string SectionName = "ProjectMappings";

    public Dictionary<string, ProjectInfo> Projects { get; set; } = new();
}

public class ProjectInfo
{
    public string Path { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public class CSharpFileAnalysis
{
    public string ProjectName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long SizeInBytes { get; set; }
    public string Namespace { get; set; } = string.Empty;
    public List<string> UsingDirectives { get; set; } = new();
    public List<ClassInfo> Classes { get; set; } = new();
}

public class ClassInfo
{
    public string Name { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public string? BaseClass { get; set; }
    public List<string> Interfaces { get; set; } = new();
    public List<ParameterInfo> ConstructorParameters { get; set; } = new();
    public List<MethodInfo> Methods { get; set; } = new();
    public List<PropertyInfo> Properties { get; set; } = new();
    public List<FieldInfo> Fields { get; set; } = new();
    public List<AttributeInfo> Attributes { get; set; } = new();
    public int LineNumber { get; set; }
}

public class MethodInfo
{
    public string Name { get; set; } = string.Empty;
    public string ReturnType { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public List<ParameterInfo> Parameters { get; set; } = new();
    public List<AttributeInfo> Attributes { get; set; } = new();
    public string? XmlDocumentation { get; set; }

    /// <summary>
    /// Line number where the method declaration starts (1-based)
    /// Includes attributes if present
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Starting line number of the method (including attributes and documentation)
    /// </summary>
    public int LineNumberStart { get; set; }

    /// <summary>
    /// Ending line number of the method (last line of the closing brace)
    /// </summary>
    public int LineNumberEnd { get; set; }
}

public class FieldInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public bool IsReadOnly { get; set; }
    public bool IsStatic { get; set; }
    public bool IsConst { get; set; }
    public List<AttributeInfo> Attributes { get; set; } = new();
    public int LineNumber { get; set; }

    /// <summary>
    /// Starting line number of the field declaration
    /// </summary>
    public int LineNumberStart { get; set; }

    /// <summary>
    /// Ending line number of the field declaration
    /// </summary>
    public int LineNumberEnd { get; set; }
}

public class PropertyInfo
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public List<string> Modifiers { get; set; } = new();
    public bool HasGetter { get; set; }
    public bool HasSetter { get; set; }
    public List<AttributeInfo> Attributes { get; set; } = new();

    /// <summary>
    /// Line number where the property declaration starts (1-based)
    /// </summary>
    public int LineNumber { get; set; }

    /// <summary>
    /// Starting line number of the property (including attributes)
    /// </summary>
    public int LineNumberStart { get; set; }

    /// <summary>
    /// Ending line number of the property
    /// </summary>
    public int LineNumberEnd { get; set; }
}

public class ParameterInfo
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DefaultValue { get; set; }
}

public class AttributeInfo
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Properties { get; set; } = new();
}

public class FolderSearchResponse
{
    public string ProjectName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string? SearchPattern { get; set; }
    public int TotalFiles { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<FileEntry> Files { get; set; } = new();
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}

public class FileEntry
{
    public string FileName { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeDisplay { get; set; } = string.Empty;
    public int LineCount { get; set; }
    public bool IsLargeFile { get; set; }
}
