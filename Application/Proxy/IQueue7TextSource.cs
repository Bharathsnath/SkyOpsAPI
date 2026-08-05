namespace SkyOpsQueueIntelligence.Application.Proxy;

public interface IQueue7TextSource
{
    Task<Queue7TextSourceResult> GetQueueTextAsync(CancellationToken cancellationToken);
    Task<Queue7TextSourceResult> GetQueueTextForCommandAsync(string hostCommand, CancellationToken cancellationToken);
    Task<Queue7TextSourceResult> GetQueueAnalysisTextForCommandAsync(string hostCommand, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetAllQueuePnrsAsync(string hostCommand, CancellationToken cancellationToken);
}

public sealed record Queue7TextSourceResult(string QueueText, string SourceId, string ConversationId);
