using MCP.Core.Models;
using Tomlyn;
using Tomlyn.Syntax;

namespace MCP.Core.Services;

/// <summary>
/// Production-ready TOML serialization service with comprehensive error handling
/// </summary>
public class TomlSerializerService : ITomlSerializerService
{
    private readonly ILogger<TomlSerializerService> _logger;
    private readonly TomlModelOptions _options;

    public TomlSerializerService(ILogger<TomlSerializerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _options = new TomlModelOptions
        {
            ConvertPropertyName = name => name, // Preserve original casing
            ConvertFieldName = name => name,
            IgnoreMissingProperties = true,
            IncludeFields = false // Only serialize properties, not fields
        };
    }

    /// <inheritdoc />
    public string Serialize<T>(T obj) where T : class
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        try
        {
            // CRITICAL: Wrap collections before serializing
            // TOML requires root to be a table, not an array
            var tomlCompatible = WrapIfCollection(obj);

            var tomlText = Toml.FromModel(tomlCompatible, _options);

            _logger.LogDebug(
                "Successfully serialized {Type} to TOML ({Length} chars)",
                typeof(T).Name,
                tomlText.Length);

            return tomlText;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to serialize {Type} to TOML",
                typeof(T).Name);

            throw new InvalidOperationException(
                $"TOML serialization failed for type {typeof(T).Name}: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Wraps collections in a container object since TOML requires root to be a table.
    /// </summary>
    private object WrapIfCollection<T>(T obj)
    {
        if (obj == null)
        {
            throw new ArgumentNullException(nameof(obj));
        }

        var type = obj.GetType();

        // Check if it's a generic List<>
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
        {
            var itemType = type.GetGenericArguments()[0];

            // Create wrapper: new TomlCollectionWrapper<ItemType> { Items = obj }
            var wrapperType = typeof(TomlCollectionWrapper<>).MakeGenericType(itemType);
            var wrapper = Activator.CreateInstance(wrapperType);
            var itemsProperty = wrapperType.GetProperty("Items");
            itemsProperty?.SetValue(wrapper, obj);

            return wrapper!;
        }

        // Check if it's IEnumerable but not string
        if (type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
        {
            // Fallback: wrap in dynamic object
            return new { items = obj };
        }

        return obj;
    }

    /// <inheritdoc />
    public T Deserialize<T>(string tomlText) where T : class, new()
    {
        if (string.IsNullOrWhiteSpace(tomlText))
        {
            throw new ArgumentException("TOML text cannot be null or whitespace", nameof(tomlText));
        }

        try
        {
            var result = Toml.ToModel<T>(tomlText, options: _options);

            _logger.LogDebug(
                "Successfully deserialized TOML to {Type} ({Length} chars)",
                typeof(T).Name,
                tomlText.Length);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to deserialize TOML to {Type}",
                typeof(T).Name);

            throw new InvalidOperationException(
                $"TOML deserialization failed for type {typeof(T).Name}: {ex.Message}",
                ex);
        }
    }

    /// <inheritdoc />
    public bool TrySerialize<T>(T obj, out string tomlText, out string? errorMessage) where T : class
    {
        tomlText = string.Empty;
        errorMessage = null;

        if (obj == null)
        {
            errorMessage = "Object cannot be null";
            return false;
        }

        try
        {
            // CRITICAL: Wrap collections before serializing
            var tomlCompatible = WrapIfCollection(obj);

            if (Toml.TryFromModel(tomlCompatible, out tomlText, out var diagnostics, _options))
            {
                return true;
            }

            errorMessage = diagnostics != null
                ? string.Join("; ", diagnostics.Select(d => d.ToString()))
                : "Unknown serialization error";

            _logger.LogWarning(
                "TOML serialization failed for {Type}: {Error}",
                typeof(T).Name,
                errorMessage);

            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            _logger.LogError(
                ex,
                "Exception during TOML serialization for {Type}",
                typeof(T).Name);

            return false;
        }
    }
}