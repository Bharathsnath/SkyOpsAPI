namespace SkyOpsQueueIntelligence.Application.DTO;

public sealed record CrmEmailDto(
    long Id,
    string CaseId,
    string Sender,
    string SenderEmail,
    string Subject,
    string Preview,
    string Body,
    string Type,
    string Pnr,
    string Priority,
    string Status,
    DateTimeOffset ReceivedAt,
    bool Starred,
    string? To = null,
    string? Cc = null,
    string? Issue = null);

public sealed record CrmCaseDto(
    long Id,
    string CaseNumber,
    string Pnr,
    string? CustomerName,
    string? CustomerEmail,
    string CaseType,
    string Priority,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SlaDueAt);

public sealed record CrmTimelineItemDto(
    long Id,
    string SenderType,
    string? SenderName,
    string Message,
    DateTimeOffset CreatedAt,
    string? ActionCode = null);

public sealed record CrmActionRequest(string ActionCode, string Source, object? ActionData = null);
public sealed record CrmMessageRequest(string Message);
public sealed record CrmSearchRequest(string? Query, string? Field = null, string? Type = null, int Page = 1, int PageSize = 50);
public sealed record Customer360Dto(
    long Id,
    string Name,
    string Email,
    string? Mobile,
    string? Company,
    int TotalBookings,
    int OpenCases,
    int ClosedCases,
    IReadOnlyList<string> RecentPnrs,
    IReadOnlyList<string> RecentCases);
