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
    
    // Symbols to subscribe to - using both Finnhub and Binance formats
    private readonly string[] _finnhubSymbols = { "BINANCE:BTCUSDT", "BINANCE:ETHUSDT", "BINANCE:ADAUSDT" };
    private readonly string[] _binanceSymbols = { "btcusdt", "ethusdt", "adausdt" };

    public FinnhubIngestService(ILogger<FinnhubIngestService> logger, IServiceProvider sp, IConfiguration configuration)
    {
        _logger = logger;
        _sp = sp;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start both Finnhub and Binance connections
        var tasks = new List<Task>
        {
            ConnectToFinnhub(stoppingToken),
            ConnectToBinance(stoppingToken)
        };

        await Task.WhenAny(tasks);
    }

    private async Task ConnectToFinnhub(CancellationToken stoppingToken)
    {
        try
        {
            var apiKey = _configuration["Finnhub:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogWarning("Finnhub API key not configured, skipping Finnhub connection");
                return;
            }

            var uri = new Uri($"wss://ws.finnhub.io?token={apiKey}");

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(uri, stoppingToken);
            _logger.LogInformation("Connected to Finnhub WebSocket");

            // Subscribe to symbols
            foreach (var symbol in _finnhubSymbols)
            {
                var subscribeMessage = JsonSerializer.Serialize(new { type = "subscribe", symbol = symbol });
                var messageBytes = Encoding.UTF8.GetBytes(subscribeMessage);
                await ws.SendAsync(new ArraySegment<byte>(messageBytes), WebSocketMessageType.Text, true, stoppingToken);
                _logger.LogInformation($"Subscribed to Finnhub {symbol}");
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
                    
                    // Handle ping/pong
                    if (doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "ping")
                    {
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
                                
                                await ProcessPriceUpdate(symbol, price, "Finnhub");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing Finnhub message: {Message}", json);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Finnhub connection failed");
        }
    }

    private async Task ConnectToBinance(CancellationToken stoppingToken)
    {
        try
        {
            // Binance WebSocket doesn't require API key for public data
            var streams = string.Join("/", _binanceSymbols.Select(s => $"{s}@ticker"));
            var uri = new Uri($"wss://stream.binance.com:9443/ws/{streams}");
            
            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(uri, stoppingToken);
            _logger.LogInformation("Connected to Binance WebSocket");

            var buffer = new byte[8192];
            while (!stoppingToken.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                var result = await ws.ReceiveAsync(buffer, stoppingToken);
                if (result.MessageType == WebSocketMessageType.Close) break;
                
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                _logger.LogDebug($"Received from Binance: {json}");
                
                try
                {
                    var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("s", out var symbolElement) && 
                        doc.RootElement.TryGetProperty("c", out var priceElement))
                    {
                        var symbol = $"BINANCE:{symbolElement.GetString()!.ToUpper()}";
                        var price = priceElement.GetDecimal();
                        
                        await ProcessPriceUpdate(symbol, price, "Binance");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error parsing Binance message: {Message}", json);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Binance connection failed");
        }
    }

    private async Task ProcessPriceUpdate(string symbol, decimal price, string source)
    {
        try
        {
            // Validate price is reasonable (not zero or negative)
            if (price <= 0)
            {
                _logger.LogWarning($"Invalid price received from {source}: {symbol} = {price}");
                return;
            }

            var tick = new PriceTick 
            { 
                Symbol = symbol, 
                Price = price, 
                Ts = DateTime.UtcNow 
            };
            
            // Update cache
            Latest.AddOrUpdate(symbol, tick, (_, __) => tick);
            _logger.LogInformation($"Updated {symbol}: ${price:F2} (from {source})");
            
            // Persist to DB
            using var scope = _sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PriceTicks.Add(tick);
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing price update for {Symbol}", symbol);
        }
    }
}
