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
    public int ChangedSegmentCount { get; init; }
    public bool IsChangedSegment { get; init; }
    public int MarriedSegmentCount { get; init; }
    public bool IsMarriedSegment { get; init; }
    public int RiskScore { get; init; }
    public string Remarks { get; init; } = string.Empty;
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
    public int Low { get; init; }
    public int Medium { get; init; }
    public int High { get; init; }
    public int Critical { get; init; }
}
