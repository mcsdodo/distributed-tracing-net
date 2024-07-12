using Common.Redis;
using Microsoft.Extensions.Options;

namespace RedisStream.Reader;

public class Worker : BackgroundService
{
    private readonly IRedisStreamsService _streamsService;
    private readonly ILogger<Worker> _logger;
    private readonly Connections _options;

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
                //TODO: do something
                await _streamsService.StreamAcknowledgeAsync(_options.Redis.StreamName,
                    _options.Redis.ConsumerGroupName, streamEntry.streamEntryId);
            }
        }
    }
}