using ApiCaller;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<Connections>(builder.Configuration.GetSection("Connections"));

builder.Services.AddLogging(configure =>
{
    configure.AddConsole();
});

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("apicaller.worker"))
    .WithTracing(tracing =>
    {
        tracing
            .SetSampler<AlwaysOnSampler>()
            .AddHttpClientInstrumentation()
            .AddOtlpExporter();
    });

builder.Services.AddHttpClient("api-client", (services, configure) =>
{
    var options = services.GetRequiredService<IOptions<Connections>>();
    configure.BaseAddress = options.Value.Api.BaseUrl;
});

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();