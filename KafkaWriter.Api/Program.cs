using System.Diagnostics;
using System.Text.Json;
using Common.Redis;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.OpenTelemetry;
using KafkaFlow.Producers;
using KafkaFlow.Serializer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<Connections>(builder.Configuration.GetSection("Connections"));
builder.Services.AddLogging(configure =>
{
    configure.AddConsole();
});

builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("kafkawriter.api"))
    .WithTracing(tracing =>
    {
        tracing
            .SetSampler<AlwaysOnSampler>()
            .AddSource(KafkaFlowInstrumentation.ActivitySourceName)
            .AddSource("Redis.Producer")
            .AddHttpClientInstrumentation()
            .AddAspNetCoreInstrumentation()
            .AddRedisInstrumentation()
            .AddOtlpExporter();
        
        tracing.ConfigureRedisInstrumentation((services, configure) =>
        {
            var nonKeyedLazyMultiplexer =
                services.GetRequiredService<Lazy<IConnectionMultiplexer>>();
            configure.AddConnection("Multiplexer", nonKeyedLazyMultiplexer.Value);
        });
    });

builder.Services.AddKafka(kafka =>
{
    kafka.UseMicrosoftLog();

    var kafkaConfig = builder.Configuration.GetSection("Connections").Get<Connections>();
    kafka.AddCluster(cluster => cluster
            .WithSecurityInformation(security =>
            {
                security.EnableSslCertificateVerification = false;
                security.SecurityProtocol = SecurityProtocol.Plaintext;
            })
            .WithBrokers(kafkaConfig!.Kafka.Brokers.Split(','))
            .AddProducer("api-producer", producer =>
            {
                producer.DefaultTopic(kafkaConfig.Kafka.TopicName);
                producer.AddMiddlewares(middlewares =>
                {
                    middlewares.AddSerializer<JsonCoreSerializer>();
                });
            })
        )
        .AddOpenTelemetryInstrumentation();
});

builder.Services.TryAddSingleton<Lazy<IConnectionMultiplexer>>(provider =>
{
    var connectionString = provider.GetRequiredService<IOptions<Connections>>().Value.Redis.ConnectionString;
    return new Lazy<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(connectionString));
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.TryAddSingleton<IRedisStreamsService, RedisStreamsService>();
builder.Services.TryAddSingleton<IRedisCacheService, RedisCacheService>();

var app = builder.Build();
var activitySource = new ActivitySource("Redis.Producer");

app.MapPost("/produce", async (IProducerAccessor producers, IOptions<Connections> options,
        IRedisStreamsService streamsService,
        IRedisCacheService redisCacheService,
        ILogger<Program> logger) =>
    {
        var now = DateTime.UtcNow;
        await ProduceKafkaMessage(now, producers, logger);
        CacheRedisMessage(now, redisCacheService, logger);
        await StreamRedisMessage(now, streamsService, options, logger);
    })
    .WithName("Produce");

app.Run();


void CacheRedisMessage(DateTime now, IRedisCacheService redisCacheService, ILogger<Program> logger)
{
    var message = new TracedMessage()
    {
        Content = $@"It's {now}",
        CreatedOn = now
    };
    logger.LogInformation($"Creating redis cache entry {message.Content}");
    using var activity = activitySource.StartActivity("redis-set", ActivityKind.Producer);
    AddActivityToMessage(activity, message);
    var serializedMessage = JsonSerializer.Serialize(message);
    redisCacheService.Set("redis-key", serializedMessage);
}

async Task StreamRedisMessage(DateTime now, IRedisStreamsService redisStreamsService, IOptions<Connections> options, ILogger<Program> logger)
{
    var message = new TracedMessage()
    {
        Content = $@"It's {now}",
        CreatedOn = now
    };
    logger.LogInformation($"Producing to redis stream {message.Content}");
    var serializedMessage = JsonSerializer.Serialize(message);
    await redisStreamsService.StreamAddAsync(options.Value.Redis.StreamName, serializedMessage, 1);
}

void AddActivityToMessage(Activity activity, TracedMessage redisMessage)
{
    Propagators.DefaultTextMapPropagator.Inject(
        new PropagationContext(activity.Context, Baggage.Current), 
        redisMessage, 
        (message, key, value) => message.PropagationContext = value);
}

async Task ProduceKafkaMessage(DateTime dateTime, IProducerAccessor producerAccessor, ILogger<Program> logger)
{
    var message = new Message()
    {
        Content = $@"It's {dateTime}",
        CreatedOn = dateTime
    };
    logger.LogInformation($"Producing to kafka stream {message.Content}");
    await producerAccessor["api-producer"].ProduceAsync(Guid.NewGuid().ToString(), message);
}