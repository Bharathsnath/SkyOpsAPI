using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Helpers.Adapters.DTOAdapters;

public sealed class DashboardDtoAdapter
{
    public ExecutiveDashboardDto ToExecutiveDashboard(ExecutiveDashboardDto dto) => dto;
    public QueuePerformanceDto ToQueuePerformance(QueuePerformanceDto dto) => dto;
    public PccPerformanceDto ToPccPerformance(PccPerformanceDto dto) => dto;
    public FlightStatusDto ToFlightStatus(FlightStatusDto dto) => dto;
    public CriticalQueueDto ToCriticalQueue(CriticalQueueDto dto) => dto;
    public DelayAnalysisDto ToDelayAnalysis(DelayAnalysisDto dto) => dto;
    public FlightImpactDto ToFlightImpact(FlightImpactDto dto) => dto;
    public PnrAnalysisDto ToPnrAnalysis(PnrAnalysisDto dto) => dto;
    public PnrsDto ToPnrs(PnrsDto dto) => dto;
    public OperationalDashboardDto ToOperationalDashboard(OperationalDashboardDto dto) => dto;
    public ManagementDashboardDto ToManagementDashboard(ManagementDashboardDto dto) => dto;
    public XmlLogsDto ToXmlLogs(XmlLogsDto dto) => dto;
    public ActionTakenDto ToActionTaken(ActionTakenDto dto) => dto;
    public ErrorLogsDto ToErrorLogs(ErrorLogsDto dto) => dto;
}
