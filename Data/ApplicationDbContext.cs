using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> opts) : base(opts) {}
    public DbSet<PriceTick> PriceTicks { get; set; } = null!;
    public DbSet<SubscriptionAudit> SubscriptionAudits { get; set; } = null!;
}
