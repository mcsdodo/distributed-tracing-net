using System.Text.Json;
using Common.Redis;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.OpenTelemetry;
using KafkaFlow.Producers;
using KafkaFlow.Serializer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
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

        var serializedMessage = JsonSerializer.Serialize(message);
        await streamsService.StreamAddAsync(options.Value.Redis.StreamName, serializedMessage, 1);
        
        logger.LogInformation($"Producing {message.Message}");
    })
    .WithName("Produce");

app.Run();