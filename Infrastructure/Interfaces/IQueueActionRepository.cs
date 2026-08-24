using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IQueueActionRepository
{
    bool IsConfigured { get; }
    bool IsLogConfigured { get; }

    Task<(int Saved, IReadOnlyList<QueueAnalysisResult> ChangedResults)> SaveRecommendedActionsAsync(
        IReadOnlyList<QueueAnalysisResult> analysisResults,
        string uplId = "",
        string providerName = "",
        CancellationToken cancellationToken = default);

    Task<int> MarkPnrsNotInQueueAsync(
        int queueNumber,
        IReadOnlyCollection<string> currentPnrs,
        CancellationToken cancellationToken = default);

    Task SaveProcessingLogAsync(
        string sourceId,
        string contentHash,
        int pnrCount,
        int actionCount,
        int pccCount,
        string status,
        string message,
        string uplId = "",
        CancellationToken cancellationToken = default);

    Task SaveApiLogAsync(
        string pccCode,
        string serviceName,
        string hostCommand,
        string requestXml,
        string responseXml,
        int httpStatusCode,
        string status,
        string uplId,
        string workFlow = "QueuePolling",
        string moduleName = "SabreQueueMCP",
        string moduleCode = "QUEUE",
        CancellationToken cancellationToken = default);

    Task<bool> UpdateRemarksAsync(
        string pnr,
        int segmentNumber,
        string flight,
        string statusCode,
        string remarks,
        int remarkUpdatedBy,
        CancellationToken cancellationToken = default);
    Task<bool> UpdateAgentRemarksAsync(
        string pnr,
        int segmentNumber,
        string flight,
        string statusCode,
        string remarks,
        CancellationToken cancellationToken = default);

    Task<PnrDelayAnalysisDto?> GetDelayAnalysisByPnrAsync(string pnr, CancellationToken cancellationToken = default);
}
