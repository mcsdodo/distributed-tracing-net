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

var app = builder.Build();
var activitySource = new ActivitySource("Redis.Producer");

app.MapPost("/produce", async (IProducerAccessor producers, IOptions<Connections> options,
        IRedisStreamsService streamsService, 
        ILogger<Program> logger) =>
    {
        var now = DateTime.UtcNow;
        var message = new KafkaMessage()
        {
            Message = $@"It's {now}",
            CreatedOn = now
        };
        await producers["api-producer"].ProduceAsync(Guid.NewGuid().ToString(), message);

        await StreamMessage(message, streamsService, options, logger);
    })
    .WithName("Produce");

app.Run();

async Task StreamMessage(KafkaMessage kafkaMessage, IRedisStreamsService redisStreamsService, IOptions<Connections> options, ILogger<Program> logger)
{
    using var activity = activitySource.StartActivity("redis-produce", ActivityKind.Producer);
    AddActivityToMessage(activity, kafkaMessage);
    var serializedMessage = JsonSerializer.Serialize(kafkaMessage);
    await redisStreamsService.StreamAddAsync(options.Value.Redis.StreamName, serializedMessage, 1);

    logger.LogInformation($"Producing {kafkaMessage.Message}");
}

void AddActivityToMessage(Activity activity, KafkaMessage kafkaMessage)
{
    Propagators.DefaultTextMapPropagator.Inject(
        new PropagationContext(activity.Context, Baggage.Current), 
        kafkaMessage, 
        (message, key, value) => message.PropagationContext = value);
}