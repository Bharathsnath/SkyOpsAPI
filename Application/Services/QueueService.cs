using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Helpers.Adapters.DTOAdapters;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public sealed class QueueService : IQueueService
{
    private readonly IQueueActionRepository _repository;
    private readonly QueueDtoAdapter _adapter;
    private readonly IErrorLogService _errorLogService;

    public QueueService(IQueueActionRepository repository, QueueDtoAdapter adapter, IErrorLogService errorLogService)
    {
        _repository = repository;
        _adapter = adapter;
        _errorLogService = errorLogService;
    }

    public async Task<PnrDelayAnalysisDto?> GetDelayAnalysisByPnrAsync(string pnr, CancellationToken ct = default)
    {
        try { return _adapter.ToDelayAnalysis(await _repository.GetDelayAnalysisByPnrAsync(pnr, ct)); }
        catch (Exception ex) { await _errorLogService.LogAsync(ex, "QueueService", "SkyOpsQueueIntelligence", "SERVICE", nameof(GetDelayAnalysisByPnrAsync), nameof(QueueService), null, ct); throw; }
    }

    public async Task<bool> UpdateRemarksAsync(string pnr, int segmentNumber, string flight, string statusCode, string remarks, CancellationToken ct = default)
    {
        try { return await _repository.UpdateRemarksAsync(pnr, segmentNumber, flight, statusCode, remarks, ct); }
        catch (Exception ex) { await _errorLogService.LogAsync(ex, "QueueService", "SkyOpsQueueIntelligence", "SERVICE", nameof(UpdateRemarksAsync), nameof(QueueService), null, ct); throw; }
    }
}
