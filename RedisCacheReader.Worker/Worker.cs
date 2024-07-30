using System.Diagnostics;
using System.Text.Json;
using Common;
using Common.Redis;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace RedisCacheReader.Worker;

public class Worker : BackgroundService
{
    private readonly IRedisCacheService _redisCacheService;
    private readonly ILogger<Worker> _logger;
    public Worker(IRedisCacheService redisCacheService, ILogger<Worker> logger)
    {
        _redisCacheService = redisCacheService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Worker 1 running at: {time}", DateTimeOffset.Now);
            }

            var value = _redisCacheService.Get("redis-key");

            if (value == null)
            {
                _logger.LogInformation("Cache not found");
                continue;
            }

            var message = JsonSerializer.Deserialize<TracedMessage>(value);

            if (message == null)
            {
                _logger.LogError("Message could not be deserialized");
                continue;
            }

            // Extract context from message and start activity using it
            var parentContext = Propagators.DefaultTextMapPropagator.Extract(default, message, (message, key) => [message.PropagationContext]);
            Baggage.Current = parentContext.Baggage;
                
            // Start the activity earlier and set the context when we have access to it? ¯\_(ツ)_/¯
            using var activity = DistributedTracingInstrumentation.Source.StartActivity("redis-get", ActivityKind.Consumer, parentContext.ActivityContext);

            await Task.Delay(1000, stoppingToken);
        }
    }
}