namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IQueue7PollingTrigger
{
    Task TriggerAsync(CancellationToken cancellationToken);
}
