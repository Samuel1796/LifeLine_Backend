using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Nook.Api.Hubs;

[Authorize]
public class NotificationHub : Hub
{
    // Each authenticated user joins a personal group so the server can
    // target notifications at specific users by id if ever needed.
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (userId is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user-{userId}");

        await base.OnConnectedAsync();
    }
}
