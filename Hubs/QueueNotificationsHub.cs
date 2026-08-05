using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Hubs;

public sealed class QueueNotificationsHub : Hub
{
    private readonly IErrorLogService _errorLogService;
    private readonly ILogger<QueueNotificationsHub> _logger;

    public QueueNotificationsHub(IErrorLogService errorLogService, ILogger<QueueNotificationsHub> logger)
    {
        _errorLogService = errorLogService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Hub OnConnectedAsync failed for connection {ConnectionId}", Context.ConnectionId);
            await _errorLogService.LogAsync(ex, "HubConnect", "SkyOpsQueueIntelligence", "HUB", "OnConnectedAsync", nameof(QueueNotificationsHub));
            throw;
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception is not null)
        {
            _logger.LogError(exception, "Hub OnDisconnectedAsync error for connection {ConnectionId}", Context.ConnectionId);
            await _errorLogService.LogAsync(exception, "HubDisconnect", "SkyOpsQueueIntelligence", "HUB", "OnDisconnectedAsync", nameof(QueueNotificationsHub));
        }
        await base.OnDisconnectedAsync(exception);
    }

    public static async Task SendQueueNotificationAsync(IHubContext<QueueNotificationsHub> hub, string message, CancellationToken ct = default)
        => await hub.Clients.All.SendAsync("QueueNotification", new { message }, ct);

    public static async Task SendQueueSummaryAsync(IHubContext<QueueNotificationsHub> hub, string summary, CancellationToken ct = default)
        => await hub.Clients.All.SendAsync("QueueProcessingSummary", new { message = summary }, ct);

    public static async Task SendWorkflowLogAsync(IHubContext<QueueNotificationsHub> hub, string level, string message, CancellationToken ct = default)
        => await hub.Clients.All.SendAsync("WorkflowLog", new { level, message, timestamp = DateTimeOffset.UtcNow }, ct);
}
