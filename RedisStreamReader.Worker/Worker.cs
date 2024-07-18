using System.Diagnostics;
using System.Text.Json;
using Common.Redis;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace RedisStream.Reader;

public class Worker : BackgroundService
{
    private readonly IRedisStreamsService _streamsService;
    private readonly ILogger<Worker> _logger;
    private readonly Connections _options;
    private readonly ActivitySource _activitySource = new("Redis.Consumer");

    public Worker(
        IRedisStreamsService streamsService,
        IOptions<Connections> options,
        ILogger<Worker> logger)
    {
        _streamsService = streamsService;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            var streamEntries = await _streamsService.StreamReadGroupAsync(_options.Redis.StreamName,
                _options.Redis.ConsumerGroupName,
                "redisstreamreader.worker");

            foreach (var streamEntry in streamEntries)
            {
                _logger.LogInformation($"Consuming message {streamEntry.message}");

                var message = JsonSerializer.Deserialize<TracedMessage>(streamEntry.message);

                if (message == null)
                {
                    _logger.LogError("Message could not be deserialized");
                    continue;
                }

                // Extract context from message and start activity using it
                var parentContext = Propagators.DefaultTextMapPropagator.Extract(default, message, (message, key) => [message.PropagationContext]);
                Baggage.Current = parentContext.Baggage;
                
                // Start the activity earlier and set the context when we have access to it? ¯\_(ツ)_/¯
                using var activity = _activitySource.StartActivity("redis-consume", ActivityKind.Consumer, parentContext.ActivityContext);
                await _streamsService.StreamAcknowledgeAsync(_options.Redis.StreamName,
                    _options.Redis.ConsumerGroupName, streamEntry.streamEntryId);
            }
        }
    }
}