using SkyOpsQueueIntelligence.Application.Helpers;
using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Helpers;

public static class Queue7Processor
{
    public static QueueAnalysisResult ProcessPnr(ParsedPnr pnr, int queueNumber)
    {
        var actions = new List<ActionFinding>();

        foreach (var segment in pnr.Segments)
        {
            var action = BuildAction(segment, pnr.RawText);
            actions.Add(action);
        }

        var notifyActions = actions.Where(action => action.ShouldNotify).ToList();
        var summary = notifyActions.Count == 0
            ? "No queue action required."
            : BuildSummary(notifyActions);

        return new QueueAnalysisResult(
            queueNumber,
            pnr.Pnr,
            pnr.ReceivedFrom,
            pnr.PCC,
            pnr.ReceivedDateTime,
            notifyActions.Count > 0,
            actions,
            Array.Empty<InformationalFinding>(),
            summary,
            pnr.Passengers,
            pnr.TicketingDeadline,
            pnr.CurrencyCode,
            pnr.BaseFare,
            pnr.Taxes,
            pnr.TotalFare,
            pnr.RemarkEmail,
            pnr.RawText,
            Airline: null,
            IsTicketed: pnr.IsTicketed);
    }

    public static IReadOnlyList<QueueAnalysisResult> ProcessQueueText(string queueText, int queueNumber = 7)
    {
        if (string.IsNullOrWhiteSpace(queueText))
        {
            return Array.Empty<QueueAnalysisResult>();
        }

        var parsed = Queue7Parser.ParseQueueText(queueText, queueNumber);
        return parsed.Pnrs.Select(p => ProcessPnr(p, queueNumber)).ToArray();
    }

    private static ActionFinding BuildAction(FlightSegment segment, string pnrText)
    {
        ActionFinding action = segment.Status switch
        {
            "TK" => BuildScheduleChangeAction(segment),
            "KK" => new ActionFinding(
                segment.Segment,
                segment.Flight,
                segment.Status,
                "Record KK status",
                ShouldNotify: false),
            "KL" => new ActionFinding(
                segment.Segment,
                segment.Flight,
                segment.Status,
                "Record KL status",
                ShouldNotify: false),
            "HK" => new ActionFinding(
                segment.Segment,
                segment.Flight,
                segment.Status,
                "Record HK status",
                RecommendedFutureCommand: null,
                QueueRecommendation: new QueueRecommendation(
                    "Informational Status",
                    "Low",
                    new[] {
                        $"Record HK segment {segment.Segment}",
                        "Track status for reference only",
                        "No notification required"
                    }),
                ShouldNotify: false),
            "HX" => new ActionFinding(
                segment.Segment,
                segment.Flight,
                segment.Status,
                "Remove Cancelled Segment (Recommendation Only)",
                RecommendedFutureCommand: "Remove segment after agent review",
                    Reason: ExtractCancellationReason(pnrText),
                    QueueRecommendation: new QueueRecommendation(
                        "Cancelled Segment",
                        "High",
                        new[] {
                            $"Review HX segment {segment.Segment}",
                            "Confirm cancellation reason with airline",
                            "Remove cancelled segment after agent review",
                            "Check ticket refund or reissue requirement"
                        })),
            "UN" => new ActionFinding(
                segment.Segment,
                segment.Flight,
                segment.Status,
                "Rebook Required",
                RecommendedFutureCommand: "Rebook segment after agent review",
                    QueueRecommendation: new QueueRecommendation(
                    "Unavailable Segment",
                    "High",
                    new[] {
                        $"Review UN segment {segment.Segment}",
                        "Contact airline to confirm availability",
                        "Rebook on alternative flight after agent review",
                        "Notify passenger of itinerary change"
                    })),
            "UC" => new ActionFinding(
                segment.Segment,
                segment.Flight,
                segment.Status,
                "Confirmation Required",
                RecommendedFutureCommand: "Confirm segment with airline after agent review",
                    QueueRecommendation: new QueueRecommendation(
                    "Unconfirmed Segment",
                    "Medium",
                    new[] {
                        $"Review UC segment {segment.Segment}",
                        "Contact airline to obtain confirmation",
                        "Verify seat availability and booking status"
                    })),
            "US" => new ActionFinding(
                segment.Segment,
                segment.Flight,
                segment.Status,
                "Resell Required",
                RecommendedFutureCommand: "Resell segment after agent review",
                    QueueRecommendation: new QueueRecommendation(
                    "Unable to Sell",
                    "Medium",
                    new[] {
                        $"Review US segment {segment.Segment}",
                        "Check fare availability and rebook",
                        "Contact airline if segment cannot be resold"
                    }),
                ShouldNotify: false),
            "WL" => new ActionFinding(
                segment.Segment,
                segment.Flight,
                segment.Status,
                "Monitor Waitlist",
                RecommendedFutureCommand: "Monitor waitlist after agent review",
                    QueueRecommendation: new QueueRecommendation(
                    "Waitlisted Segment",
                    "Low",
                    new[] {
                        $"Monitor WL segment {segment.Segment}",
                        "Check waitlist clearance status with airline",
                        "Arrange alternative if waitlist does not clear"
                    }),
                ShouldNotify: false),
            _ => new ActionFinding(
                segment.Segment,
                segment.Flight,
                segment.Status,
                $"Record {segment.Status} status",
                ShouldNotify: false)
        };

        action = action with
        {
            Origin = segment.Origin,
            Destination = segment.Destination,
            DepartureTime = segment.NewDepartureTime ?? segment.DepartureTime,
            ArrivalTime = segment.NewArrivalTime ?? segment.ArrivalTime,
            DepartureDate = segment.Date
        };

        return action;
    }

    private static ActionFinding BuildScheduleChangeAction(FlightSegment segment)
    {
        var delayMinutes = CalculateDelayMinutes(segment);
        decimal? delayHours = delayMinutes.HasValue ? Math.Round(delayMinutes.Value / 60m, 2) : null;

        return new ActionFinding(
            segment.Segment,
            segment.Flight,
            segment.Status,
            "Review Schedule Change",
            delayMinutes,
            delayHours,
            "Review schedule change after agent review",
            QueueRecommendation: new QueueRecommendation(
                "Schedule Change",
                "Medium",
                new[] {
                    $"Review TK segment {segment.Segment}",
                    "Verify minimum connection time",
                    "Check ticket revalidation/reissue requirement",
                    "Contact passenger if airline policy requires notification"
                }));
    }

    private static int? CalculateDelayMinutes(FlightSegment segment)
    {
        var oldTime = segment.OldDepartureTime ?? segment.DepartureTime;
        var newTime = segment.NewDepartureTime;

        if (oldTime is null || newTime is null)
        {
            return null;
        }

        if (!TryParseTime(oldTime, out var oldSpan) || !TryParseTime(newTime, out var newSpan))
        {
            return null;
        }

        var delay = (int)(newSpan - oldSpan).TotalMinutes;

        // Return negative values for early departures (do not wrap across midnight).
        return delay;
    }

    private static bool TryParseTime(string value, out TimeSpan time)
    {
        time = default;
        var normalized = Queue7Parser.NormalizeTime(value);

        if (normalized.Length != 4)
        {
            return false;
        }

        if (!int.TryParse(normalized[..2], out var hours) || !int.TryParse(normalized[2..], out var minutes))
        {
            return false;
        }

        if (hours is < 0 or > 23 || minutes is < 0 or > 59)
        {
            return false;
        }

        time = new TimeSpan(hours, minutes, 0);
        return true;
    }

    private static string BuildSummary(IReadOnlyCollection<ActionFinding> actions)
    {
        var grouped = actions
            .GroupBy(action => action.Status)
            .Select(group => group.Key switch
            {
                "TK" => CountPhrase(group.Count(), "schedule change"),
                "HX" => CountPhrase(group.Count(), "cancelled segment"),
                "UN" => CountPhrase(group.Count(), "unavailable segment"),
                "UC" => CountPhrase(group.Count(), "confirmation issue"),
                "US" => CountPhrase(group.Count(), "resell issue"),
                "WL" => CountPhrase(group.Count(), "waitlisted segment"),
                _ => CountPhrase(group.Count(), "actionable segment")
            });

        return string.Join(", ", grouped) + " detected.";
    }

    private static string? ExtractCancellationReason(string pnrText)
    {
        var lines = pnrText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        var cancellationReason = lines.FirstOrDefault(line =>
            line.Contains("CANCELLATION DUE TO", StringComparison.OrdinalIgnoreCase)
            || line.Contains("CANCELLED DUE TO", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(cancellationReason))
        {
            return CleanReason(cancellationReason);
        }

        var headerReason = lines.FirstOrDefault(line =>
            line.Contains("AIR SEGMENT CANCELLED", StringComparison.OrdinalIgnoreCase));

        return string.IsNullOrWhiteSpace(headerReason)
            ? null
            : CleanReason(headerReason);
    }

    private static string CleanReason(string value)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(value, @"^\d+\.\s*", "");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"^SSR\s+OTHS\s+\S+\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = cleaned.Replace("<Response>", "", StringComparison.OrdinalIgnoreCase);

        return System.Text.RegularExpressions.Regex.Replace(cleaned.Trim(), @"\s+", " ");
    }

    private static string CountPhrase(int count, string phrase)
    {
        return count == 1 ? $"1 {phrase}" : $"{count} {phrase}s";
    }
}
