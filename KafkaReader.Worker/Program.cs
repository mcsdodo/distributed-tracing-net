using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.OpenTelemetry;
using KafkaFlow.Serializer;
using KafkaReader;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<Connections>(builder.Configuration.GetSection("Connections"));

builder.Services.AddLogging(configure =>
{
    configure.AddConsole();
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("kafkareader.worker"))
    .WithTracing(tracing =>
    {
        tracing
            .SetSampler<AlwaysOnSampler>()
            .AddSource(KafkaFlowInstrumentation.ActivitySourceName)
            .AddHttpClientInstrumentation()
            .AddRedisInstrumentation()
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
            .AddConsumer(consumer =>
            {
                consumer.Topic(kafkaConfig.Kafka.TopicName);
                consumer.WithGroupId("kafka-reader-v1");
                consumer.WithBufferSize(100);
                consumer.WithWorkersCount(1);
                consumer.AddMiddlewares(middlewares =>
                {
                    middlewares.AddDeserializer<JsonCoreDeserializer>();
                    middlewares.AddTypedHandlers(h => h.AddHandler<KafkaReaderMessageHandler>());
                });
            })
        )
        .AddOpenTelemetryInstrumentation();
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
var kafkaBus = host.Services.CreateKafkaBus();
var applicationLifetime = host.Services.GetService<IHostApplicationLifetime>();
await kafkaBus.StartAsync(applicationLifetime?.ApplicationStopping ?? default);

await host.RunAsync();