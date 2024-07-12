using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.OpenTelemetry;
using KafkaFlow.Producers;
using KafkaFlow.Serializer;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;

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
            .AddOtlpExporter();
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

var app = builder.Build();

app.MapPost("/produce", async (IProducerAccessor producers, ILogger<Program> logger) =>
    {
        var now = DateTime.UtcNow;
        var message = new KafkaMessage()
        {
            Message = $@"It's {now}",
            CreatedOn = now
        };
        await producers["api-producer"].ProduceAsync(Guid.NewGuid().ToString(), message);
        logger.LogInformation($"Producing {message.Message}");
    })
    .WithName("Produce");

app.Run();