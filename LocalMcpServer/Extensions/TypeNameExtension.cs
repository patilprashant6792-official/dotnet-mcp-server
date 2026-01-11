using System.Text;
using System.Text.RegularExpressions;

namespace NuGetExplorer.Extensions;

public static class TypeNameExtensions
{
    private static readonly Dictionary<string, string> TypeAliases = new()
    {
        { "System.String", "string" },
        { "System.Int32", "int" },
        { "System.Int64", "long" },
        { "System.Boolean", "bool" },
        { "System.Double", "double" },
        { "System.Decimal", "decimal" },
        { "System.Single", "float" },
        { "System.Byte", "byte" },
        { "System.SByte", "sbyte" },
        { "System.Int16", "short" },
        { "System.UInt16", "ushort" },
        { "System.UInt32", "uint" },
        { "System.UInt64", "ulong" },
        { "System.Char", "char" },
        { "System.Object", "object" },
        { "System.Void", "void" }
    };

    public static string SimplifyTypeName(this string fullTypeName)
    {
        if (string.IsNullOrEmpty(fullTypeName))
        {
            return "void";
        }

        if (fullTypeName.Contains("System.Nullable`1"))
        {
            var match = Regex.Match(fullTypeName, @"System\.Nullable`1\[\[([^,]+),");
            if (match.Success)
            {
                var innerType = SimplifyTypeName(match.Groups[1].Value);
                return $"{innerType}?";
            }
        }

        var genericMatch = Regex.Match(fullTypeName, @"^([^`\[]+)`(\d+)\[\[(.+)\]\]$");
        if (genericMatch.Success)
        {
            var baseType = genericMatch.Groups[1].Value;
            var genericArgs = genericMatch.Groups[3].Value;
            var args = SplitGenericArguments(genericArgs);
            var simplifiedArgs = args.Select(SimplifyTypeName).ToList();
            var shortBaseType = baseType.Split('.').Last();

            return shortBaseType switch
            {
                "List" when simplifiedArgs.Count == 1 => $"List<{simplifiedArgs[0]}>",
                "Dictionary" when simplifiedArgs.Count == 2 => $"Dictionary<{simplifiedArgs[0]}, {simplifiedArgs[1]}>",
                "IEnumerable" when simplifiedArgs.Count == 1 => $"IEnumerable<{simplifiedArgs[0]}>",
                "Task" when simplifiedArgs.Count == 1 => $"Task<{simplifiedArgs[0]}>",
                _ => $"{shortBaseType}<{string.Join(", ", simplifiedArgs)}>"
            };
        }

        var typeWithoutAssembly = fullTypeName.Split(',')[0].Trim();

        if (TypeAliases.TryGetValue(typeWithoutAssembly, out var alias))
        {
            return alias;
        }

        return typeWithoutAssembly.Split('.').Last();
    }

    private static List<string> SplitGenericArguments(string genericArgs)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var bracketDepth = 0;

        foreach (var ch in genericArgs)
        {
            switch (ch)
            {
                case '[':
                    bracketDepth++;
                    if (bracketDepth > 1)
                        current.Append(ch);
                    break;
                case ']':
                    bracketDepth--;
                    if (bracketDepth > 0)
                        current.Append(ch);
                    break;
                case ',' when bracketDepth == 1:
                    var arg = ExtractArgument(current.ToString());
                    result.Add(arg);
                    current.Clear();
                    break;
                default:
                    current.Append(ch);
                    break;
            }
        }

        if (current.Length > 0)
        {
            result.Add(ExtractArgument(current.ToString()));
        }

        return result;
    }

    private static string ExtractArgument(string arg)
    {
        arg = arg.Trim();
        if (arg.StartsWith("[") && arg.EndsWith("]"))
        {
            arg = arg.Substring(1, arg.Length - 2);
        }
        return arg;
    }
}