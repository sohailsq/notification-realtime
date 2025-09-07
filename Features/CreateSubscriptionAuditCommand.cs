using MediatR;

public record CreateSubscriptionAuditCommand(string UserId, string Symbol, string Action) : IRequest;

public class CreateSubscriptionAuditHandler : IRequestHandler<CreateSubscriptionAuditCommand>
{
    private readonly ApplicationDbContext _db;
    public CreateSubscriptionAuditHandler(ApplicationDbContext db) => _db = db;

    public async Task<Unit> Handle(CreateSubscriptionAuditCommand req, CancellationToken ct)
    {
        _db.SubscriptionAudits.Add(new SubscriptionAudit { UserId = req.UserId, Symbol = req.Symbol, Action = req.Action, At = DateTime.UtcNow });
        await _db.SaveChangesAsync(ct);
        return Unit.Value;
    }
}
