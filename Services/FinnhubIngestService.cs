using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

public class FinnhubIngestService : BackgroundService
{
    private readonly ILogger<FinnhubIngestService> _logger;
    private readonly IServiceProvider _sp;
    private readonly IConfiguration _configuration;
    // simple in-memory cache; BroadcastService will read this
    public static ConcurrentDictionary<string, PriceTick> Latest = new();
    
    // Symbols to subscribe to (you can make this configurable)
    private readonly string[] _symbols = { "BINANCE:BTCUSDT", "BINANCE:ETHUSDT", "BINANCE:ADAUSDT" };

    public FinnhubIngestService(ILogger<FinnhubIngestService> logger, IServiceProvider sp, IConfiguration configuration)
    {
        _logger = logger;
        _sp = sp;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Get API key from configuration
        var apiKey = _configuration["Finnhub:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            _logger.LogError("Finnhub API key not configured");
            return;
        }

        var uri = new Uri($"wss://ws.finnhub.io?token={apiKey}");

        using var ws = new ClientWebSocket();
        await ws.ConnectAsync(uri, stoppingToken);
        _logger.LogInformation("Connected to Finnhub WebSocket");

        // Subscribe to symbols
        foreach (var symbol in _symbols)
        {
            var subscribeMessage = JsonSerializer.Serialize(new { type = "subscribe", symbol = symbol });
            var messageBytes = Encoding.UTF8.GetBytes(subscribeMessage);
            await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, stoppingToken);
            _logger.LogInformation($"Subscribed to {symbol}");
        }

        var buffer = new byte[8192];
        while (!stoppingToken.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var result = await ws.ReceiveAsync(buffer, stoppingToken);
            if (result.MessageType == WebSocketMessageType.Close) break;
            
            var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
            _logger.LogDebug($"Received from Finnhub: {json}");
            
            try
            {
                var doc = JsonDocument.Parse(json);
                
                // Handle subscription confirmation
                if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "ping")
                {
                    // Send pong response
                    var pongMessage = JsonSerializer.Serialize(new { type = "pong" });
                    var pongBytes = Encoding.UTF8.GetBytes(pongMessage);
                    await ws.SendAsync(new ArraySegment<byte>(pongBytes), WebSocketMessageType.Text, true, stoppingToken);
                    continue;
                }
                
                // Handle price data
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    foreach (var item in data.EnumerateArray())
                    {
                        if (item.TryGetProperty("s", out var symbolElement) && 
                            item.TryGetProperty("p", out var priceElement))
                        {
                            var symbol = symbolElement.GetString()!;
                            var price = priceElement.GetDecimal();
                            
                            var tick = new PriceTick 
                            { 
                                Symbol = symbol, 
                                Price = price, 
                                Ts = DateTime.UtcNow 
                            };
                            
                            Latest.AddOrUpdate(symbol, tick, (_, __) => tick);
                            _logger.LogInformation($"Updated {symbol}: {price}");
                            
                            // Persist to DB
                            using var scope = _sp.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                            db.PriceTicks.Add(tick);
                            await db.SaveChangesAsync();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Finnhub message: {Message}", json);
            }
        }
        
        _logger.LogInformation("Finnhub WebSocket connection closed");
    }
}
