// Extensions/MetadataFormattingExtensions.cs
using NuGetExplorer.Services;
using System.Text;

namespace NuGetExplorer.Extensions;

public static class MetadataFormattingExtensions
{
    public static string FormatAsMarkdown(
        this NamespaceMetadata metadata,
        string @namespace,
        string packageId,
        string? version)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Namespace: {@namespace}");
        sb.AppendLine($"**Package:** {packageId}{(version != null ? $"@{version}" : "")}");
        sb.AppendLine();

        if (metadata.Types.Count == 0)
        {
            sb.AppendLine("*No types found in this namespace.*");
            return sb.ToString();
        }

        var groupedTypes = metadata.Types
            .OrderBy(t => GetTypeKind(t))
            .ThenBy(t => t.TypeName)
            .GroupBy(t => GetTypeKind(t));

        foreach (var group in groupedTypes)
        {
            sb.AppendLine($"## {group.Key}s");
            sb.AppendLine();

            foreach (var type in group)
            {
                type.FormatType(sb);
            }
        }

        return sb.ToString();
    }

    private static string GetTypeKind(TypeMetadata type)
    {
        return type.Kind.ToString() switch
        {
            "Interface" => "Interface",
            "Enum" => "Enum",
            _ => "Class"
        };
    }

    public static void FormatType(this TypeMetadata type, StringBuilder sb)
    {
        // Type declaration
        var keyword = type.Kind.ToString().ToLower();
        sb.AppendLine($"```csharp");
        sb.AppendLine($"{keyword} {type.TypeName}");
        sb.AppendLine($"{{");

        // Constructors (collapsed)
        var ctorGroups = type.Constructors
            .GroupBy(c => c.Parameters.Count)
            .OrderBy(g => g.Key);

        foreach (var group in ctorGroups)
        {
            var representative = group.First();
            sb.AppendLine($"    {representative.FormatConstructor(type.TypeName)}");

            if (group.Count() > 1)
            {
                sb.AppendLine($"    // + {group.Count() - 1} overload{(group.Count() > 2 ? "s" : "")}");
            }
        }

        if (type.Constructors.Count > 0 && (type.Properties.Count > 0 || type.Methods.Count > 0))
            sb.AppendLine();

        // Properties (no change - properties rarely have overloads)
        foreach (var prop in type.Properties.OrderBy(p => p.Name))
        {
            sb.AppendLine($"    {prop.FormatProperty()}");
        }

        if (type.Properties.Count > 0 && type.Methods.Count > 0)
            sb.AppendLine();

        // Methods (collapsed by name)
        var methodGroups = type.Methods
            .GroupBy(m => m.Name)
            .OrderBy(g => g.Key);

        foreach (var group in methodGroups)
        {
            // Pick the most representative overload:
            // 1. Prefer methods with 0 parameters (simplest)
            // 2. Then by lowest parameter count
            // 3. Then by non-generic
            var representative = group
                .OrderBy(m => m.Parameters.Count)
                .ThenBy(m => m.ReturnType.Contains("<") ? 1 : 0) // Non-generic first
                .First();

            sb.AppendLine($"    {representative.FormatMethod()}");

            if (group.Count() > 1)
            {
                sb.AppendLine($"    // + {group.Count() - 1} overload{(group.Count() > 2 ? "s" : "")}");
            }
        }

        sb.AppendLine($"}}");
        sb.AppendLine($"```");
        sb.AppendLine();
    }

    public static string FormatConstructor(this ConstructorSignature ctor, string typeName)
    {
        var parameters = string.Join(", ",
            ctor.Parameters.Select(p => $"{p.ParameterType.SimplifyTypeName()} {p.Name}"));
        return $"{ctor.Visibility.ToLower()} {typeName}({parameters});";
    }

    public static string FormatProperty(this PropertySignature prop)
    {
        var accessors = new List<string>();
        if (prop.CanRead) accessors.Add("get");
        if (prop.CanWrite) accessors.Add("set");

        var staticModifier = prop.IsStatic ? "static " : "";
        var accessorList = string.Join("; ", accessors);

        return $"{prop.Visibility.ToLower()} {staticModifier}{prop.PropertyType.SimplifyTypeName()} {prop.Name} {{ {accessorList}; }}";
    }

    public static string FormatMethod(this MethodSignature method)
    {
        var parameters = string.Join(", ",
            method.Parameters.Select(p => $"{p.ParameterType.SimplifyTypeName()} {p.Name}"));

        var staticModifier = method.IsStatic ? "static " : "";
        return $"{method.Visibility.ToLower()} {staticModifier}{method.ReturnType.SimplifyTypeName()} {method.Name}({parameters});";
    }
}