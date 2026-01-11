using System.Text.Json;
using StackExchange.Redis;

namespace NuGetExplorer.Services;

public class RedisPackageMetadataCache : IPackageMetadataCache, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _db;
    private readonly TimeSpan _expiration;
    private readonly JsonSerializerOptions _jsonOptions;

    public RedisPackageMetadataCache(
        IConnectionMultiplexer redis,
        TimeSpan? expiration = null)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _db = _redis.GetDatabase();
        _expiration = expiration ?? TimeSpan.FromDays(7);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    public PackageMetadata? Get(string key)
    {
        try
        {
            var value = _db.StringGet(key);

            if (value.IsNullOrEmpty)
            {
                return null;
            }

            return JsonSerializer.Deserialize<PackageMetadata>((string)value!, _jsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Set(string key, PackageMetadata metadata)
    {
        try
        {
            var json = JsonSerializer.Serialize(metadata, _jsonOptions);
            _db.StringSet(key, json, _expiration);
        }
        catch (Exception)
        {
            // Fail silently - cache miss will trigger reload
        }
    }

    public bool TryGet(string key, out PackageMetadata? metadata)
    {
        metadata = Get(key);
        return metadata != null;
    }

    public void Dispose()
    {
        //_redis?.Dispose();
    }
}