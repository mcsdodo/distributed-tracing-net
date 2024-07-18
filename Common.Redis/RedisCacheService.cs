using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Common.Redis;

public class RedisCacheService : IRedisCacheService
{
    private readonly ILogger<RedisStreamsService> _logger;
    private readonly IDatabase _database;

    public RedisCacheService(Lazy<IConnectionMultiplexer> connectionMultiplexer,
        ILogger<RedisStreamsService> logger)
    {
        _logger = logger;
        _database = connectionMultiplexer.Value.GetDatabase();;
    }
    public void Set(string key, string value)
    {
        _database.StringSet(key, value);
    }

    public string? Get(string key)
    {
        return _database.StringGet(key);
    }
}