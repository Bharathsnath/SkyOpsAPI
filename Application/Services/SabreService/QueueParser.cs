using System.Globalization;
using System.Text.RegularExpressions;
using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Helpers;

public static partial class Queue7Parser
{
    private static readonly string[] KnownStatuses = new[] { "HK", "KK", "KL", "TK", "HX", "UN", "UC", "US", "WL", "NO" };

    public static ParsedQueueResult ParseQueueText(string queueText, int queueNumber = 7)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueText);

        var blocks = SplitPnrBlocks(queueText);
        var pnrs = blocks.Select(block =>
        {
            var pnr = ExtractPnr(block);
            var receivedFrom = ExtractReceivedFrom(block);
            var pcc = ExtractPCC(block);
            var receivedDateTime = ExtractReceivedDateTime(block);
            var segments = ParseSegments(block);
            var passengers = ExtractPassengers(block);
            var ticketingDeadline = ExtractTicketingDeadline(block);
            var (currencyCode, baseFare, taxes, totalFare) = ExtractFare(block);
            var remarkEmail = ExtractRemarkEmail(block);
            var isTicketed = ExtractIsTicketed(block);
            var vendorLocator = ParseVendorLocator(block);
            var vendorRemarks = ParseVendorRemarks(block);
            // Prefer VLOC deadline over TKT deadline when present
            var effectiveDeadline = vendorLocator?.Deadline ?? ticketingDeadline;
            return new ParsedPnr(pnr, block.Trim(), receivedFrom, pcc, receivedDateTime, segments, passengers, effectiveDeadline, currencyCode, baseFare, taxes, totalFare, remarkEmail, isTicketed, vendorLocator, vendorRemarks);
        }).ToArray();

        return new ParsedQueueResult(queueNumber, pnrs);
    }

    public static IReadOnlyList<FlightSegment> ParseSegments(string pnrText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pnrText);

        var segments = new List<FlightSegment>();
        var inSeatsSection = false;

        foreach (var rawLine in pnrText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();

            if (line.StartsWith("SEATS/BOARDING PASS", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("SEATS/", StringComparison.OrdinalIgnoreCase))
            {
                inSeatsSection = true;
                continue;
            }

            // Any new all-caps section header ends the seats section
            if (inSeatsSection && line.Length > 3 && line == line.ToUpperInvariant() && !line.Any(char.IsDigit))
                inSeatsSection = false;

            if (inSeatsSection)
                continue;

            var parsed = ParseSegmentLine(line);
            if (parsed is not null)
                segments.Add(parsed);
        }

        return ApplyScheduleChanges(pnrText, segments);
    }

    private static IReadOnlyList<string> SplitPnrBlocks(string queueText)
    {
        var responseMatches = ResponseBlockRegex().Matches(queueText);
        if (responseMatches.Count > 0)
        {
            return responseMatches
                .Select(match => match.Value)
                .ToArray();
        }

        var noPicBlocks = SplitNoPicCodeBlocks(queueText);
        if (noPicBlocks.Count > 0)
        {
            return noPicBlocks;
        }

        // Split on queue category header lines like "024  AIR SEGMENT CANCELLED"
        var queueCategoryBlocks = SplitQueueCategoryBlocks(queueText);
        if (queueCategoryBlocks.Count > 0)
        {
            return queueCategoryBlocks;
        }

        var matches = PnrHeaderRegex().Matches(queueText);

        if (matches.Count == 0)
        {
            return new[] { queueText };
        }

        // Keep each PNR header block separate — do NOT merge by PNR name,
        // because the same PNR may appear multiple times with different segments
        // (e.g. Galileo returns one segment per I-command response).
        var blocks = new List<string>();
        for (var index = 0; index < matches.Count; index++)
        {
            var start = matches[index].Index;
            var end = index + 1 < matches.Count ? matches[index + 1].Index : queueText.Length;
            blocks.Add(queueText[start..end]);
        }
        return blocks;
    }

    private static IReadOnlyList<string> SplitQueueCategoryBlocks(string queueText)
    {
        var matches = QueueCategoryHeaderRegex().Matches(queueText);
        if (matches.Count < 2)
        {
            // Only useful if there are multiple PNR blocks; single match means one PNR
            if (matches.Count == 1)
                return new[] { queueText };
            return Array.Empty<string>();
        }

        var blocks = new List<string>();
        for (var i = 0; i < matches.Count; i++)
        {
            var start = matches[i].Index;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : queueText.Length;
            var block = queueText[start..end].Trim();
            if (!string.IsNullOrWhiteSpace(block))
                blocks.Add(block);
        }

        return blocks;
    }

    private static IReadOnlyList<string> SplitNoPicCodeBlocks(string queueText)
    {
        var blocks = new List<string>();
        List<string>? currentBlock = null;

        foreach (var line in queueText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            if (line.Contains("NO PIC CODE", StringComparison.OrdinalIgnoreCase))
            {
                if (currentBlock is { Count: > 0 })
                {
                    blocks.Add(string.Join(Environment.NewLine, currentBlock).Trim());
                }

                currentBlock = new List<string> { line };
                continue;
            }

            currentBlock?.Add(line);
        }

        if (currentBlock is { Count: > 0 })
        {
            blocks.Add(string.Join(Environment.NewLine, currentBlock).Trim());
        }

        return blocks
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToArray();
    }

    private static string ExtractPnr(string block)
    {
        // Try Sabre header format: 3A78.3A78*ATT 0619/16FEB26 AFZPJI H
        var sabreHeaderMatch = SabreHeaderPnrRegex().Match(block);
        if (sabreHeaderMatch.Success)
        {
            var candidate = sabreHeaderMatch.Groups["pnr"].Value.Trim().ToUpperInvariant();
            if (LooksLikePnr(candidate))
            {
                return candidate;
            }
        }

        // Try Galileo header format: G28S5G/WS LONOU 6TP2GWS AG ...
        var galileoHeaderMatch = GalileoHeaderPnrRegex().Match(block);
        if (galileoHeaderMatch.Success)
        {
            var candidate = galileoHeaderMatch.Groups["pnr"].Value.Trim().ToUpperInvariant();
            if (LooksLikePnr(candidate))
            {
                return candidate;
            }
        }

        var pnrHeaderMatch = PnrHeaderRegex().Match(block);
        if (pnrHeaderMatch.Success)
        {
            var candidate = pnrHeaderMatch.Groups["pnr"].Value.Trim().ToUpperInvariant();
            if (LooksLikePnr(candidate))
            {
                return candidate;
            }
        }

        var bareLocator = block
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => BareLocatorRegex().Match(line))
            .FirstOrDefault(locatorMatch => locatorMatch.Success);

        return bareLocator is not null
            ? bareLocator.Groups["pnr"].Value.ToUpperInvariant()
            : "UNKNOWN";
    }

    private static bool LooksLikePnr(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Length is < 3 or > 12)
        {
            return false;
        }

        if (normalized.Contains(' '))
        {
            return false;
        }

        if (normalized is "SPLIT" or "PARSING" or "TAKING" or "PNR" or "RECORD" or "LOCATOR" or "RECLOC")
        {
            return false;
        }

        return normalized.Any(char.IsDigit)
            || normalized.Contains('*')
            || normalized.Contains('.')
            || normalized.Contains('-')
            || normalized.Length is >= 5 and <= 8 && normalized.All(char.IsLetterOrDigit);
    }

    private static string? ExtractReceivedFrom(string block)
    {
        // Sabre: "RECEIVED FROM - AGENTNAME"
        var match = ReceivedFromRegex().Match(block);
        if (match.Success)
            return EmptyToNull(match.Groups["receivedFrom"].Value
                .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault() ?? string.Empty);

        // Galileo header: GQHTZG/WS LONOU 6TP2GWS AG 91215600 28APR
        // TransactionId = "AG 91215600" (agent code + number)
        var galileoMatch = GalileoReceivedFromRegex().Match(block);
        if (galileoMatch.Success)
            return EmptyToNull(galileoMatch.Groups["agentCode"].Value.Trim());

        return null;
    }

    private static string? ExtractPCC(string block)
    {
        var match = PCCRegex().Match(block);
        if (match.Success)
            return EmptyToNull(match.Groups["officeId"].Value.Trim());

        var galileoMatch = GalileoPCCRegex().Match(block);
        return galileoMatch.Success
            ? EmptyToNull(galileoMatch.Groups["officeId"].Value.Trim())
            : null;
    }

    private static DateTime? ExtractReceivedDateTime(string block)
    {
        // Sabre format: 0619/16FEB26
        var match = ReceivedDateTimeRegex().Match(block);
        if (match.Success)
        {
            var rawValue = match.Groups["received"].Value.Trim().ToUpperInvariant();
            if (DateTime.TryParseExact(rawValue, "HHmm/ddMMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
        }

        // Galileo format: 28APR or 28APR25 (ddMMM or ddMMMyy) at end of header line
        var galileoMatch = GalileoReceivedDateRegex().Match(block);
        if (galileoMatch.Success)
        {
            var raw = galileoMatch.Groups["date"].Value.Trim().ToUpperInvariant();
            if (DateTime.TryParseExact(raw, "ddMMMyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt2))
                return dt2;
            if (DateTime.TryParseExact(raw, "ddMMM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt3))
                return new DateTime(DateTime.UtcNow.Year, dt3.Month, dt3.Day);
        }

        return null;
    }

    private static FlightSegment? ParseSegmentLine(string line)
    {
        var match = SegmentLineRegex().Match(line);

        if (!match.Success)
        {
            return null;
        }

        var status = match.Groups["status"].Value.ToUpperInvariant();

        if (!KnownStatuses.Contains(status))
        {
            return null;
        }

        var segment = int.Parse(match.Groups["segment"].Value);
        var carrier = match.Groups["carrier"].Value.ToUpperInvariant();
        var flightNumber = match.Groups["flightNumber"].Value;
        var flight = carrier + flightNumber;
        var timeText = match.Groups["times"].Success ? match.Groups["times"].Value : string.Empty;

        // Support both space-separated (Sabre) and concatenated (Galileo) origin/destination
        var origin = match.Groups["origin"].Success && !string.IsNullOrEmpty(match.Groups["origin"].Value)
            ? match.Groups["origin"].Value
            : match.Groups["origin6"].Value;
        var destination = match.Groups["destination"].Success && !string.IsNullOrEmpty(match.Groups["destination"].Value)
            ? match.Groups["destination"].Value
            : match.Groups["destination6"].Value;

        var (departure, arrival) = ExtractPrimaryTimes(timeText);
        var (oldDeparture, newDeparture) = ExtractChangedTimes(line, "DEP");
        var (oldArrival, newArrival) = ExtractChangedTimes(line, "ARR");

        return new FlightSegment(
            segment,
            flight,
            carrier,
            flightNumber,
            EmptyToNull(match.Groups["date"].Value),
            EmptyToNull(origin),
            EmptyToNull(destination),
            status,
            departure,
            arrival,
            oldDeparture,
            newDeparture,
            oldArrival,
            newArrival);
    }

    private static IReadOnlyList<FlightSegment> ApplyScheduleChanges(string pnrText, IReadOnlyList<FlightSegment> segments)
    {
        if (segments.Count == 0)
        {
            return segments;
        }

        var changes = ExtractScheduleChanges(pnrText);

        return segments
            .Select(segment =>
            {
                if (!changes.TryGetValue(segment.Flight, out var change))
                {
                    return segment;
                }

                return segment with
                {
                    DepartureTime = change.OldDeparture,
                    ArrivalTime = change.OldArrival,
                    OldDepartureTime = change.OldDeparture,
                    NewDepartureTime = change.NewDeparture ?? segment.DepartureTime,
                    OldArrivalTime = change.OldArrival,
                    NewArrivalTime = change.NewArrival ?? segment.ArrivalTime
                };
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, ScheduleChangeTimes> ExtractScheduleChanges(string pnrText)
    {
        var changes = new Dictionary<string, ScheduleChangeTimes>(StringComparer.OrdinalIgnoreCase);
        var lines = pnrText
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .ToArray();

        foreach (var line in lines)
        {
            var previousTimeMatch = PreviousTimeRegex().Match(line);

            if (!previousTimeMatch.Success)
            {
                continue;
            }

            var flight = NormalizeFlight(previousTimeMatch.Groups["flight"].Value);
            changes[flight] = new ScheduleChangeTimes(
                NormalizeTime(previousTimeMatch.Groups["dep"].Value),
                NormalizeTime(previousTimeMatch.Groups["arr"].Value),
                null,
                null);
        }

        for (var index = 0; index < lines.Length - 2; index++)
        {
            var flightMatch = ScheduleFlightRegex().Match(lines[index]);

            if (!flightMatch.Success)
            {
                continue;
            }

            var oldMatch = OldTimesRegex().Match(lines[index + 1]);
            var newMatch = NewTimesRegex().Match(lines[index + 2]);

            if (!oldMatch.Success || !newMatch.Success)
            {
                continue;
            }

            var flight = flightMatch.Groups["flight"].Value.ToUpperInvariant();
            changes[flight] = new ScheduleChangeTimes(
                NormalizeTime(oldMatch.Groups["dep"].Value),
                NormalizeTime(oldMatch.Groups["arr"].Value),
                NormalizeTime(newMatch.Groups["dep"].Value),
                NormalizeTime(newMatch.Groups["arr"].Value));
        }

        return changes;
    }

    private static (string? Departure, string? Arrival) ExtractPrimaryTimes(string value)
    {
        var matches = TimeRegex().Matches(value);

        return matches.Count switch
        {
            0 => (null, null),
            1 => (NormalizeTime(matches[0].Value), null),
            _ => (NormalizeTime(matches[0].Value), NormalizeTime(matches[1].Value))
        };
    }

    private static (string? OldTime, string? NewTime) ExtractChangedTimes(string line, string label)
    {
        var regex = new Regex($@"\b{label}\s*(?:TIME)?\s*(?<old>\d{{3,4}})\s*(?:-|/|TO|>)\s*(?<new>\d{{3,4}})\b", RegexOptions.IgnoreCase);
        var match = regex.Match(line);

        return match.Success
            ? (NormalizeTime(match.Groups["old"].Value), NormalizeTime(match.Groups["new"].Value))
            : (null, null);
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.ToUpperInvariant();
    }

    private static string NormalizeFlight(string value)
    {
        return Regex.Replace(value.ToUpperInvariant(), @"\s+", "");
    }

    public static string NormalizeTime(string value)
    {
        var trimmed = value.Trim();
        var digits = new string(value.Where(char.IsDigit).ToArray());

        if (digits.Length is not (3 or 4))
        {
            return value;
        }

        var normalized = digits.Length == 3 ? "0" + digits : digits;
        var suffix = trimmed[^1];

        if (!char.Equals(suffix, 'A') && !char.Equals(suffix, 'P'))
        {
            return normalized;
        }

        var hours = int.Parse(normalized[..2]);
        var minutes = int.Parse(normalized[2..]);

        if (char.Equals(suffix, 'A'))
        {
            hours = hours == 12 ? 0 : hours;
        }
        else
        {
            hours = hours == 12 ? 12 : hours + 12;
        }

        return $"{hours:00}{minutes:00}";
    }

    [GeneratedRegex(@"^\s*\d{1,3}\s{2,}.+(?:CANCELLED|SCHEDULE CHANGE|TIME LIMIT|WAITLIST|REQUESTED)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex QueueCategoryHeaderRegex();

    private static IReadOnlyList<PnrPassenger> ExtractPassengers(string block)
    {
        var meal = ExtractMeal(block);
        var passengers = new List<PnrPassenger>();
        var seatsByJsNo = ExtractSeatsByPassenger(block);

        foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (Match m in PassengerRegex().Matches(line))
            {
                var name = m.Groups["name"].Value.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var jsNo = EmptyToNull(m.Groups["jsno"].Value.Trim());
                var seat = jsNo is not null && seatsByJsNo.TryGetValue(jsNo, out var s) ? s : ExtractSeat(line);
                passengers.Add(new PnrPassenger(name, jsNo, seat, meal));
            }
        }

        return passengers.Count > 0 ? passengers : Array.Empty<PnrPassenger>();
    }

    private static Dictionary<string, string> ExtractSeatsByPassenger(string block)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var m = SeatBoardingPassRegex().Match(line);
            if (m.Success)
            {
                result.TryAdd(m.Groups["jsno"].Value.Trim(), m.Groups["seat"].Value.Trim().ToUpperInvariant());
            }
        }
        return result;
    }

    private static string? ExtractTicketingDeadline(string block)
    {
        var match = TicketingDeadlineRegex().Match(block);
        if (match.Success)
        {
            return match.Groups["deadline"].Value.Trim();
        }

        // Try ADTK SSR line: "SSR ADTK 1B TO QR BY 22MAY 1540 BOM"
        var adtkMatch = AdtkDeadlineRegex().Match(block);
        return adtkMatch.Success ? adtkMatch.Groups["deadline"].Value.Trim() : null;
    }

    private static string? ExtractRemarkEmail(string block)
    {
        var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Parse remark format: N./LABEL//DOMAIN.TLD  → label@domain.tld
        foreach (Match m in s_remarkEmailRegex.Matches(block))
        {
            var local = m.Groups["local"].Value.Replace("/", ".").Trim('.');
            var domain = m.Groups["domain"].Value;
            emails.Add($"{local}@{domain}".ToLowerInvariant());
        }

        // Also capture any plain email addresses already in the block
        foreach (Match m in EmailRegex().Matches(block))
            emails.Add(m.Groups["email"].Value.Trim().ToLowerInvariant());

        return emails.Count == 0 ? null : string.Join(";", emails);
    }

    private static bool ExtractIsTicketed(string block)
    {
        // Galileo: ** ELECTRONIC DATA EXISTS ** >*HTE; or ** TINS REMARKS EXIST ** >*HTI;
        if (GalileoTicketedRegex().IsMatch(block))
            return true;

        // Sabre: ACCOUNTING DATA section exists
        if (AccountingDataRegex().IsMatch(block))
            return true;

        var tktMatch = TktTimeLimitRegex().Match(block);
        if (!tktMatch.Success)
            return false;

        // TAW/ alone (no office/signing) = unticketed
        // T-DATE-OFFICEID*SIGN or TAW/OFFICEID*SIGN = ticketed
        var tktValue = tktMatch.Groups["tkt"].Value.Trim();
        return TicketedTktRegex().IsMatch(tktValue);
    }

    private static string? ExtractSeat(string line)
    {
        var match = SeatRegex().Match(line);
        return match.Success ? match.Groups["seat"].Value.Trim().ToUpperInvariant() : null;
    }

    private static string? ExtractMeal(string block)
    {
        var meals = block
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => MealRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["meal"].Value.Trim().ToUpperInvariant())
            .Where(meal => !string.IsNullOrWhiteSpace(meal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return meals.Length == 0 ? null : string.Join(";", meals);
    }

    private static string ExtractAccountingBlock(string block)
    {
        var start = block.IndexOf("ACCOUNTING DATA", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return string.Empty;

        // Find the next section header after ACCOUNTING DATA
        var afterHeader = start + "ACCOUNTING DATA".Length;
        var nextSection = FindNextSectionStart(block, afterHeader);
        return nextSection > afterHeader
            ? block[afterHeader..nextSection]
            : block[afterHeader..];
    }

    private static int FindNextSectionStart(string block, int from)
    {
        var lines = block[from..].Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var pos = from;
        var firstLine = true;
        foreach (var line in lines)
        {
            if (!firstLine)
            {
                var trimmed = line.Trim();
                // A section header is an all-caps line with no leading digits/dots (e.g. "REMARKS", "RECEIVED FROM")
                if (trimmed.Length > 3
                    && trimmed == trimmed.ToUpperInvariant()
                    && !trimmed.Any(char.IsDigit)
                    && !trimmed.StartsWith('.'))
                    return pos;
            }
            firstLine = false;
            pos += line.Length + 1; // +1 for newline
        }
        return -1;
    }

    private static (string? CurrencyCode, decimal? BaseFare, decimal? Taxes, decimal? TotalFare) ExtractFare(string block)
    {
        // Only scan lines within the ACCOUNTING DATA section for currency/fare
        var accountingBlock = ExtractAccountingBlock(block);
        var lines = accountingBlock.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

        var currency = lines
            .Select(line => AccountingCurrencyRegex().Match(line))
            .FirstOrDefault(match => match.Success);

        var currencyCode = currency?.Groups["currency"].Value.Trim().ToUpperInvariant();

        var slashFares = lines
            .Select(line => s_accountingSlashFareRegex.Match(line))
            .Where(match => match.Success)
            .ToArray();

        if (slashFares.Length > 0)
        {
            currencyCode ??= slashFares[0].Groups["currency"].Value.Trim().ToUpperInvariant();
            var slashBaseFare = slashFares.Sum(m => TryParseAmount(m.Groups["base"].Value) ?? 0);
            var slashTaxes = slashFares.Sum(m => TryParseAmount(m.Groups["tax"].Value) ?? 0);
            return (currencyCode, slashBaseFare, slashTaxes, slashBaseFare + slashTaxes);
        }

        var baseFare = TryParseAmount(lines
            .Select(line => AccountingBaseRegex().Match(line))
            .FirstOrDefault(match => match.Success)
            ?.Groups["base"].Value);

        var taxValues = lines
            .Select(line => AccountingTaxRegex().Matches(line))
            .SelectMany(matches => matches.Cast<Match>())
            .Where(match => match.Success)
            .Select(match => TryParseAmount(match.Groups["tax"].Value))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        decimal? taxes = taxValues.Length == 0 ? null : taxValues.Sum();

        var totalFare = TryParseAmount(lines
            .Select(line => AccountingTotalRegex().Match(line))
            .FirstOrDefault(match => match.Success)
            ?.Groups["total"].Value);

        if (totalFare is null && baseFare.HasValue && taxes.HasValue)
        {
            totalFare = baseFare.Value + taxes.Value;
        }

        return (currencyCode, baseFare, taxes, totalFare);
    }

    // Matches a single passenger token anywhere in a line, e.g. "1.1MUKHERJEE/SUBHRO MR" or "2.1MUKHERJEE/SIKHA SAHA MRS"
    [GeneratedRegex(@"(?<jsno>\d+\.\d+)(?<name>[A-Z][A-Z'-]*/[A-Z][A-Z\s'-]*?)(?=\s{2,}\d+\.\d+|\s*$)", RegexOptions.IgnoreCase)]
    private static partial Regex PassengerRegex();

    // Capture only the date portion before any slash or space+agent-sign in T- lines, e.g. "1.T-16FEB" from "1.T-16FEB-3A78*ATT"
    [GeneratedRegex(@"^\s*\d+\.T-(?<deadline>\d{1,2}[A-Z]{3}(?:\d{2,4})?)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex TicketingDeadlineRegex();

    [GeneratedRegex(@"\bBY\s+(?<deadline>\d{1,2}[A-Z]{3}\s+\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex AdtkDeadlineRegex();

    [GeneratedRegex(@"\b(?<currency>INR|USD|EUR|GBP|AED|AUD|CAD|CHF|CNY|DKK|HKD|JPY|MYR|NZD|SAR|SGD|THB|ZAR)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountingCurrencyRegex();

    private static readonly Regex s_accountingSlashFareRegex = new(
        @"\b(?<currency>INR|USD|EUR|GBP|AED|AUD|CAD|CHF|CNY|DKK|HKD|JPY|MYR|NZD|SAR|SGD|THB|ZAR)\s+(?<base>\d[\d,]*(?:\.\d+)?)\s*/\s*(?<tax>\d[\d,]*(?:\.\d+)?)\s*/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    [GeneratedRegex(@"\b(?:base\s+fare|base\s+amt|base|bf)\)?\D*(?<base>\d[\d,]*(?:\.\d+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountingBaseRegex();

    [GeneratedRegex(@"\b(?:taxes?|tax)\)?\D*(?<tax>\d[\d,]*(?:\.\d+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountingTaxRegex();

    [GeneratedRegex(@"\b(?:total\s+fare|total|grand\s+total)\)?\D*(?<total>\d[\d,]*(?:\.\d+)?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex AccountingTotalRegex();

    [GeneratedRegex(@"\b(?<seat>\d{1,2}[A-Z])\s*(?:\(seat\)|(?=\s+[NY]\s))", RegexOptions.IgnoreCase)]
    private static partial Regex SeatRegex();

    // Seat in boarding-pass section: "KK 17D N  1.1 NAME" — captures seat and jsno
    [GeneratedRegex(@"\bKK\s+(?<seat>\d{1,2}[A-Z])\s+[NY]\s+(?<jsno>\d+\.\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex SeatBoardingPassRegex();

    [GeneratedRegex(@"\bSSR\s+(?<meal>HNML|VGML|AVML|MOML|KSML|DBML|BLML|CHML|FPML|GFML|IFML|LCML|LSML|NLML|ORML|PRML|RVML|SFML|SPML|VLML|BBML|CNML|JPML|KSML|NSML|PFML|VOML)[A-Z0-9]*\b", RegexOptions.IgnoreCase)]
    private static partial Regex MealRegex();

    [GeneratedRegex(@"\b(?<email>[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,})\b", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();

    // Remark line: N./LABEL//DOMAIN.TLD  (label may contain letters, digits, dots, underscores, hyphens)
    private static readonly Regex s_remarkEmailRegex = new(
        @"^\s*\d+\./(?<local>[A-Z0-9._%-]+)//(?<domain>[A-Z0-9.-]+\.[A-Z]{2,})\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    // Matches the TKT/TIME LIMIT entry line(s), captures the value after the line number
    [GeneratedRegex(@"TKT/TIME LIMIT[\s\S]*?\d+\.(?<tkt>[^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex TktTimeLimitRegex();

    // Ticketed: has office ID + signing after TAW/ or T-DATE-, e.g. TAW/3A78*AWS or T-14JUL-3A78*AWS
    [GeneratedRegex(@"(?:TAW/|T-\d{1,2}[A-Z]{3}(?:\d{2,4})?-)[A-Z0-9]{3,5}\*[A-Z0-9]+", RegexOptions.IgnoreCase)]
    private static partial Regex TicketedTktRegex();

    [GeneratedRegex(@"ACCOUNTING DATA", RegexOptions.IgnoreCase)]
    private static partial Regex AccountingDataRegex();

    [GeneratedRegex(@"\b(?:PNR|RECORD\s+LOCATOR|LOCATOR|RECLOC)\s*[:#-]?\s*(?<pnr>[A-Z0-9*.-]{3,12})\b", RegexOptions.IgnoreCase)]
    private static partial Regex PnrHeaderRegex();

    [GeneratedRegex(@"<Response\b[\s\S]*?</Response>", RegexOptions.IgnoreCase)]
    private static partial Regex ResponseBlockRegex();

    [GeneratedRegex(@"^(?<pnr>[A-Z0-9*.-]{3,12})$", RegexOptions.IgnoreCase)]
    private static partial Regex BareLocatorRegex();

    [GeneratedRegex(@"^\s*RECEIVED\s+FROM\s*-\s*(?<receivedFrom>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ReceivedFromRegex();

    // Galileo header: GQHTZG/WS LONOU 6TP2GWS AG 91215600 28APR — captures "AG 91215600" as agentCode
    [GeneratedRegex(@"^\s*[A-Z0-9]{5,8}/[A-Z]{2}\s+[A-Z]{5}\s+[A-Z0-9]{4,8}\s+(?<agentCode>[A-Z]{2}\s+\d+)\s+\d{1,2}[A-Z]{3}", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GalileoReceivedFromRegex();

    // Galileo header date: last token ddMMM or ddMMMyy on the header line
    [GeneratedRegex(@"^\s*[A-Z0-9]{5,8}/[A-Z]{2}\s+[A-Z]{5}\s+[A-Z0-9]{4,8}\s+[A-Z]{2}\s+\d+\s+(?<date>\d{1,2}[A-Z]{3}(?:\d{2,4})?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GalileoReceivedDateRegex();

    // Galileo ticketed indicators: ** ELECTRONIC DATA EXISTS ** or ** TINS REMARKS EXIST **
    [GeneratedRegex(@"\*\*\s*(?:ELECTRONIC DATA EXISTS|TINS REMARKS EXIST)\s*\*\*", RegexOptions.IgnoreCase)]
    private static partial Regex GalileoTicketedRegex();

    // Sabre header: 3A78.3A78*ATT 0619/16FEB26 AFZPJI H  — PNR is the token after the datetime
    [GeneratedRegex(@"^\s*[A-Z0-9]{3,5}\.[A-Z0-9*]+\s+\d{4}/\d{2}[A-Z]{3}\d{2}\s+(?<pnr>[A-Z0-9]{5,8})\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SabreHeaderPnrRegex();

    // Sabre header: 3A78.3A78*ATT 0619/16FEB26 AFZPJI H — PCC must contain at least one letter and one digit
    // Galileo header: H9TX9T/AA LONOU 6TP2AA AG ... — PCC is the 4th token (e.g. 6TP2AA)
    [GeneratedRegex(@"^\s*(?<officeId>[A-Z0-9]{3,5})\.[A-Z0-9*]+\s+\d{4}/\d{2}[A-Z]{3}\d{2}\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex PCCRegex();

    [GeneratedRegex(@"^\s*[A-Z0-9]{5,8}/[A-Z]{2}\s+[A-Z]{5}\s+(?<officeId>[A-Z0-9]{4,8})\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GalileoPCCRegex();

    [GeneratedRegex(@"^\s*[A-Z0-9]{3,5}\.[^\r\n]*?\s(?<received>\d{4}/\d{2}[A-Z]{3}\d{2})\s+[A-Z0-9]{5,8}\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ReceivedDateTimeRegex();

    // Galileo header: G28S5G/WS LONOU 6TP2GWS AG ... — PNR is the first token before the slash
    [GeneratedRegex(@"^\s*(?<pnr>[A-Z0-9]{5,8})/[A-Z]{2}\s+[A-Z]{5}\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex GalileoHeaderPnrRegex();

    // Matches both Sabre (space-separated origin/dest) and Galileo (concatenated 6-char IATA pair)
    [GeneratedRegex(@"^\s*(?<segment>\d{1,2})\.?\s+(?<carrier>[A-Z0-9]{2})\s*(?<flightNumber>\d{1,4}[A-Z]?)\s+(?:[A-Z]\s+)?(?<date>[0-9]{1,2}[A-Z]{3}|[A-Z]{3}\s*[0-9]{1,2})?\s*(?:[A-Z]\s+)?(?:(?<origin>[A-Z]{3})\s+(?<destination>[A-Z]{3})|(?<origin6>[A-Z]{3})(?<destination6>[A-Z]{3}))\*?\s*(?<status>HK|KK|KL|TK|HX|UN|UC|US|WL|NO)\d*\b(?<times>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SegmentLineRegex();

    [GeneratedRegex(@"\b\d{3,4}[AP]?\b", RegexOptions.IgnoreCase)]
    private static partial Regex TimeRegex();

    [GeneratedRegex(@"\bPREV\s+TIME\s+FOR\s+(?<flight>[A-Z0-9]{2}\s*\d{1,4}[A-Z]?)\s+\S+\s+\S+\s+(?<dep>\d{3,4}[AP]?)\s+(?<arr>\d{3,4}[AP]?)\b", RegexOptions.IgnoreCase)]
    private static partial Regex PreviousTimeRegex();

    [GeneratedRegex(@"^(?<flight>[A-Z0-9]{2}\d{1,4}[A-Z]?)\s+[A-Z]{3}\s+[A-Z]{3}$", RegexOptions.IgnoreCase)]
    private static partial Regex ScheduleFlightRegex();

    [GeneratedRegex(@"^OLD:\s*(?<dep>\d{3,4})\s+(?<arr>\d{3,4})$", RegexOptions.IgnoreCase)]
    private static partial Regex OldTimesRegex();

    [GeneratedRegex(@"^NEW:\s*(?<dep>\d{3,4})\s+(?<arr>\d{3,4})$", RegexOptions.IgnoreCase)]
    private static partial Regex NewTimesRegex();

    private sealed record ScheduleChangeTimes(
        string OldDeparture,
        string OldArrival,
        string? NewDeparture,
        string? NewArrival);

    private static decimal? TryParseAmount(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace(",", string.Empty).Trim();
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount)
            ? amount
            : null;
    }

    private static VendorLocatorInfo? ParseVendorLocator(string block)
    {
        var match = VlocRegex().Match(block);
        if (!match.Success)
            return null;

        var locator = match.Groups["locator"].Value.Trim().ToUpperInvariant();
        var deadline = match.Groups["deadline"].Success && !string.IsNullOrWhiteSpace(match.Groups["deadline"].Value)
            ? match.Groups["deadline"].Value.Trim()
            : null;
        return new VendorLocatorInfo(locator, deadline);
    }

    private static IReadOnlyList<VendorRemarkAction>? ParseVendorRemarks(string block)
    {
        var vrStart = block.IndexOf("*VR", StringComparison.OrdinalIgnoreCase);
        if (vrStart < 0)
            return null;

        var vrBlock = block[(vrStart + 3)..];
        var actions = new List<VendorRemarkAction>();

        foreach (var rawLine in vrBlock.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = VrLinePrefixRegex().Replace(rawLine.Trim(), string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (MissingSsrRegex().IsMatch(line))
            {
                actions.Add(new VendorRemarkAction(VendorRemarkType.MissingSsr, line));
                continue;
            }

            var dupMatch = DuplicatePnrRegex().Match(line);
            if (dupMatch.Success)
            {
                var deadline = dupMatch.Groups["deadline"].Success && !string.IsNullOrWhiteSpace(dupMatch.Groups["deadline"].Value)
                    ? dupMatch.Groups["deadline"].Value.Trim() : null;
                var dupLocator = dupMatch.Groups["locator"].Success && !string.IsNullOrWhiteSpace(dupMatch.Groups["locator"].Value)
                    ? dupMatch.Groups["locator"].Value.Trim().ToUpperInvariant() : null;
                var type = line.Contains("AUTO", StringComparison.OrdinalIgnoreCase) && line.Contains("CANCEL", StringComparison.OrdinalIgnoreCase)
                    ? VendorRemarkType.AutoCancelWarning
                    : VendorRemarkType.DuplicatePnr;
                actions.Add(new VendorRemarkAction(type, line, deadline, dupLocator));
                continue;
            }

            var scMatch = VrScheduleChangeRegex().Match(line);
            if (scMatch.Success)
            {
                actions.Add(new VendorRemarkAction(VendorRemarkType.ScheduleChange, line,
                    Flight: scMatch.Groups["flight"].Value.Trim().ToUpperInvariant()));
                continue;
            }
        }

        return actions.Count > 0 ? actions : null;
    }

    // VLOC-1A*XFR62W/03APR 1245  or  VLOC-1A*XFR62W
    [GeneratedRegex(@"VLOC-[^*]*\*(?<locator>[A-Z0-9]{5,8})(?:[/\s]+(?<deadline>[^\r\n]+))?", RegexOptions.IgnoreCase)]
    private static partial Regex VlocRegex();

    // Strip leading "1. VI/ASV *" or "VRMK-VI/ASV *" or "2." style prefixes from VR lines
    [GeneratedRegex(@"^(?:\d+\.\s*)?(?:VRMK-)?(?:[A-Z]{2}/[A-Z]+\s*\*)?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex VrLinePrefixRegex();

    // MISSING SSR CTCM / CTCE / CTCR / NON-CONSENT
    [GeneratedRegex(@"MISSING\s+SSR\s+(?:CTCM|CTCE|CTCR|NON.CONSENT)", RegexOptions.IgnoreCase)]
    private static partial Regex MissingSsrRegex();

    // DUPLICATE OF XDJSNK or CLEAR DUPLICATES BY LON 0148/04APR26
    [GeneratedRegex(@"(?:DUPLICATE\s+OF\s+(?<locator>[A-Z0-9]{5,8})|CLEAR\s+DUPLICATES)(?:.*?(?:BY|LON)\s+(?<deadline>[\d]{4}/[\d]{2}[A-Z]{3}[\d]{0,4}|[A-Z]{3}\s+[\d]{4}/[\d]{2}[A-Z]{3}[\d]{0,4}))?", RegexOptions.IgnoreCase)]
    private static partial Regex DuplicatePnrRegex();

    // SV722 SCHEDULE CHANGE DUE TO OPERATIONAL REASON
    [GeneratedRegex(@"(?<flight>[A-Z]{2}\d{1,4})\s+SCHEDULE\s+CHANGE", RegexOptions.IgnoreCase)]
    private static partial Regex VrScheduleChangeRegex();
}
