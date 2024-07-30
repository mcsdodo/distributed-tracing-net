using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Context.Propagation;
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

    public T? Get<T>(string key)
    {
        using var activity = DistributedTracingInstrumentation.Source.CreateActivity("redis-get", ActivityKind.Consumer);
        var redisValue = _database.StringGet(key);
        if (!redisValue.HasValue) return default;

        var message = JsonSerializer.Deserialize<T>(redisValue!);

        var propName = "PropagationContext";
        var parentContext =
            Propagators.DefaultTextMapPropagator.Extract(default, message,
                (message, key) =>
                {
                    var value = (string)message.GetType().GetProperty(propName)!.GetValue(message, null);
                    return [value];
                });
        var activityContext = parentContext.ActivityContext;
        activity!.SetParentId(activityContext.TraceId, activityContext.SpanId);
        return message;
    }
}