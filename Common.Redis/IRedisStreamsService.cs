namespace Common.Redis;

public interface IRedisStreamsService
{
    Task<string?> StreamAddAsync(string streamName, string message, int? streamLength = null);

    Task<ICollection<(string streamEntryId, string message)>> StreamReadGroupAsync(
        string streamName, string consumerGroupName, string consumerName, int maxNumberOfMessagesToReturn = 1000);

    Task StreamAcknowledgeAsync(string streamName, string consumerGroupName, string streamEntryId);
}