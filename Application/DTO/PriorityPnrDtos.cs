namespace SkyOpsQueueIntelligence.Application.DTO;

public sealed class PriorityPnrEntry
{
    public long Id { get; set; }
    public string Pnr { get; set; } = string.Empty;
    public string PriorityLevel { get; set; } = "MEDIUM";
    public DateTime? TravelDate { get; set; }

    /// <summary>Comma/semicolon-separated email addresses to notify when this PNR is found in polling.</summary>
    public string? NotifyEmail { get; set; }
    public string? Users { get; set; }

    public int IsActive { get; set; } = 1;
    public int CreatedBy { get; set; }
    public string? CreatedByUser { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int? ModifiedBy { get; set; }
    public string? ModifiedByUser { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
