public class PriceTick
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Symbol { get; set; } = null!;
    public decimal Price { get; set; }
    public decimal? Bid { get; set; }
    public decimal? Ask { get; set; }
    public DateTime Ts { get; set; } = DateTime.UtcNow;
}
