namespace SkyOpsQueueIntelligence.Application.DTO.Request;

public sealed record SabreCommandRequest(
    string OfficeId,
    string Pnr,
    string Queue,
    string Provider);

public sealed record SabreQueueProcessRequest(
    string OfficeId,
    string Pnr,
    int Queue = 7,
    string? Provider = null);

public sealed record SabreCommandResponse(
    string OfficeId,
    string Pnr,
    string Command,
    string ResponseText);
