namespace SkyOpsQueueIntelligence.Application.DTO;

public sealed record ExecutiveSummaryDto(
     long CancelledPNR, long TotalFlightCanceled,
    long TimeChanges, long Unconfirmed, long TotalPccs,
    long TodayActions, long ActionsTakenLast7Days);

public sealed record QueueBreakdownItemDto(
    string? Pnr, string? Flight, string? TransactionId, string? Status,
    int Queue, string? Action, string? Reason, string? PCC, string? ProviderName, DateTime UpdatedAt);

public sealed record ExecutiveDashboardDto(ExecutiveSummaryDto Summary, IReadOnlyList<QueueBreakdownItemDto> QueueBreakdown);

public sealed record QueueByStatusDto(int Queue, string Status, long Total);
public sealed record QueueByHourDto(int Hour, long Total);
public sealed record QueuePerformanceDto(IReadOnlyList<QueueByStatusDto> ByStatus, IReadOnlyList<QueueByHourDto> ByHour);

public sealed record PccPerformanceItemDto(string PCC, long TotalActions, long Critical, long TimeChange, long UniquePnrs);
public sealed record PccPerformanceDto(IReadOnlyList<PccPerformanceItemDto> PccPerformance);

public sealed record FlightStatusItemDto(string Pnr, string Flight, string TransactionId, string Status, int Queue, string Action, string? PCC, string? ProviderName, DateTime UpdatedAt);
public sealed record FlightStatusSummaryDto(string Status, long Total);
public sealed record FlightStatusDto(IReadOnlyList<FlightStatusItemDto> Flights, IReadOnlyList<FlightStatusSummaryDto> Summary);

public sealed record CriticalQueueItemDto(string Pnr, string Flight, string TransactionId, string Status, int Queue, string Action, string? Reason, string? PCC, string? ProviderName, DateTime UpdatedAt);
public sealed record CriticalQueueDto( IReadOnlyList<CriticalQueueItemDto> CriticalItems, IReadOnlyList<CriticalQueueItemDto> UnticketedCriticalItems, long TicketedTotal, long UnticketedTotal);

public sealed record DelayItemDto(string Pnr, string Flight, string TransactionId, int? DelayMinutes, decimal? DelayHours, int Queue, string? PCC, string? ProviderName, DateTime UpdatedAt);
public sealed record DelayAnalysisDto(  int? PreponedPnrCount,decimal? PostponedPnrCount,long FlightChange, int? OntimePnrCount, IReadOnlyList<DelayItemDto> Delays);

public sealed record FlightImpactItemDto(string Pnr, string Flight, string TransactionId, string Status, int Queue, string Action, string? Reason, string? PCC, string? ProviderName, DateTime UpdatedAt);
public sealed record FlightImpactDto(IReadOnlyList<FlightImpactItemDto> ImpactedFlights, long TotalImpacted);

public sealed record PnrSegmentDto(string Pnr, string Flight, string Status, int Queue, int Segment, string Action, int? DelayMinutes, string? Reason, string? RecommendedCommand, string Summary, string? PCC, DateTime UpdatedAt);
public sealed record TopPnrDto(string Pnr, long Actions, string Statuses);
public sealed record PnrAnalysisDto(string? Pnr, IReadOnlyList<PnrSegmentDto>? Segments, IReadOnlyList<TopPnrDto>? TopPnrs);

public sealed record PnrRowDto(string PNR, string? PCC, string? ProviderName, int TK_Segments,int HX_Segments, int UN_Segments,int UC_Segments);
public sealed record PnrCountDto(long TotalPCC);
public sealed record PnrsDto(IReadOnlyList<PnrRowDto> ByPNR, IReadOnlyList<PnrCountDto> Count);

public sealed record LivePnrItemDto(string Pnr, string Flight, string TransactionId, string Status, int Queue, string Action, string? PCC, string? ProviderName, DateTime UpdatedAt);
public sealed record OperationalDashboardDto(long OpenCriticalCases, long TkQueueMonitor, long HxUnQueueMonitor, IReadOnlyList<LivePnrItemDto> LivePNR);

public sealed record PccRankingDto(string? PCC, long TotalActions, long Critical, decimal CriticalPct, long ScheduleChange, decimal TimeChangePct);
public sealed record QueueEfficiencyDto(int Queue, long PnrsProcessed, long ActionsGenerated);
public sealed record ProviderRankingDto(string? Provider, long TotalActions, long Critical, decimal CriticalPct);
public sealed record TransactionRankingDto(string? TransactionId, string? PCC, long TotalActions, long Critical, decimal CriticalPct);
public sealed record ManagementDashboardDto(IReadOnlyList<PccRankingDto> PccRanking, IReadOnlyList<QueueEfficiencyDto> QueueEfficiency, IReadOnlyList<ProviderRankingDto> ProviderRanking, IReadOnlyList<TransactionRankingDto> TransactionIdRanking, long TodayTotal, long TodayCritical, double CriticalPercentage);

public sealed record XmlLogItemDto(long LogId, string? Upl, string? WorkFlow, string? ModuleName, string? ModuleCode, string? ClassName, string? LogCode, string? LogXml, string? Remarks, DateTime LogDate);
public sealed record XmlLogsDto(int Total, IReadOnlyList<XmlLogItemDto> Logs);

public sealed record PriorityPnrStatusCardDto(
    long Id, string Pnr, string PriorityLevel, DateTime? TravelDate,
    string NotifyEmail, long HX, long TK, long UN, long UC, bool FoundInQueue);

public sealed record PriorityPnrStatusDto(IReadOnlyList<PriorityPnrStatusCardDto> Cards);

public sealed record ActionTakenItemDto(string? Pnr, string? TransactionId, DateTime? ReceivedDateTime, int? SegmentNumber, string? Flight, string? StatusCode, string? ActionText, DateTime? UpdatedAt, string? PCC, string? Remarks, string? ProviderName, string? CustomeRemarks);
public sealed record ActionTakenDto(int Total, IReadOnlyList<ActionTakenItemDto> Pending);

public sealed record ErrorLogItemDto(long LogId, string? Upl, string? WorkFlow, long? UserId, string? UserType, string? ModuleName, string? ModuleCode, string? ClassName, string? ProcedureName, string? ErrorCode, string? Remarks, DateTime? LogDate, string? Level, string? IpDetails, string? SessionId);
public sealed record ErrorLogsDto(int Total, IReadOnlyList<ErrorLogItemDto> Logs);
