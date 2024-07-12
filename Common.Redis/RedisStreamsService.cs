using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Common.Redis;

public class RedisStreamsService : IRedisStreamsService
{
    private const string ValueRedisEntryName = "value";
    private const string CreatedAtRedisEntryName = "createdAtDateTimeOffset";
    private const int DefaultMaxLength = 10_000;

    private readonly TimeProvider _dateTimeProvider;
    private readonly ILogger<RedisStreamsService> _logger;
    private readonly IDatabase _database;

    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _initializedStreamsConcurrentDictionary = new();

    public RedisStreamsService(
        Lazy<IConnectionMultiplexer> connectionMultiplexer,
        TimeProvider dateTimeProvider,
        ILogger<RedisStreamsService> logger)
    {
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _database = connectionMultiplexer.Value.GetDatabase();
    }

    public async Task<string?> StreamAddAsync(string streamName, string message, int? streamLength = DefaultMaxLength)
    {
        try
        {
            var utcNow = _dateTimeProvider.GetUtcNow().ToString();
            
            return await _database.StreamAddAsync(
                streamName,
                streamPairs:
                [
                    new NameValueEntry(ValueRedisEntryName, message),
                    new NameValueEntry(CreatedAtRedisEntryName, utcNow)
                ],
                maxLength: streamLength,
                useApproximateMaxLength: true);
        }
        catch (RedisException redisServerException)
        {
            _logger.LogError(
                redisServerException,
                """
                {Component}: {UtcNow} - Error occured during {MethodName}
                StreamName: {StreamName}
                Message: {Message}
                """,
                nameof(RedisStreamsService),
                DateTimeOffset.UtcNow,
                nameof(StreamAddAsync),
                streamName,
                message);
            
            return null;
        }
    }

    public async Task<ICollection<(string streamEntryId, string message)>> StreamReadGroupAsync(
        string streamName,
        string consumerGroupName,
        string consumerName,
        int maxNumberOfMessagesToReturn = 1000)
    {
        try
        {
            await EnsureStreamAndConsumerGroupExists(_database, streamName, consumerGroupName);

            var streamEntries = await _database.StreamReadGroupAsync(
                streamName, consumerGroupName, consumerName: consumerName, position: ">",
                count: maxNumberOfMessagesToReturn);

            if (streamEntries is null || streamEntries.Length == 0)
                return [];

            var messages = new List<(string, string)>(streamEntries.Length);

            foreach (var streamEntry in streamEntries)
            {
                if (!TryGetMessage(streamEntry, out var message) || message is null)
                    continue;

                messages.Add(new(streamEntry.Id!, message));
            }

            return messages;
        }
        catch (RedisServerException redisServerException) when
            (redisServerException.Message.Contains("NOGROUP No such key"))
        {
            _logger.LogError(
                redisServerException,
                """
                {Component}: {UtcNow} - Error occured during {MethodName}
                StreamName: {StreamName}
                ConsumerGroupName: {ConsumerGroupName}
                ConsumerName: {ConsumerName}
                """,
                nameof(RedisStreamsService),
                DateTimeOffset.UtcNow,
                nameof(StreamReadGroupAsync),
                streamName,
                consumerGroupName,
                consumerName);

            var regex = new Regex("No such key '([^']*)'");
            var match = regex.Match(redisServerException.Message);

            if (match.Success)
            {
                var streamNameToEvict = match.Groups[1].Value;
                _initializedStreamsConcurrentDictionary.TryRemove(streamNameToEvict, out _);
                await EnsureStreamAndConsumerGroupExists(_database, streamName, consumerGroupName);
            }
        }
        catch (RedisServerException redisServerException)
        {
            _logger.LogError(
                redisServerException,
                """
                {Component}: {UtcNow} - Error occured during {MethodName}
                StreamName: {StreamName}
                ConsumerGroupName: {ConsumerGroupName}
                ConsumerName: {ConsumerName}
                """,
                nameof(RedisStreamsService),
                DateTimeOffset.UtcNow,
                nameof(StreamReadGroupAsync),
                streamName,
                consumerGroupName,
                consumerName);
        }

        return [];
    }

    private async ValueTask EnsureStreamAndConsumerGroupExists(
        IDatabaseAsync db,
        string streamName,
        string consumerGroupName)
    {
        if (_initializedStreamsConcurrentDictionary.ContainsKey(streamName))
            return;

        await _initializationLock.WaitAsync();

        try
        {
            if (!_initializedStreamsConcurrentDictionary.ContainsKey(streamName))
            {
                if (!await db.KeyExistsAsync(streamName) ||
                    (await db.StreamGroupInfoAsync(streamName)).All(x => x.Name != consumerGroupName))
                {
                    await db.StreamCreateConsumerGroupAsync(streamName, consumerGroupName, position: "0-0",
                        createStream: true);
                }

                _initializedStreamsConcurrentDictionary.TryAdd(streamName, 0);
            }
        }
        catch (RedisServerException redisServerException)
        {
            // stream with consumer group is already created
            _logger.LogError(redisServerException, redisServerException.Message);
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private bool TryGetMessage(StreamEntry streamEntry, out string? message)
    {
        message = null;

        try
        {
            var streamEntryDictionary =
                streamEntry.Values.ToDictionary(x => x.Name.ToString(), x => x.Value.ToString());

            message = streamEntryDictionary["value"];

            return !string.IsNullOrEmpty(message);
        }
        catch (ArgumentException argumentException)
        {
            _logger.LogError(argumentException, argumentException.Message);
        }
        catch (KeyNotFoundException keyNotFoundException)
        {
            _logger.LogError(keyNotFoundException, keyNotFoundException.Message);
        }

        return false;
    }

    public async Task StreamAcknowledgeAsync(string streamName, string consumerGroupName, string streamEntryId)
    {
        try
        {
            await EnsureStreamAndConsumerGroupExists(_database, streamName, consumerGroupName);

            await _database.StreamAcknowledgeAsync(streamName, consumerGroupName, streamEntryId);
        }
        catch (RedisServerException redisServerException)
        {
            _logger.LogError(
                redisServerException,
                """
                {Component}: {UtcNow} - Error occured during {MethodName}
                StreamName: {StreamName}
                ConsumerGroupName: {ConsumerGroupName}
                StreamEntryId: {StreamEntryId}
                """,
                nameof(RedisStreamsService),
                DateTimeOffset.UtcNow,
                nameof(StreamAcknowledgeAsync),
                streamName,
                consumerGroupName,
                streamEntryId);
        }
    }
}