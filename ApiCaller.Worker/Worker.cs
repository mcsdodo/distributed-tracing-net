using Microsoft.Extensions.Options;

namespace ApiCaller;

public class Worker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<Worker> _logger;
    private readonly Connections _options;

    public Worker(IHttpClientFactory httpClientFactory,
        ILogger<Worker> logger, IOptions<Connections> options)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            var client = _httpClientFactory.CreateClient("api-client");
            var msg = new HttpRequestMessage(HttpMethod.Post, $"/produce");
            var response = await client.SendAsync(msg, stoppingToken);

            response.EnsureSuccessStatusCode();
        }
    }
}