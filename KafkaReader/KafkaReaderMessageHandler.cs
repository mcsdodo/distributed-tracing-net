using KafkaFlow;

namespace KafkaReader;

public class KafkaReaderMessageHandler : IMessageHandler<KafkaMessage>
{
    private readonly ILogger<KafkaReaderMessageHandler> _logger;

    public KafkaReaderMessageHandler(ILogger<KafkaReaderMessageHandler> logger)
    {
        _logger = logger;
    }
    public Task Handle(IMessageContext context, KafkaMessage message)
    {
        _logger.LogInformation($"Consuming {message.Message}");
        return Task.CompletedTask;
    }
}