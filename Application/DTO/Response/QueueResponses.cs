using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.DTO.Response;

public sealed record QueueStoreResult(int Queue, int PnrCount, int SavedActionCount, bool DatabaseConfigured)
{
    public string Message => DatabaseConfigured
        ? "Recommended actions stored in MySQL."
        : "MySQL connection string is not configured. No actions were stored.";
}

public sealed record QueueSummaryResult(int Queue, int TotalPnrs, int ActionablePnrs, int TotalActions, string Summary, IReadOnlyList<PnrActionSummary> Pnrs);
public sealed record PnrActionSummary(string Pnr, bool RequiresAction, string Summary, IReadOnlyList<ActionFinding> Actions);
