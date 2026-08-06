using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public class AdmAnalysisService : IAdmAnalysisService
{
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

    // Matches: TKT AMT - INR       110272DC  (strips trailing alpha suffix like DC/CA)
    private static readonly System.Text.RegularExpressions.Regex s_tktAmtRegex = new(
        @"TKT AMT\s*-\s*(?<currency>[A-Z]{3})\s+(?<amount>[\d]+)",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Matches ticket number line: e.g.   2323929960020       IN              ETR
    private static readonly System.Text.RegularExpressions.Regex s_ticketNoRegex = new(
        @"^\s+(?<tktno>\d{10,13})\s+",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private static IReadOnlyList<SalesAuditEntry> ParseSalesAuditReport(string report, string agencyPcc)
    {
        var entries = new List<SalesAuditEntry>();
        var lines = report.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < lines.Length; i++)
        {
            var pnrMatch = s_pnrLineRegex.Match(lines[i]);
            if (!pnrMatch.Success) continue;

            var pnr = pnrMatch.Groups["pnr"].Value.ToUpperInvariant();
            var agent = pnrMatch.Groups["agent"].Value;
            var time = pnrMatch.Groups["time"].Value;

            var ticketNo = string.Empty;
            var amount = 0m;

            // Scan next 3 lines for TKT AMT and ticket number
            for (var j = i + 1; j < Math.Min(i + 4, lines.Length); j++)
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

        // Collect all entries across all PCCs, deduplicate by PNR (last-write-wins per PCC order)
        var pnrMap = new Dictionary<string, SalesAuditEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var officeId in officeIds)
        {
            _logger.LogInformation("Reading DQB* for PCC: {OfficeId}", officeId);

            var pages = await _sabreCommandService.ExecutePagedHostCommandAsync(
                officeId, "DQB*", "DQB*MD", "END OF REPORT", maxPages: 50, cancellationToken);

            foreach (var entry in ParseSalesAuditReport(string.Join("\n", pages), officeId))
                pnrMap[entry.Pnr] = entry;
        }

        _logger.LogInformation("Unique PNRs extracted: {Count}", pnrMap.Count);

        // Save all unique PNRs to adm_sales_audit (INSERT IGNORE — skips duplicates)
        foreach (var entry in pnrMap.Values)
            await _repository.SaveSalesAuditAsync(entry, cancellationToken);

        // Loop every PNR: open in Sabre, run ADM rules, save results
        foreach (var (pnr, entry) in pnrMap)
        {
            try
            {
                var pnrText = await _sabreCommandService.ExecuteHostCommandAsync(entry.AgencyPcc, $"*{pnr}", cancellationToken);
                var hiText = await _sabreCommandService.ExecuteHostCommandAsync(entry.AgencyPcc, "*HI", cancellationToken);
                var analysis = await RunRulesAsync(pnr, pnrText, hiText, cancellationToken);
                await _repository.SaveAdmAnalysisAsync(analysis, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed processing PNR {Pnr}", pnr);
            }
        }

        _logger.LogInformation("ADM analysis run completed. Total PNRs processed: {Count}", pnrMap.Count);
    }

    private async Task<AdmAnalysisDto> RunRulesAsync(string pnr, string pnrText, string hiText, CancellationToken cancellationToken)
    {
        var marketMap = await _repository.GetPccMarketMapAsync(cancellationToken);

        // Rule 1 — Cross Border
        var ticketPcc = ExtractTicketPcc(pnrText);
        var bookingPcc = ExtractBookingPcc(pnrText, hiText);
        var ticketMarket = marketMap.TryGetValue(ticketPcc, out var tm) ? tm : string.Empty;
        var bookingMarket = marketMap.TryGetValue(bookingPcc, out var bm) ? bm : string.Empty;
        var isCrossBorder = !string.IsNullOrWhiteSpace(ticketMarket)
            && !string.IsNullOrWhiteSpace(bookingMarket)
            && !string.Equals(ticketMarket, bookingMarket, StringComparison.OrdinalIgnoreCase);

        var risk = 0;
        if (isCrossBorder) risk += 40;

        // Rule 2 — Changed Segment
        var segments = ExtractSegmentKeys(hiText);
        var duplicates = segments.GroupBy(s => s).Where(g => g.Count() > 1).ToList();
        var changedCount = duplicates.Sum(g => g.Count());
        var isChanged = duplicates.Any();
        if (isChanged) risk += 30;

        // Rule 3 — Married Segment
        var signatures = ExtractItinerarySignatures(hiText);
        var uniqueGroups = signatures.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var isMarried = uniqueGroups > 2;
        if (isMarried) risk += 30;

        return new AdmAnalysisDto
        {
            Pnr = pnr,
            TicketPcc = ticketPcc,
            BookingPcc = bookingPcc,
            TicketMarket = ticketMarket,
            BookingMarket = bookingMarket,
            IsCrossBorder = isCrossBorder,
            ChangedSegmentCount = changedCount,
            IsChangedSegment = isChanged,
            MarriedSegmentCount = uniqueGroups,
            IsMarriedSegment = isMarried,
            RiskScore = risk,
            Remarks = BuildRemarks(isCrossBorder, isChanged, isMarried, ticketMarket, bookingMarket),
            AnalyzedAt = DateTime.UtcNow
        };
    }

    private static string BuildRemarks(bool crossBorder, bool changed, bool married, string tktMkt, string bkgMkt)
    {
        var parts = new List<string>();
        if (crossBorder) parts.Add($"CrossBorder:{tktMkt}->{bkgMkt}");
        if (changed) parts.Add("ChangedSeg");
        if (married) parts.Add("MarriedSeg");
        return string.Join("; ", parts);
    }

    // Rule 1 helpers
    // T-06AUG-3A78*AWS  or  3A78.3A78*AWS  — PCC is exactly 4 chars (Sabre PCC format)
    private static readonly System.Text.RegularExpressions.Regex s_ticketPccRegex = new(
        @"T-\d{2}[A-Z]{3}-([A-Z0-9]{4})\*|^([A-Z0-9]{4})\.([A-Z0-9]{4})\*",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string ExtractTicketPcc(string text)
    {
        foreach (System.Text.RegularExpressions.Match m in s_ticketPccRegex.Matches(text))
        {
            var pcc = m.Groups[1].Success ? m.Groups[1].Value
                    : m.Groups[2].Success ? m.Groups[2].Value
                    : string.Empty;
            if (!string.IsNullOrWhiteSpace(pcc)) return pcc.ToUpperInvariant();
        }
        return string.Empty;
    }

    private static string ExtractBookingPcc(string pnrText, string hiText)
    {
        // Priority order: creation history AAA line, RECEIVED FROM, AG line in *HI
        var combined = pnrText + "\n" + hiText;
        var patterns = new[]
        {
            @"\bAAA\s+([A-Z0-9]{4})\b",                          // AAA 3A78
            @"RECEIVED\s+FROM\s+([A-Z0-9]{4})\b",               // RECEIVED FROM 3A78
            @"CREATED\s+(?:IN|BY)\s+([A-Z0-9]{4})\b",           // CREATED IN 3A78
            @"^\s*([A-Z0-9]{4})\s+AG\s",                        // 3A78 AG ...
            @"^\s*([A-Z0-9]{4})\.[A-Z0-9]{4}\*",               // 3A78.3A78* (booking PCC)
        };
        foreach (var pattern in patterns)
        {
            var m = System.Text.RegularExpressions.Regex.Match(combined, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline);
            if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        }
        return string.Empty;
    }

    // Rule 2 — segment key includes flight, date, origin, destination
    // *HI line format: AS AI1818 06AUG SXRDEL  or  AS AI1818 06AUG SXR DEL
    private static readonly System.Text.RegularExpressions.Regex s_hiSegmentRegex = new(
        @"^\s*AS\s+([A-Z0-9]{2}\d{1,4})\s+(\d{2}[A-Z]{3})\s+([A-Z]{3})([A-Z]{3})",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    // Fallback: no date in line — AS AI1818 SXRDEL
    private static readonly System.Text.RegularExpressions.Regex s_hiSegmentNoDtRegex = new(
        @"^\s*AS\s+([A-Z0-9]{2}\d{1,4})\s+([A-Z]{3})([A-Z]{3})",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static List<string> ExtractSegmentKeys(string text)
    {
        var list = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in s_hiSegmentRegex.Matches(text))
            list.Add($"{m.Groups[1].Value.ToUpperInvariant()}:{m.Groups[2].Value.ToUpperInvariant()}:{m.Groups[3].Value.ToUpperInvariant()}-{m.Groups[4].Value.ToUpperInvariant()}");

        // If no date-keyed matches, fall back to no-date format
        if (list.Count == 0)
            foreach (System.Text.RegularExpressions.Match m in s_hiSegmentNoDtRegex.Matches(text))
                list.Add($"{m.Groups[1].Value.ToUpperInvariant()}:{m.Groups[2].Value.ToUpperInvariant()}-{m.Groups[3].Value.ToUpperInvariant()}");

        return list;
    }

    // Rule 3 — group contiguous AS-lines into itinerary sets separated by non-AS lines
    private static List<string> ExtractItinerarySignatures(string text)
    {
        var sets = new List<string>();
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var current = new List<string>();

        foreach (var line in lines)
        {
            var m = s_hiSegmentRegex.Match(line);
            if (!m.Success) m = s_hiSegmentNoDtRegex.Match(line);

            if (m.Success)
            {
                // Use flight+origin+dest (no date) as the segment identity within a set
                var seg = m.Groups.Count > 4
                    ? $"{m.Groups[1].Value.ToUpperInvariant()}:{m.Groups[3].Value.ToUpperInvariant()}-{m.Groups[4].Value.ToUpperInvariant()}"
                    : $"{m.Groups[1].Value.ToUpperInvariant()}:{m.Groups[2].Value.ToUpperInvariant()}-{m.Groups[3].Value.ToUpperInvariant()}";
                current.Add(seg);
            }
            else if (current.Count > 0)
            {
                sets.Add(string.Join("|", current));
                current.Clear();
            }
        }
        if (current.Count > 0) sets.Add(string.Join("|", current));
        return sets;
    }

    public Task<IReadOnlyList<AdmAnalysisDto>> GetAllAsync(CancellationToken cancellationToken = default)
        => _repository.GetAllAsync(cancellationToken);

    public Task<AdmAnalysisDto?> GetByPnrAsync(string pnr, CancellationToken cancellationToken = default)
        => _repository.GetByPnrAsync(pnr, cancellationToken);

    public Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
        => _repository.GetDashboardAsync(cancellationToken);
}
