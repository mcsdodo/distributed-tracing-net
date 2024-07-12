using KafkaWriter;
using KafkaFlow;
using KafkaFlow.Configuration;
using KafkaFlow.OpenTelemetry;
using KafkaFlow.Serializer;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<Connections>(builder.Configuration.GetSection("Connections"));

builder.Services.AddLogging(configure =>
{
    configure.AddConsole();
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("kafka-writer"))
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
            .AddProducer<Worker>(producer =>
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

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

