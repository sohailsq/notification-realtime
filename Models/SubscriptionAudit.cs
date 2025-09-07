public class SubscriptionAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = null!;
    public string Symbol { get; set; } = null!;
    public string Action { get; set; } = null!; // "subscribe" / "unsubscribe"
    public DateTime At { get; set; } = DateTime.UtcNow;
}
