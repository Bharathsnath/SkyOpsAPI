namespace SkyOpsQueueIntelligence.Application.DTO;

public record AdmAnalysisDto
{
    public long? SalesAuditId { get; init; }
    public string Pnr { get; init; } = string.Empty;
    public string? TicketNo { get; init; }
    public string? TicketPcc { get; init; }
    public string? BookingPcc { get; init; }
    public string? TicketMarket { get; init; }
    public string? BookingMarket { get; init; }
    public bool IsCrossBorder { get; init; }
    public int ChurnedSegmentCount { get; init; }
    public bool IsChurnedSegment { get; init; }
    public int MarriedSegmentCount { get; init; }
    public bool IsMarriedSegment { get; init; }
    public int RiskScore { get; init; }
    public string Remarks { get; init; } = string.Empty;
    public string? TransactionId { get; init; }
    public IReadOnlyList<AdmAnalysisDetailDto> Details { get; init; } = Array.Empty<AdmAnalysisDetailDto>();
    public DateTime AnalyzedAt { get; init; }
}

public record AdmAnalysisDetailDto
{
    public string Rule { get; init; } = string.Empty;
    public bool Triggered { get; init; }
    public int Points { get; init; }
    public string Remarks { get; init; } = string.Empty;
}

public record DashboardDto
{
    public int TotalAnalyzed { get; init; }
    public int CrossBorder { get; init; }
    public int ChurnedSegment { get; init; }
    public int MarriedSegment { get; init; }
    public int AllThree { get; init; }
    public IReadOnlyList<AdmSummaryRowDto> Summary { get; init; } = Array.Empty<AdmSummaryRowDto>();
}

public record AdmSummaryRowDto(
    string Pnr,
    string? TicketNo,
    string? TicketPcc,
    string? BookingPcc,
    string? TicketMarket,
    string? BookingMarket,
    string IssueType,
    DateTime CreatedDate);

public record AdmKpiDto(
    int TotalPnrs,
    int AdmCases,
    int PendingAdm,
    int ClosedAdm,
    decimal RevenueImpact,
    int AvgRiskScore);

public record AdmTrendPointDto(string Date, int Count);
public record AdmStatusPieDto(int Pending, int Closed, int Waived);
public record AdmBarItemDto(string Label, int Count);
public record AdmReasonDto(string Reason, int Count);
public record AdmRevenueTrendDto(string Date, decimal Amount);

public record AdmDashboardDto(
    AdmKpiDto Kpi,
    IReadOnlyList<AdmTrendPointDto> Trend,
    AdmStatusPieDto StatusPie,
    IReadOnlyList<AdmBarItemDto> AirlineWise,
    IReadOnlyList<AdmBarItemDto> AgentWise,
    IReadOnlyList<AdmReasonDto> ReasonAnalysis,
    IReadOnlyList<AdmRevenueTrendDto> RevenueTrend);
