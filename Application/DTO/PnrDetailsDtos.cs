namespace SkyOpsQueueIntelligence.Application.DTO;

public sealed record PnrSegmentDetailDto(
    int SegmentNumber, string FlightNo, string StatusCode, string Airline,
    string Origin, string Destination, string DepartureTime, string ArrivalTime,
    string DepartureDate, int? DelayMinutes, decimal? DelayHours, string Action, string Reason);

public sealed record PnrFareSummaryDto(decimal? BaseFare, decimal? Taxes, decimal? Total);

public sealed record PnrDelayAnalysisDto(
    string Pnr, string? ReceivedFrom, string? AgentCode, string? CurrencyCode,
    string? RawResponse, IReadOnlyList<PnrSegmentDetailDto> Segments,
    PnrFareSummaryDto FareSummary, IReadOnlyList<PnrPassenger>? Passengers, string? TicketingDeadline,
    bool IsTicketed = false);
