using KafkaFlow;

namespace KafkaReader;

public class KafkaReaderMessageHandler : IMessageHandler<Message>
{
    private readonly ILogger<KafkaReaderMessageHandler> _logger;

    public KafkaReaderMessageHandler(ILogger<KafkaReaderMessageHandler> logger)
    {
        _logger = logger;
    }
    public Task Handle(IMessageContext context, Message message)
    {
        _logger.LogInformation($"Consuming {message.Content}");
        return Task.CompletedTask;
    }
}