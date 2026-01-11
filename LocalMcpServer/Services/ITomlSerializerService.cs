namespace MCP.Core.Services;

/// <summary>
/// Service for TOML serialization operations
/// </summary>
public interface ITomlSerializerService
{
    /// <summary>
    /// Serializes an object to TOML string format
    /// </summary>
    string Serialize<T>(T obj) where T : class;

    /// <summary>
    /// Deserializes a TOML string to the specified type
    /// </summary>
    T Deserialize<T>(string tomlText) where T : class, new();

    /// <summary>
    /// Attempts to serialize an object to TOML
    /// </summary>
    bool TrySerialize<T>(T obj, out string tomlText, out string? errorMessage) where T : class;
}