using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Helpers.Adapters.DTOAdapters;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public sealed class DashboardService : IDashboardService
{
    private readonly IDashboardRepository _repository;
    private readonly DashboardDtoAdapter _adapter;

    public DashboardService(IDashboardRepository repository, DashboardDtoAdapter adapter)
    {
        _repository = repository;
        _adapter = adapter;
    }

    public async Task<ExecutiveDashboardDto> GetExecutiveDashboardAsync(int userId, CancellationToken ct = default)
        => _adapter.ToExecutiveDashboard(await _repository.GetExecutiveDashboardAsync(userId, ct));

    public async Task<QueuePerformanceDto> GetQueuePerformanceAsync(int userId, int? queueNumber, CancellationToken ct = default)
        => _adapter.ToQueuePerformance(await _repository.GetQueuePerformanceAsync(userId, queueNumber, ct));

    public async Task<PccPerformanceDto> GetPccPerformanceAsync(int userId, string? pcc, CancellationToken ct = default)
        => _adapter.ToPccPerformance(await _repository.GetPccPerformanceAsync(userId, pcc, ct));

    public async Task<FlightStatusDto> GetFlightStatusAsync(int userId, string? statusCode, CancellationToken ct = default)
        => _adapter.ToFlightStatus(await _repository.GetFlightStatusAsync(userId, statusCode, ct));

    public async Task<CriticalQueueDto> GetCriticalQueueAsync(int userId, CancellationToken ct = default)
        => _adapter.ToCriticalQueue(await _repository.GetCriticalQueueAsync(userId, ct));

    public async Task<DelayAnalysisDto> GetDelayAnalysisAsync(int userId, CancellationToken ct = default)
        => _adapter.ToDelayAnalysis(await _repository.GetDelayAnalysisAsync(userId, ct));

    public async Task<FlightImpactDto> GetFlightImpactAsync(int userId, CancellationToken ct = default)
        => _adapter.ToFlightImpact(await _repository.GetFlightImpactAsync(userId, ct));

    public async Task<PnrAnalysisDto> GetPnrAnalysisAsync(int userId, string? pnr, CancellationToken ct = default)
        => _adapter.ToPnrAnalysis(await _repository.GetPnrAnalysisAsync(userId, pnr, ct));

    public async Task<PnrsDto> GetPnrsAsync(int userId, string? pnr, CancellationToken ct = default)
        => _adapter.ToPnrs(await _repository.GetPnrsAsync(userId, pnr, ct));

    public async Task<OperationalDashboardDto> GetOperationalDashboardAsync(int userId, CancellationToken ct = default)
        => _adapter.ToOperationalDashboard(await _repository.GetOperationalDashboardAsync(userId, ct));

    public async Task<ManagementDashboardDto> GetManagementDashboardAsync(CancellationToken ct = default)
        => _adapter.ToManagementDashboard(await _repository.GetManagementDashboardAsync(ct));

    public async Task<XmlLogsDto> GetXmlLogsAsync(CancellationToken ct = default)
        => _adapter.ToXmlLogs(await _repository.GetXmlLogsAsync(ct));

    public async Task<ActionTakenDto> GetActionTakenAsync(int userId, CancellationToken ct = default)
        => _adapter.ToActionTaken(await _repository.GetActionTakenAsync(userId, ct));

    public async Task<ErrorLogsDto> GetErrorLogsAsync(CancellationToken ct = default)
        => _adapter.ToErrorLogs(await _repository.GetErrorLogsAsync(ct));

    public async Task<PriorityPnrStatusDto> GetPriorityPnrStatusAsync(CancellationToken ct = default)
        => await _repository.GetPriorityPnrStatusAsync(ct);

    public Task<object> GetAccessFilterDebugAsync(int userId, CancellationToken ct = default)
        => _repository.GetAccessFilterDebugAsync(userId, ct);
}
