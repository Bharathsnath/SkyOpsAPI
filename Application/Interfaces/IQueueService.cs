using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IQueueService
{
    Task<PnrDelayAnalysisDto?> GetDelayAnalysisByPnrAsync(string pnr, CancellationToken ct = default);
    Task<bool> UpdateRemarksAsync(string pnr, int segmentNumber, string flight, string statusCode, string remarks, CancellationToken ct = default);
}
