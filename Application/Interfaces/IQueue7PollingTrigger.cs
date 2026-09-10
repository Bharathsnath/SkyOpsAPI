namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IQueue7PollingTrigger
{
    Task TriggerAsync(CancellationToken cancellationToken);
    void Enable();
    void Disable();
    bool IsEnabled { get; }
}
