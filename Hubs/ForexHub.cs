using Microsoft.AspNetCore.SignalR;
using MediatR;
using System.Security.Claims;

public class ForexHub : Hub
{
    private readonly IMediator _mediator;
    public ForexHub(IMediator mediator) => _mediator = mediator;

    public override Task OnConnectedAsync()
    {
        // log or track connection if needed
        return base.OnConnectedAsync();
    }

    public async Task Subscribe(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, symbol);
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        await _mediator.Send(new CreateSubscriptionAuditCommand(userId, symbol, "subscribe"));
    }

    public async Task Unsubscribe(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, symbol);
        var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
        await _mediator.Send(new CreateSubscriptionAuditCommand(userId, symbol, "unsubscribe"));
    }
}
