using Microsoft.AspNetCore.SignalR;

namespace SkyOpsQueueIntelligence.Hubs;

public sealed class CrmHub : Hub
{
    public Task JoinCase(string caseId) => Groups.AddToGroupAsync(Context.ConnectionId, $"crm:{caseId}");
    public Task LeaveCase(string caseId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"crm:{caseId}");

    public override Task OnConnectedAsync() => base.OnConnectedAsync();
}