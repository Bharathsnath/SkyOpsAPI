using System.Text.Json.Serialization;

namespace SkyOpsQueueIntelligence.Application.DTO;

public sealed record ParsedQueueResult(
    [property: JsonPropertyName("queue")] int Queue,
    [property: JsonPropertyName("pnrs")] IReadOnlyList<ParsedPnr> Pnrs);

public sealed record VendorLocatorInfo(
    [property: JsonPropertyName("locator")] string Locator,
    [property: JsonPropertyName("deadline")] string? Deadline);

public enum VendorRemarkType { MissingSsr, DuplicatePnr, AutoCancelWarning, ScheduleChange, Other }

public sealed record VendorRemarkAction(
    [property: JsonPropertyName("type")] VendorRemarkType Type,
    [property: JsonPropertyName("rawText")] string RawText,
    [property: JsonPropertyName("deadline")] string? Deadline = null,
    [property: JsonPropertyName("duplicateLocator")] string? DuplicateLocator = null,
    [property: JsonPropertyName("flight")] string? Flight = null);

public sealed record ParsedPnr(
    [property: JsonPropertyName("pnr")] string Pnr,
    [property: JsonPropertyName("rawText")] string RawText,
    [property: JsonPropertyName("receivedFrom")] string? ReceivedFrom,
    [property: JsonPropertyName("pcc")] string? PCC,
    [property: JsonPropertyName("receivedDateTime")] DateTime? ReceivedDateTime,
    [property: JsonPropertyName("segments")] IReadOnlyList<FlightSegment> Segments,
    [property: JsonPropertyName("passengers")] IReadOnlyList<PnrPassenger>? Passengers = null,
    [property: JsonPropertyName("ticketingDeadline")] string? TicketingDeadline = null,
    [property: JsonPropertyName("currencyCode")] string? CurrencyCode = null,
    [property: JsonPropertyName("baseFare")] decimal? BaseFare = null,
    [property: JsonPropertyName("taxes")] decimal? Taxes = null,
    [property: JsonPropertyName("totalFare")] decimal? TotalFare = null,
    [property: JsonPropertyName("remarkEmail")] string? RemarkEmail = null,
    [property: JsonPropertyName("isTicketed")] bool IsTicketed = false,
    [property: JsonPropertyName("vendorLocator")] VendorLocatorInfo? VendorLocator = null,
    [property: JsonPropertyName("vendorRemarks")] IReadOnlyList<VendorRemarkAction>? VendorRemarks = null);

public sealed record PnrPassenger(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("passengerJsNo")] string? PassengerJsNo = null,
    [property: JsonPropertyName("seat")] string? Seat = null,
    [property: JsonPropertyName("meal")] string? Meal = null);

public sealed record FlightSegment(
    [property: JsonPropertyName("segment")] int Segment,
    [property: JsonPropertyName("flight")] string Flight,
    [property: JsonPropertyName("carrier")] string Carrier,
    [property: JsonPropertyName("flightNumber")] string FlightNumber,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("destination")] string? Destination,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("departureTime")] string? DepartureTime,
    [property: JsonPropertyName("arrivalTime")] string? ArrivalTime,
    [property: JsonPropertyName("oldDepartureTime")] string? OldDepartureTime,
    [property: JsonPropertyName("newDepartureTime")] string? NewDepartureTime,
    [property: JsonPropertyName("oldArrivalTime")] string? OldArrivalTime,
    [property: JsonPropertyName("newArrivalTime")] string? NewArrivalTime);

public sealed record QueueAnalysisResult(
    [property: JsonPropertyName("queue")] int Queue,
    [property: JsonPropertyName("pnr")] string Pnr,
    [property: JsonPropertyName("receivedFrom")] string? ReceivedFrom,
    [property: JsonPropertyName("pcc")] string? PCC,
    [property: JsonPropertyName("receivedDateTime")] DateTime? ReceivedDateTime,
    [property: JsonPropertyName("requiresAction")] bool RequiresAction,
    [property: JsonPropertyName("actions")] IReadOnlyList<ActionFinding> Actions,
    [property: JsonPropertyName("informational")] IReadOnlyList<InformationalFinding> Informational,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("passengers")] IReadOnlyList<PnrPassenger>? Passengers = null,
    [property: JsonPropertyName("ticketingDeadline")] string? TicketingDeadline = null,
    [property: JsonPropertyName("currencyCode")] string? CurrencyCode = null,
    [property: JsonPropertyName("baseFare")] decimal? BaseFare = null,
    [property: JsonPropertyName("taxes")] decimal? Taxes = null,
    [property: JsonPropertyName("totalFare")] decimal? TotalFare = null,
    [property: JsonPropertyName("remarkEmail")] string? RemarkEmail = null,
    [property: JsonPropertyName("rawResponse")] string? RawResponse = null,
    [property: JsonPropertyName("airline")] string? Airline = null,
    [property: JsonPropertyName("isTicketed")] bool IsTicketed = false);

public sealed record QueueRecommendation(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("priority")] string Priority,
    [property: JsonPropertyName("recommendedActions")] IReadOnlyList<string> RecommendedActions);

public sealed record ActionFinding(
    [property: JsonPropertyName("segment")] int Segment,
    [property: JsonPropertyName("flight")] string Flight,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("delayMinutes")] int? DelayMinutes = null,
    [property: JsonPropertyName("delayHours")] decimal? DelayHours = null,
    [property: JsonPropertyName("recommendedFutureCommand")] string? RecommendedFutureCommand = null,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("queueRecommendation")] QueueRecommendation? QueueRecommendation = null,
    [property: JsonPropertyName("origin")] string? Origin = null,
    [property: JsonPropertyName("destination")] string? Destination = null,
    [property: JsonPropertyName("departureTime")] string? DepartureTime = null,
    [property: JsonPropertyName("arrivalTime")] string? ArrivalTime = null,
    [property: JsonPropertyName("departureDate")] string? DepartureDate = null,
    [property: JsonPropertyName("shouldNotify")] bool ShouldNotify = true);

public sealed record InformationalFinding(
    [property: JsonPropertyName("segment")] int Segment,
    [property: JsonPropertyName("flight")] string Flight,
    [property: JsonPropertyName("status")] string Status);

public sealed record DelayFlight(
    [property: JsonPropertyName("pnr")] string Pnr,
    [property: JsonPropertyName("flight")] string Flight,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("origin")] string? Origin,
    [property: JsonPropertyName("destination")] string? Destination,
    [property: JsonPropertyName("previousDeparture")] string? PreviousDeparture,
    [property: JsonPropertyName("previousArrival")] string? PreviousArrival,
    [property: JsonPropertyName("currentDeparture")] string? CurrentDeparture,
    [property: JsonPropertyName("currentArrival")] string? CurrentArrival);

public sealed record DelaySummaryResult(
    [property: JsonPropertyName("queue")] int Queue,
    [property: JsonPropertyName("totalDelayedFlights")] int TotalDelayedFlights,
    [property: JsonPropertyName("delayedFlights")] IReadOnlyList<DelayFlight> DelayedFlights);
