using Microsoft.Extensions.Logging;
using NuGetExplorer.Extensions;
using System.Collections.Generic;
using System.Text;

namespace NuGetExplorer.Services;

public class NuGetPackageExplorer : INuGetPackageExplorer
{
    private readonly INuGetPackageLoader _loader;
    private readonly ILogger<NuGetPackageExplorer> _logger;

    public NuGetPackageExplorer(
        INuGetPackageLoader loader,
        ILogger<NuGetPackageExplorer> logger)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // NugetServices/NugetPackageExplorer.cs - Add implementation

    public async Task<string> GetMethodOverloads(
        string packageId,
        string @namespace,
        string typeName,
        string methodName,
        string? version = null,
        string? targetFramework = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));

        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace cannot be null or empty", nameof(@namespace));

        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentException("Type name cannot be null or empty", nameof(typeName));

        if (string.IsNullOrWhiteSpace(methodName))
            throw new ArgumentException("Method name cannot be null or empty", nameof(methodName));

        try
        {
            var metadata = await _loader.LoadPackageMetadata(
                packageId, version, targetFramework, includePrerelease, cancellationToken);

            var namespaceMetadata = metadata.MetadataByNamespace.GetValueOrDefault(
                @namespace, new NamespaceMetadata());

            var type = namespaceMetadata.Types
                .FirstOrDefault(t => t.TypeName == typeName);

            if (type == null)
            {
                return $"Type '{typeName}' not found in namespace '{@namespace}'.\n\n" +
                       $"Available types: {string.Join(", ", namespaceMetadata.Types.Select(t => t.TypeName))}";
            }

            var overloads = type.Methods
                .Where(m => m.Name == methodName)
                .ToList();

            if (overloads.Count == 0)
            {
                return $"Method '{methodName}' not found in type '{typeName}'.\n\n" +
                       $"Available methods: {string.Join(", ", type.Methods.Select(m => m.Name).Distinct())}";
            }

            return FormatMethodOverloads(typeName, methodName, overloads);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get method overloads for {PackageId}.{Namespace}.{TypeName}.{MethodName}",
                packageId, @namespace, typeName, methodName);
            throw;
        }
    }

    private static string FormatMethodOverloads(string typeName, string methodName, List<MethodSignature> overloads)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Method Overloads: `{typeName}.{methodName}`");
        sb.AppendLine();
        sb.AppendLine($"**Total Overloads:** {overloads.Count}");
        sb.AppendLine();
        sb.AppendLine("```csharp");

        foreach (var method in overloads.OrderBy(m => m.Parameters.Count))
        {
            var parameters = string.Join(", ",
                method.Parameters.Select(p => $"{p.ParameterType.SimplifyTypeName()} {p.Name}"));

            var staticModifier = method.IsStatic ? "static " : "";
            sb.AppendLine($"{method.Visibility.ToLower()} {staticModifier}{method.ReturnType.SimplifyTypeName()} {methodName}({parameters});");
        }

        sb.AppendLine("```");
        return sb.ToString();
    }

    public async Task<List<string>> GetNamespaces(
        string packageId,
        string? version = null,
        string? targetFramework = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        }

        try
        {
            var metadata = await _loader.LoadPackageMetadata(
                packageId,
                version,
                targetFramework,
                includePrerelease,
                cancellationToken);

            return metadata.Namespaces;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get namespaces for package: {PackageId}@{Version}",
                packageId, version ?? "latest");
            throw;
        }
    }

    public async Task<string> FilterMetadataByNamespace(
        string packageId,
        string @namespace,
        string? version = null,
        string? targetFramework = null,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            throw new ArgumentException("Package ID cannot be null or empty", nameof(packageId));
        }

        if (string.IsNullOrWhiteSpace(@namespace))
        {
            throw new ArgumentException("Namespace cannot be null or empty", nameof(@namespace));
        }

        try
        {
            var metadata = await _loader.LoadPackageMetadata(
                packageId,
                version,
                targetFramework,
                includePrerelease,
                cancellationToken);

            var namespaceMetadata = metadata.MetadataByNamespace.GetValueOrDefault(
                @namespace,
                new NamespaceMetadata());

            return namespaceMetadata.FormatAsMarkdown(@namespace, packageId, version);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to filter metadata for package: {PackageId}@{Version}, namespace: {Namespace}",
                packageId, version ?? "latest", @namespace);
            throw;
        }
    }
}