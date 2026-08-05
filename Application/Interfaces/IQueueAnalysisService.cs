using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Response;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IQueueAnalysisService
{
    bool IsDatabaseConfigured { get; }
    ParsedQueueResult ParseQueueText(string queueText, int queueNumber = 7);
    IReadOnlyList<FlightSegment> ParseSegments(string pnrText);
    IReadOnlyList<QueueAnalysisResult> Analyze(string queueText, int queueNumber = 7);
    Task<QueueStoreResult> AnalyzeAndStoreAsync(string queueText, int queueNumber = 7, CancellationToken cancellationToken = default);
    Task<QueueStoreResult> FetchAnalyzeAndStoreAsync(int queueNumber, CancellationToken cancellationToken = default);
    Task<DelaySummaryResult> GetDelaySummaryAsync(int queueNumber, CancellationToken cancellationToken = default);
    Task<QueueSummaryResult> GetSummaryAsync(int queueNumber, CancellationToken cancellationToken = default);
}
