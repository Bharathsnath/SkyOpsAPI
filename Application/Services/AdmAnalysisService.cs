using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public class AdmAnalysisService : IAdmAnalysisService
{
    private static readonly bool IncludeMarriedSegmentRule = false;

    private readonly IAdmAnalysisRepository _repository;
    private readonly ISabreCommandService _sabreCommandService;
    private readonly ICredentialStore _credentialStore;
    private readonly ILogger<AdmAnalysisService> _logger;

    public AdmAnalysisService(IAdmAnalysisRepository repository, ISabreCommandService sabreCommandService, ICredentialStore credentialStore, ILogger<AdmAnalysisService> logger)
    {
        _repository = repository;
        _sabreCommandService = sabreCommandService;
        _credentialStore = credentialStore;
        _logger = logger;
    }

    // Matches: PNR-DLVRME ARORA/CHANDRA MOHAN MR   AGT SINE-ATT TIME 1430
    private static readonly System.Text.RegularExpressions.Regex s_pnrLineRegex = new(
        @"PNR-(?<pnr>[A-Z0-9]{6})\s+(?<name>.+?)\s+AGT SINE-(?<agent>[A-Z0-9]+)\s+TIME\s+(?<time>\d{4})",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Fallback: PNR-ICAAKS DEVASIA/LINTA MS                 ETR  (no AGT SINE / TIME)
    private static readonly System.Text.RegularExpressions.Regex s_pnrLineShortRegex = new(
        @"PNR-(?<pnr>[A-Z0-9]{6})\s+(?<name>.+?)\s+ETR\s*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches: TKT AMT - INR       110272DC  (strips trailing alpha suffix like DC/CA)
    private static readonly System.Text.RegularExpressions.Regex s_tktAmtRegex = new(
        @"TKT AMT\s*-\s*(?<currency>[A-Z]{3})\s+(?<amount>[\d]+)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches ticket number line: e.g.   2323929960020       IN              ETR
    private static readonly System.Text.RegularExpressions.Regex s_ticketNoRegex = new(
        @"^\s*(?<tktno>\d{10,13})\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static IReadOnlyList<SalesAuditEntry> ParseSalesAuditReport(string report, string agencyPcc)
    {
        var entries = new List<SalesAuditEntry>();
        var lines = report.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < lines.Length; i++)
        {
            var pnrMatch = s_pnrLineRegex.Match(lines[i]);
            if (!pnrMatch.Success) pnrMatch = s_pnrLineShortRegex.Match(lines[i]);
            if (!pnrMatch.Success) continue;

            var pnr = pnrMatch.Groups["pnr"].Value.ToUpperInvariant();
            var agent = pnrMatch.Groups["agent"].Success ? pnrMatch.Groups["agent"].Value : string.Empty;
            var time = pnrMatch.Groups["time"].Success ? pnrMatch.Groups["time"].Value : string.Empty;

            var ticketNo = string.Empty;
            var amount = 0m;

            // Scan a wider window for TKT AMT and ticket number
            for (var j = i + 1; j < Math.Min(i + 8, lines.Length); j++)
            {
                if (string.IsNullOrWhiteSpace(ticketNo))
                {
                    var tnoMatch = s_ticketNoRegex.Match(lines[j]);
                    if (tnoMatch.Success) ticketNo = tnoMatch.Groups["tktno"].Value;
                }
                if (amount == 0m)
                {
                    var amtMatch = s_tktAmtRegex.Match(lines[j]);
                    if (amtMatch.Success && decimal.TryParse(amtMatch.Groups["amount"].Value, out var parsed))
                        amount = parsed;
                }
            }

            entries.Add(new SalesAuditEntry(pnr, ticketNo, amount, DateTime.UtcNow, agencyPcc, agent, time));
        }

        return entries;
    }

    // Matches: RECEIVED FROM - AO261530620
    private static readonly System.Text.RegularExpressions.Regex s_receivedFromRegex = new(
        @"RECEIVED\s+FROM\s*-\s*([A-Z0-9]+)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string? ExtractTransactionId(string pnrText)
    {
        var m = s_receivedFromRegex.Match(pnrText);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }

    // Matches country code from W/* response: "-5YW8  AQUA TRAVEL SERVICES\n      MUMBAI, IN"
    private static readonly System.Text.RegularExpressions.Regex s_pccCountryRegex = new(
        @",\s*([A-Z]{2})\s*$",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private async Task<string> FetchPccMarketAsync(string officeId, string pcc, Dictionary<string, string> cache, CancellationToken ct)
    {
        if (cache.TryGetValue(pcc, out var cached)) return cached;
        try
        {
            var responses = await _sabreCommandService.ExecuteSequentialCommandsAsync(
                officeId, [$"W/*{pcc}"], ct,
                moduleName: "SabreADMAnalysis", moduleCode: "ADM");
            var text = string.Join("\n", responses);
            var match = s_pccCountryRegex.Match(text);
            var market = match.Success ? match.Groups[1].Value.ToUpperInvariant() : string.Empty;
            cache[pcc] = market;
            return market;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "W/*{Pcc} lookup failed", pcc);
            cache[pcc] = string.Empty;
            return string.Empty;
        }
    }

    public async Task RunAnalysisAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting ADM analysis run.");

        var officeIds = _credentialStore.GetAll()
            .Where(c => c.TagName.Equals("SourceOffice", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.TagValue))
            .Select(c => c.TagValue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (officeIds.Count == 0)
        {
            _logger.LogWarning("No SourceOffice credentials found; aborting ADM run.");
            return;
        }

        var pnrMap = new Dictionary<string, SalesAuditEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var officeId in officeIds)
        {
            _logger.LogInformation("Reading DQB* for PCC: {OfficeId}", officeId);
            var pages = await _sabreCommandService.ExecutePagedHostCommandAsync(
                officeId, "DQB*", "DQB*MD", "END OF REPORT", maxPages: 50, cancellationToken,
                moduleName: "SabreADMAnalysis", moduleCode: "ADM");
            foreach (var entry in ParseSalesAuditReport(string.Join("\n", pages), officeId))
                pnrMap[entry.Pnr] = entry;
        }

        _logger.LogInformation("Unique PNRs extracted: {Count}", pnrMap.Count);

        var salesAuditIds = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in pnrMap.Values)
        {
            var salesAuditId = await _repository.SaveSalesAuditAsync(entry, cancellationToken);
            if (salesAuditId > 0) salesAuditIds[entry.Pnr] = salesAuditId;
        }

        // Per-run PCC market cache to avoid duplicate W/* calls
        var marketCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (pnr, entry) in pnrMap)
        {
            try
            {
                var commands = new List<string> { $"*{pnr}" };
                if (IncludeMarriedSegmentRule)
                    commands.Add("*HI");

                var responses = await _sabreCommandService.ExecuteSequentialCommandsAsync(
                    entry.AgencyPcc, commands, cancellationToken,
                    moduleName: "SabreADMAnalysis", moduleCode: "ADM", pnr: pnr);
                var pnrText = responses[0];
                var hiText = IncludeMarriedSegmentRule && responses.Count > 1 ? responses[1] : string.Empty;
                var transactionId = ExtractTransactionId(pnrText);
                var analysis = await RunRulesAsync(pnr, entry.TicketNumber, pnrText, hiText, entry.AgencyPcc, marketCache, cancellationToken);
                if (salesAuditIds.TryGetValue(pnr, out var salesAuditId))
                    analysis = analysis with { SalesAuditId = salesAuditId };
                analysis = analysis with { TransactionId = transactionId };
                await _repository.SaveAdmAnalysisAsync(analysis, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed processing PNR {Pnr}", pnr);
            }
        }

        _logger.LogInformation("ADM analysis run completed. Total PNRs processed: {Count}", pnrMap.Count);
    }

    // Matches PNR locator from a queue item header: e.g. ODLOCF or 1V08.1V08*AWC 1053/08AUG26 ODLOCF H
    private static readonly System.Text.RegularExpressions.Regex s_queuePnrRegex = new(
        @"^(?:[A-Z0-9]{6}\s*$|[A-Z0-9]{4}[.\s]+[A-Z0-9]{4}\*[A-Z0-9]+\s+\d{1,4}/\d{2}[A-Z]{3}\d{2}\s+(?<pnr>[A-Z0-9]{6})\b)",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task RunQueue379ChurnScanAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Queue 379 churn scan.");

        var officeIds = _credentialStore.GetAll()
            .Where(c => c.TagName.Equals("SourceOffice", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.TagValue))
            .Select(c => c.TagValue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (officeIds.Count == 0)
        {
            _logger.LogWarning("No SourceOffice credentials found; aborting Queue 379 churn scan.");
            return;
        }

        foreach (var officeId in officeIds)
        {
            _logger.LogInformation("Reading Q/0 for PCC: {OfficeId}", officeId);

            var pages = await _sabreCommandService.ExecutePagedHostCommandAsync(
                officeId, "Q/0", "I", "QUEUE EMPTY", maxPages: 500, cancellationToken,
                moduleName: "SabreADMAnalysis", moduleCode: "ADM");

            var combinedText = string.Join("\n", pages);

            // Extract 6-char PNR locators from queue items
            var pnrs = s_queuePnrRegex.Matches(combinedText)
                .Select(m => m.Groups["pnr"].Success ? m.Groups["pnr"].Value : m.Groups[0].Value.Trim())
                .Select(pnr => pnr.ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation("Queue 0 PNRs found for {OfficeId}: {Count}", officeId, pnrs.Count);

            foreach (var pnr in pnrs)
            {
                try
                {
                    var responses = await _sabreCommandService.ExecuteSequentialCommandsAsync(
                        officeId, [$"*{pnr}", "*HI"], cancellationToken,
                        moduleName: "SabreADMAnalysis", moduleCode: "ADM", pnr: pnr);

                    var pnrText = responses[0];
                    var hiText = responses[1];

                    var segments = ExtractSegmentKeys(hiText);
                    var duplicates = segments.GroupBy(s => s).Where(g => g.Count() > 2).ToList();
                    if (!duplicates.Any()) continue;

                    var analysis = new AdmAnalysisDto
                    {
                        Pnr = pnr,
                        IsChurnedSegment = true,
                        ChurnedSegmentCount = duplicates.Count,
                        RiskScore = 30,
                        Remarks = "ChurnedSeg",
                        TransactionId = ExtractTransactionId(pnrText),
                        AnalyzedAt = DateTime.UtcNow
                    };
                    await _repository.SaveAdmAnalysisAsync(analysis, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Queue 0 churn scan failed for PNR {Pnr}", pnr);
                }
            }
        }

        _logger.LogInformation("Queue 0 churn scan completed.");
    }

    private async Task<AdmAnalysisDto> RunRulesAsync(string pnr, string? ticketNo, string pnrText, string hiText, string officeId, Dictionary<string, string> marketCache, CancellationToken cancellationToken)
    {
        // Rule 1 — Cross Border
        var ticketPcc = ExtractTicketPcc(pnrText);
        var bookingPcc = ExtractBookingPcc(pnrText, hiText);
        var ticketMarket = string.IsNullOrWhiteSpace(ticketPcc) ? string.Empty
            : await FetchPccMarketAsync(officeId, ticketPcc, marketCache, cancellationToken);
        var bookingMarket = string.IsNullOrWhiteSpace(bookingPcc) ? string.Empty
            : string.Equals(bookingPcc, ticketPcc, StringComparison.OrdinalIgnoreCase) ? ticketMarket
            : await FetchPccMarketAsync(officeId, bookingPcc, marketCache, cancellationToken);
        var isCrossBorder = !string.IsNullOrWhiteSpace(ticketMarket)
            && !string.IsNullOrWhiteSpace(bookingMarket)
            && !string.Equals(ticketMarket, bookingMarket, StringComparison.OrdinalIgnoreCase);

        var risk = 0;
        if (isCrossBorder) risk += 40;

        var signatures = IncludeMarriedSegmentRule ? ExtractItinerarySignatures(hiText) : [];
        var isMarried = IncludeMarriedSegmentRule && signatures.Any(sig => sig.Split('|').Length > 1);
        var uniqueItineraryGroups = IncludeMarriedSegmentRule ? signatures.Distinct().Count() : 0;
        if (isMarried) risk += 30;

        return new AdmAnalysisDto
        {
            Pnr = pnr,
            TicketNo = ticketNo,
            TicketPcc = ticketPcc,
            BookingPcc = bookingPcc,
            TicketMarket = ticketMarket,
            BookingMarket = bookingMarket,
            IsCrossBorder = isCrossBorder,
            MarriedSegmentCount = uniqueItineraryGroups,
            IsMarriedSegment = isMarried,
            RiskScore = risk,
            Remarks = BuildRemarks(isCrossBorder, false, isMarried, ticketMarket, bookingMarket),
            AnalyzedAt = DateTime.UtcNow
        };
    }

    private static string BuildRemarks(bool crossBorder, bool changed, bool married, string tktMkt, string bkgMkt)
    {
        var parts = new List<string>();
        if (crossBorder) parts.Add($"CrossBorder:{tktMkt}->{bkgMkt}");
        if (changed) parts.Add("ChurnedSeg");
        if (married) parts.Add("MarriedSeg");
        return string.Join("; ", parts);
    }

    // Rule 1 helpers
    // Matches: T-06AUG-3A78*AWS  — ticketed PCC only
    private static readonly System.Text.RegularExpressions.Regex s_ticketPccRegex = new(
        @"T-\d{2}[A-Z]{3}-([A-Z0-9]{4})\*",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string ExtractTicketPcc(string text)
    {
        var m = s_ticketPccRegex.Match(text);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : string.Empty;
    }

    private static string ExtractBookingPcc(string pnrText, string hiText)
    {
        var combined = pnrText + "\n" + hiText;

        // Sabre footer line: XXXX.XXXX*AGENT — first 4 chars is the booking (AAA) PCC
        // e.g. 3A78.3A78*AWS  or  8FR2.3A78*AWS
        var footerMatch = System.Text.RegularExpressions.Regex.Match(combined,
            @"^([A-Z0-9]{4})\.[A-Z0-9]{4}\*",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
        if (footerMatch.Success) return footerMatch.Groups[1].Value.ToUpperInvariant();

        var patterns = new[]
        {
            @"\bAAA\s+([A-Z0-9]{4})\b",
            @"\bCREATED\s+(?:IN|BY)\s+([A-Z0-9]{4})\b",
            @"\bBOOKING\s+PCC\b[^\n\r]{0,20}([A-Z0-9]{4})\b",
            @"\bORIGINAL\s+BOOKING\b[^\n\r]{0,20}([A-Z0-9]{4})\b",
            @"^\s*([A-Z0-9]{4})\s+AG\s"
        };
        foreach (var pattern in patterns)
        {
            var m = System.Text.RegularExpressions.Regex.Match(combined, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        }

        return string.Empty;
    }

    // Rule 2 — segment key: only AS lines with HK status (real bookings, not NN/TK/SS attempts)
    // Format: AS   VJ1806Z 03AUG AMDSGN SS/HK3  or  AS   GF 65O 25AUG BOMBAH*HK1
    // Status delimiter can be * (e.g. BOMBAH*HK1) or SS/ (e.g. AMDSGN SS/HK3)
    private static readonly System.Text.RegularExpressions.Regex s_hiSegmentRegex = new(
        @"^\s*AS\s+([A-Z]{2})\s*(\d{1,4}[A-Z]?)\s+(\d{2}[A-Z]{3})\s+([A-Z]{3})([A-Z]{3})[^\n]*?[*/]HK\d",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Fallback: no date — AS VJ1806Z AMDSGN SS/HK3
    private static readonly System.Text.RegularExpressions.Regex s_hiSegmentNoDtRegex = new(
        @"^\s*AS\s+([A-Z]{2})\s*(\d{1,4}[A-Z]?)\s+([A-Z]{3})([A-Z]{3})[^\n]*?[*/]HK\d",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static List<string> ExtractSegmentKeys(string text)
    {
        var list = new List<string>();
        // Groups: 1=carrier, 2=flightNum, 3=date, 4=origin, 5=dest
        foreach (System.Text.RegularExpressions.Match m in s_hiSegmentRegex.Matches(text))
            list.Add($"{m.Groups[1].Value.ToUpperInvariant()}{m.Groups[2].Value.ToUpperInvariant()}:{m.Groups[3].Value.ToUpperInvariant()}:{m.Groups[4].Value.ToUpperInvariant()}-{m.Groups[5].Value.ToUpperInvariant()}");

        // If no date-keyed matches, fall back to no-date format
        // Groups: 1=carrier, 2=flightNum, 3=origin, 4=dest
        if (list.Count == 0)
            foreach (System.Text.RegularExpressions.Match m in s_hiSegmentNoDtRegex.Matches(text))
                list.Add($"{m.Groups[1].Value.ToUpperInvariant()}{m.Groups[2].Value.ToUpperInvariant()}:{m.Groups[3].Value.ToUpperInvariant()}-{m.Groups[4].Value.ToUpperInvariant()}");

        return list;
    }

    // Rule 3 — a married segment exists when a single R- block contains 2+ distinct AS flights
    // (i.e. a connecting itinerary that must be priced/ticketed together)
    // Each R- block in *HI represents one history entry; collect AS lines per block.
    private static readonly System.Text.RegularExpressions.Regex s_rBlockRegex = new(
        @"^R-",
        System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static List<string> ExtractItinerarySignatures(string text)
    {
        var sets = new List<string>();
        var blocks = s_rBlockRegex.Split(text);

        foreach (var block in blocks)
        {
            var blockLines = block.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var segs = new List<(string Key, string Date, string Origin, string Dest)>();
            foreach (var line in blockLines)
            {
                var m = s_hiSegmentRegex.Match(line);
                if (!m.Success) m = s_hiSegmentNoDtRegex.Match(line);
                if (!m.Success) continue;

                var hasDate = m.Groups.Count > 5 && m.Groups[5].Success;
                string key, date, orig, dest;
                if (hasDate)
                {
                    key  = $"{m.Groups[1].Value.ToUpperInvariant()}{m.Groups[2].Value.ToUpperInvariant()}:{m.Groups[4].Value.ToUpperInvariant()}-{m.Groups[5].Value.ToUpperInvariant()}";
                    date = m.Groups[3].Value.ToUpperInvariant();
                    orig = m.Groups[4].Value.ToUpperInvariant();
                    dest = m.Groups[5].Value.ToUpperInvariant();
                }
                else
                {
                    key  = $"{m.Groups[1].Value.ToUpperInvariant()}{m.Groups[2].Value.ToUpperInvariant()}:{m.Groups[3].Value.ToUpperInvariant()}-{m.Groups[4].Value.ToUpperInvariant()}";
                    date = string.Empty;
                    orig = m.Groups[3].Value.ToUpperInvariant();
                    dest = m.Groups[4].Value.ToUpperInvariant();
                }

                if (!segs.Any(s => s.Key == key)) segs.Add((key, date, orig, dest));
            }

            if (segs.Count < 2) continue;

            // Only flag as married if legs are truly connecting:
            // same date OR dest of leg N == origin of leg N+1
            var isConnecting = false;
            for (var i = 0; i < segs.Count - 1; i++)
            {
                var sameDate = !string.IsNullOrEmpty(segs[i].Date) && segs[i].Date == segs[i + 1].Date;
                var connecting = segs[i].Dest == segs[i + 1].Origin;
                if (sameDate || connecting) { isConnecting = true; break; }
            }

            if (isConnecting)
                sets.Add(string.Join("|", segs.Select(s => s.Key)));
        }
        return sets;
    }

    public Task<IReadOnlyList<AdmAnalysisDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => _repository.GetAllAsync(cancellationToken);

    public Task<AdmAnalysisDto?> GetByPnrAsync(string pnr, CancellationToken cancellationToken = default)
        => _repository.GetByPnrAsync(pnr, cancellationToken);

    public Task<DashboardDto> GetDashboardAsync(int userId, CancellationToken cancellationToken = default)
        => _repository.GetDashboardAsync(userId, cancellationToken);

    public Task<AdmDashboardDto> GetAdmDashboardAsync(CancellationToken cancellationToken = default)
        => _repository.GetAdmDashboardAsync(cancellationToken);
}
