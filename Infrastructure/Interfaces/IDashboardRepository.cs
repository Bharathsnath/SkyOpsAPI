using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IDashboardRepository
{
    Task<ExecutiveDashboardDto> GetExecutiveDashboardAsync(int userId, CancellationToken ct = default);
    Task<QueuePerformanceDto> GetQueuePerformanceAsync(int userId, int? queueNumber, CancellationToken ct = default);
    Task<PccPerformanceDto> GetPccPerformanceAsync(int userId, string? pcc, CancellationToken ct = default);
    Task<FlightStatusDto> GetFlightStatusAsync(int userId, string? statusCode, CancellationToken ct = default);
    Task<CriticalQueueDto> GetCriticalQueueAsync(int userId, CancellationToken ct = default);
    Task<DelayAnalysisDto> GetDelayAnalysisAsync(int userId, CancellationToken ct = default);
    Task<FlightImpactDto> GetFlightImpactAsync(int userId, CancellationToken ct = default);
    Task<PnrAnalysisDto> GetPnrAnalysisAsync(int userId, string? pnr, CancellationToken ct = default);
    Task<PnrsDto> GetPnrsAsync(int userId, string? pnr, CancellationToken ct = default);
    Task<OperationalDashboardDto> GetOperationalDashboardAsync(int userId, CancellationToken ct = default);
    Task<ManagementDashboardDto> GetManagementDashboardAsync(CancellationToken ct = default);
    Task<XmlLogsDto> GetXmlLogsAsync(CancellationToken ct = default);
    Task<ActionTakenDto> GetActionTakenAsync(int userId, CancellationToken ct = default);
    Task<ErrorLogsDto> GetErrorLogsAsync(CancellationToken ct = default);
    Task<PriorityPnrStatusDto> GetPriorityPnrStatusAsync(CancellationToken ct = default);
    Task<object> GetAccessFilterDebugAsync(int userId, CancellationToken ct = default);
    Task<string> BuildAccessFilterAsync(int userId, CancellationToken ct = default);
}
