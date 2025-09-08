using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace ForexNotifications.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PriceController : ControllerBase
    {
        private readonly ILogger<PriceController> _logger;
        private readonly HttpClient _httpClient;

        public PriceController(ILogger<PriceController> logger, HttpClient httpClient)
        {
            _logger = logger;
            _httpClient = httpClient;
        }

        [HttpGet("verify/{symbol}")]
        public async Task<IActionResult> VerifyPrice(string symbol)
        {
            try
            {
                // Get current price from our system
                var ourPrice = FinnhubIngestService.Latest.TryGetValue(symbol, out var tick) ? tick.Price : (decimal?)null;

                // Get price from Binance REST API for verification
                var binanceSymbol = symbol.Replace("BINANCE:", "").ToLower();
                var binanceUrl = $"https://api.binance.com/api/v3/ticker/price?symbol={binanceSymbol}";
                
                var response = await _httpClient.GetStringAsync(binanceUrl);
                var binanceData = JsonSerializer.Deserialize<BinancePriceResponse>(response);
                
                var result = new
                {
                    Symbol = symbol,
                    OurPrice = ourPrice,
                    BinancePrice = binanceData?.Price,
                    Difference = ourPrice.HasValue && binanceData != null 
                        ? Math.Abs(ourPrice.Value - binanceData.Price) 
                        : (decimal?)null,
                    DifferencePercent = ourPrice.HasValue && binanceData != null 
                        ? Math.Abs((ourPrice.Value - binanceData.Price) / binanceData.Price) * 100 
                        : (decimal?)null,
                    Timestamp = DateTime.UtcNow
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying price for {Symbol}", symbol);
                return StatusCode(500, new { error = "Failed to verify price" });
            }
        }

        [HttpGet("latest")]
        public IActionResult GetLatestPrices()
        {
            var prices = FinnhubIngestService.Latest.ToDictionary(
                kvp => kvp.Key, 
                kvp => new { 
                    Price = kvp.Value.Price, 
                    Timestamp = kvp.Value.Ts 
                }
            );
            
            return Ok(prices);
        }
    }

    public class BinancePriceResponse
    {
        public string Symbol { get; set; } = null!;
        public decimal Price { get; set; }
    }
}
