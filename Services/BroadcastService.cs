using Microsoft.AspNetCore.SignalR;

public class BroadcastService : BackgroundService
{
    private readonly IHubContext<ForexHub> _hub;
    private readonly ILogger<BroadcastService> _logger;

    public BroadcastService(IHubContext<ForexHub> hub, ILogger<BroadcastService> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var kvp in FinnhubIngestService.Latest)
                {
                    var symbol = kvp.Key;
                    var tick = kvp.Value;
                    await _hub.Clients.Group(symbol).SendAsync("PriceUpdate", tick, cancellationToken: stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error broadcasting ticks");
            }

            await Task.Delay(500, stoppingToken); // broadcast every 500ms
        }
    }
}
