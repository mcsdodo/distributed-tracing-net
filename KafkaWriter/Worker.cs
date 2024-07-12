using KafkaFlow;
using Microsoft.Extensions.Options;

namespace KafkaWriter;

public class Worker : BackgroundService
{
    private readonly IMessageProducer<Worker> _producer;
    private readonly ILogger<Worker> _logger;
    private readonly Connections _options;

    public Worker(
        IMessageProducer<Worker> producer,
        ILogger<Worker> logger, 
        IOptions<Connections> options)
    {
        _producer = producer;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var message = new KafkaMessage()
            {
                Message = $@"It's {now}",
                CreatedOn = now
            };
            await _producer.ProduceAsync(Guid.NewGuid().ToString(), message);
            
            _logger.LogInformation($"Producing {message.Message}");

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}