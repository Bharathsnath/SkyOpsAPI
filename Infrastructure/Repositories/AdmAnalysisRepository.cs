using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public sealed class AdmAnalysisRepository : IAdmAnalysisRepository
{
    private readonly IConfiguration _configuration;
    private readonly IConnectionCredentialStore _connectionCredentialStore;
    private readonly ICredentialStore _credentialStore;
    private readonly ILogger<AdmAnalysisRepository> _logger;
    private string? _connectionString;
    private string? _logConnectionString;

    public AdmAnalysisRepository(
        IConfiguration configuration,
        IConnectionCredentialStore connectionCredentialStore,
        ICredentialStore credentialStore,
        ILogger<AdmAnalysisRepository> logger)
    {
        _configuration = configuration;
        _connectionCredentialStore = connectionCredentialStore;
        _credentialStore = credentialStore;
        _logger = logger;
        RefreshConnectionStrings();
        _connectionCredentialStore.Reloaded += OnConnectionCredentialsReloaded;
    }

    private void OnConnectionCredentialsReloaded(object? sender, EventArgs e) => RefreshConnectionStrings();

    private void RefreshConnectionStrings()
    {
        _connectionString = _connectionCredentialStore.GetConnectionString("TransDBConnection");
        _logConnectionString = _connectionCredentialStore.GetConnectionString("LogDBConnection");
        _logger.LogInformation("AdmAnalysisRepository connections refreshed. TransConfigured: {t}, LogConfigured: {l}",
            !string.IsNullOrWhiteSpace(_connectionString), !string.IsNullOrWhiteSpace(_logConnectionString));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString) || !string.IsNullOrWhiteSpace(_logConnectionString);

    public async Task<long> SaveSalesAuditAsync(SalesAuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) && string.IsNullOrWhiteSpace(_logConnectionString))
        {
            _logger.LogWarning("SaveSalesAudit skipped because no DB configured.");
            return 0;
        }

        var connString = _connectionString ?? _logConnectionString!;
        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT IGNORE INTO adm_sales_audit
                (Pnr, TicketNo, AgencyPcc, TicketDate, TicketAmount, Agent, CreatedDate)
            VALUES
                (@Pnr, @TicketNo, @AgencyPcc, @TicketDate, @TicketAmount, @Agent, @CreatedDate);
        ";

        const string selectIdSql = @"
            SELECT Id FROM adm_sales_audit WHERE Pnr = @Pnr LIMIT 1;
        ";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Pnr", entry.Pnr);
        cmd.Parameters.AddWithValue("@TicketNo", (object?)entry.TicketNumber ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@AgencyPcc", (object?)entry.AgencyPcc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TicketDate", entry.TicketDate);
        cmd.Parameters.AddWithValue("@TicketAmount", entry.Amount);
        cmd.Parameters.AddWithValue("@Agent", (object?)entry.Agent ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedDate", DateTime.UtcNow);

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await using var selectCmd = new MySqlCommand(selectIdSql, conn);
            selectCmd.Parameters.AddWithValue("@Pnr", entry.Pnr);
            await using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                return reader.GetInt64("Id");

            return Convert.ToInt64(cmd.LastInsertedId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist sales audit for PNR {pnr}", entry.Pnr);
            return 0;
        }
    }

    public async Task SaveAdmAnalysisAsync(AdmAnalysisDto analysis, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_logConnectionString) && string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogWarning("SaveAdmAnalysis skipped because no DB configured.");
            return;
        }

        var connString = _logConnectionString ?? _connectionString!;
        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            INSERT INTO adm_analysis (
                SalesAuditId, Pnr, TicketNo, TicketPcc, BookingPcc, TicketMarket, BookingMarket,
                IsCrossBorder, ChurnedSegmentCount, IsChurnedSegment, MarriedSegmentCount, IsMarriedSegment,
                RiskScore, Remarks, TransactionId, CreatedDate
            )
            VALUES (
                @SalesAuditId, @Pnr, @TicketNo, @TicketPcc, @BookingPcc, @TicketMarket, @BookingMarket,
                @IsCrossBorder, @ChurnedSegmentCount, @IsChurnedSegment, @MarriedSegmentCount, @IsMarriedSegment,
                @RiskScore, @Remarks, @TransactionId, @CreatedDate
            )
            ON DUPLICATE KEY UPDATE
                SalesAuditId=VALUES(SalesAuditId), TicketNo=VALUES(TicketNo), TicketPcc=VALUES(TicketPcc),
                BookingPcc=VALUES(BookingPcc), TicketMarket=VALUES(TicketMarket), BookingMarket=VALUES(BookingMarket),
                IsCrossBorder=VALUES(IsCrossBorder), ChurnedSegmentCount=VALUES(ChurnedSegmentCount),
                IsChurnedSegment=VALUES(IsChurnedSegment), MarriedSegmentCount=VALUES(MarriedSegmentCount),
                IsMarriedSegment=VALUES(IsMarriedSegment), RiskScore=VALUES(RiskScore),
                Remarks=VALUES(Remarks), TransactionId=VALUES(TransactionId), CreatedDate=VALUES(CreatedDate);
        ";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@SalesAuditId", analysis.SalesAuditId ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@Pnr", analysis.Pnr);
        cmd.Parameters.AddWithValue("@TicketNo", (object?)analysis.TicketNo ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TicketPcc", (object?)analysis.TicketPcc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@BookingPcc", (object?)analysis.BookingPcc ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TicketMarket", (object?)analysis.TicketMarket ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@BookingMarket", (object?)analysis.BookingMarket ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsCrossBorder", analysis.IsCrossBorder);
        cmd.Parameters.AddWithValue("@ChurnedSegmentCount", analysis.ChurnedSegmentCount);
        cmd.Parameters.AddWithValue("@IsChurnedSegment", analysis.IsChurnedSegment);
        cmd.Parameters.AddWithValue("@MarriedSegmentCount", analysis.MarriedSegmentCount);
        cmd.Parameters.AddWithValue("@IsMarriedSegment", analysis.IsMarriedSegment);
        cmd.Parameters.AddWithValue("@RiskScore", analysis.RiskScore);
        cmd.Parameters.AddWithValue("@Remarks", (object?)analysis.Remarks ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@TransactionId", (object?)analysis.TransactionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedDate", analysis.AnalyzedAt);

        try
        {
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist adm analysis for PNR {pnr}", analysis.Pnr);
        }
    }

    public async Task<IReadOnlyList<AdmAnalysisDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_logConnectionString) && string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogWarning("GetAllAsync skipped because no DB configured.");
            return Array.Empty<AdmAnalysisDto>();
        }

        var connString = _logConnectionString ?? _connectionString!;
        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT SalesAuditId, Pnr, TicketNo, TicketPcc, BookingPcc, TicketMarket, BookingMarket,
                   IsCrossBorder, ChurnedSegmentCount, IsChurnedSegment, MarriedSegmentCount, IsMarriedSegment,
                   RiskScore, Remarks, TransactionId, CreatedDate
            FROM adm_analysis
            ORDER BY CreatedDate DESC
            LIMIT 1000;
        ";

        var list = new List<AdmAnalysisDto>();
        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            try
            {
                var dto = new AdmAnalysisDto
                {
                    SalesAuditId = reader["SalesAuditId"] == DBNull.Value ? null : Convert.ToInt64(reader["SalesAuditId"]),
                    Pnr = reader["Pnr"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Pnr"])!,
                    TicketNo = reader["TicketNo"] == DBNull.Value ? null : Convert.ToString(reader["TicketNo"]),
                    TicketPcc = reader["TicketPcc"] == DBNull.Value ? null : Convert.ToString(reader["TicketPcc"]),
                    BookingPcc = reader["BookingPcc"] == DBNull.Value ? null : Convert.ToString(reader["BookingPcc"]),
                    TicketMarket = reader["TicketMarket"] == DBNull.Value ? null : Convert.ToString(reader["TicketMarket"]),
                    BookingMarket = reader["BookingMarket"] == DBNull.Value ? null : Convert.ToString(reader["BookingMarket"]),
                    IsCrossBorder = reader["IsCrossBorder"] != DBNull.Value && Convert.ToBoolean(reader["IsCrossBorder"]),
                    ChurnedSegmentCount = reader["ChurnedSegmentCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["ChurnedSegmentCount"]),
                    IsChurnedSegment = reader["IsChurnedSegment"] != DBNull.Value && Convert.ToBoolean(reader["IsChurnedSegment"]),
                    MarriedSegmentCount = reader["MarriedSegmentCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["MarriedSegmentCount"]),
                    IsMarriedSegment = reader["IsMarriedSegment"] != DBNull.Value && Convert.ToBoolean(reader["IsMarriedSegment"]),
                    RiskScore = reader["RiskScore"] == DBNull.Value ? 0 : Convert.ToInt32(reader["RiskScore"]),
                    Remarks = reader["Remarks"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Remarks"])!,
                    TransactionId = reader["TransactionId"] == DBNull.Value ? null : Convert.ToString(reader["TransactionId"]),
                    AnalyzedAt = reader["CreatedDate"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(reader["CreatedDate"]) 
                };
                list.Add(dto);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read adm row");
            }
        }

        return list;
    }

    public async Task<AdmAnalysisDto?> GetByPnrAsync(string pnr, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_logConnectionString) && string.IsNullOrWhiteSpace(_connectionString))
        {
            _logger.LogWarning("GetByPnrAsync skipped because no DB configured.");
            return null;
        }

        var connString = _logConnectionString ?? _connectionString!;
        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT SalesAuditId, Pnr, TicketNo, TicketPcc, BookingPcc, TicketMarket, BookingMarket,
                   IsCrossBorder, ChurnedSegmentCount, IsChurnedSegment, MarriedSegmentCount, IsMarriedSegment,
                   RiskScore, Remarks, TransactionId, CreatedDate
            FROM adm_analysis
            WHERE TRIM(UPPER(Pnr)) = TRIM(UPPER(@Pnr))
            ORDER BY CreatedDate DESC
            LIMIT 1;
        ";

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Pnr", pnr);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

            try
            {
                return new AdmAnalysisDto
                {
                    SalesAuditId = reader["SalesAuditId"] == DBNull.Value ? null : Convert.ToInt64(reader["SalesAuditId"]),
                    Pnr = reader["Pnr"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Pnr"])!,
                    TicketNo = reader["TicketNo"] == DBNull.Value ? null : Convert.ToString(reader["TicketNo"]),
                    TicketPcc = reader["TicketPcc"] == DBNull.Value ? null : Convert.ToString(reader["TicketPcc"]),
                    BookingPcc = reader["BookingPcc"] == DBNull.Value ? null : Convert.ToString(reader["BookingPcc"]),
                    TicketMarket = reader["TicketMarket"] == DBNull.Value ? null : Convert.ToString(reader["TicketMarket"]),
                    BookingMarket = reader["BookingMarket"] == DBNull.Value ? null : Convert.ToString(reader["BookingMarket"]),
                    IsCrossBorder = reader["IsCrossBorder"] != DBNull.Value && Convert.ToBoolean(reader["IsCrossBorder"]),
                    ChurnedSegmentCount = reader["ChurnedSegmentCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["ChurnedSegmentCount"]),
                    IsChurnedSegment = reader["IsChurnedSegment"] != DBNull.Value && Convert.ToBoolean(reader["IsChurnedSegment"]),
                    MarriedSegmentCount = reader["MarriedSegmentCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["MarriedSegmentCount"]),
                    IsMarriedSegment = reader["IsMarriedSegment"] != DBNull.Value && Convert.ToBoolean(reader["IsMarriedSegment"]),
                    RiskScore = reader["RiskScore"] == DBNull.Value ? 0 : Convert.ToInt32(reader["RiskScore"]),
                    Remarks = reader["Remarks"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Remarks"])!,
                    TransactionId = reader["TransactionId"] == DBNull.Value ? null : Convert.ToString(reader["TransactionId"]),
                    AnalyzedAt = reader["CreatedDate"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(reader["CreatedDate"]) 
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read adm row for PNR {pnr}", pnr);
                return null;
            }
    }

   

    public async Task SaveCommandHistoryAsync(
        string pccCode,
        string hostCommand,
        string responseText,
        string uplId,
        string? pnr = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_logConnectionString) && string.IsNullOrWhiteSpace(_connectionString)) return;
        if (!hostCommand.TrimStart().StartsWith("*HI", StringComparison.OrdinalIgnoreCase)) return;

        var connString = _logConnectionString ?? _connectionString!;
        try
        {
            await using var conn = new MySqlConnection(connString);
            await conn.OpenAsync(cancellationToken);

            const string sql = """
                INSERT INTO HistoryItenary (PccCode, HostCommand, ResponseText, UplId, Pnr, ExecutedAt)
                VALUES (@PccCode, @HostCommand, @ResponseText, @UplId, @Pnr,
                        CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30'));
                """;

            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@PccCode", pccCode);
            cmd.Parameters.AddWithValue("@HostCommand", hostCommand);
            cmd.Parameters.AddWithValue("@ResponseText", responseText);
            cmd.Parameters.AddWithValue("@UplId", uplId);
            cmd.Parameters.AddWithValue("@Pnr", (object?)pnr ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save command history. Command: {Command}", hostCommand);
        }
    }

    public async Task<DashboardDto> GetDashboardAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_logConnectionString) && string.IsNullOrWhiteSpace(_connectionString))
            return new DashboardDto();

        var admFilter = await BuildAdmAccessFilterAsync(userId, cancellationToken);
        var connString = _logConnectionString ?? _connectionString!;
        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        var countSql = $@"
            SELECT
                SUM(IsCrossBorder = 1)                                                    AS CrossBorder,
                SUM(IsChurnedSegment = 1)                                                 AS ChurnedSegment,
                SUM(IsMarriedSegment = 1)                                                 AS MarriedSegment
            FROM adm_analysis a
            WHERE {admFilter};
        ";

        DashboardDto counts;
        await using (var cmd = new MySqlCommand(countSql, conn))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await r.ReadAsync(cancellationToken)) return new DashboardDto();
            counts = new DashboardDto
            {
               
                CrossBorder    = Convert.ToInt32(r["CrossBorder"]),
                ChurnedSegment = Convert.ToInt32(r["ChurnedSegment"]),
                MarriedSegment = Convert.ToInt32(r["MarriedSegment"]),
                
            };
        }

        var summarySql = $@"
            SELECT
                a.Pnr, a.TicketNo, a.TicketPcc, a.BookingPcc, a.TicketMarket, a.BookingMarket,
                CASE
                    WHEN a.IsCrossBorder    = 1 THEN 'Cross Border'
                    WHEN a.IsChurnedSegment = 1 THEN 'Churned Segment'
                    WHEN a.IsMarriedSegment = 1 THEN 'Married Segment'
                END AS IssueType,
                a.CreatedDate
            FROM adm_analysis a
            WHERE (a.IsCrossBorder = 1 OR a.IsChurnedSegment = 1 OR a.IsMarriedSegment = 1)
              AND {admFilter}
            ORDER BY a.CreatedDate DESC;
        ";

        var summary = new List<AdmSummaryRowDto>();
        await using (var cmd = new MySqlCommand(summarySql, conn))
        {
            cmd.Parameters.AddWithValue("@userId", userId);
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
                summary.Add(new AdmSummaryRowDto(
                    r["Pnr"] == DBNull.Value ? string.Empty : r.GetString("Pnr"),
                    r["TicketNo"] == DBNull.Value ? null : r.GetString("TicketNo"),
                    r["TicketPcc"] == DBNull.Value ? null : r.GetString("TicketPcc"),
                    r["BookingPcc"] == DBNull.Value ? null : r.GetString("BookingPcc"),
                    r["TicketMarket"] == DBNull.Value ? null : r.GetString("TicketMarket"),
                    r["BookingMarket"] == DBNull.Value ? null : r.GetString("BookingMarket"),
                    r["IssueType"] == DBNull.Value ? string.Empty : r.GetString("IssueType"),
                    r.GetDateTime("CreatedDate")));
        }

        return counts with { Summary = summary };
    }

    // Builds a WHERE filter for adm_analysis using BookingPcc (the column that exists in that table)
    private async Task<string> BuildAdmAccessFilterAsync(int userId, CancellationToken ct)
    {
        var skyOpsConnStr = _configuration.GetConnectionString("SkyOpsDBconnection");
        await using var conn = new MySqlConnection(skyOpsConnStr);
        await conn.OpenAsync(ct);

        bool hasAllPcc = false;
        var pccs = new List<string>();

        await using var cmd = new MySqlCommand(@"
            SELECT AccessType, PCCCode FROM UserPCCMapping
            WHERE UserId = @UserId AND IsActive = 1", conn);
        cmd.Parameters.AddWithValue("@UserId", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var accessType = reader.GetString(reader.GetOrdinal("AccessType"));
            if (accessType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                hasAllPcc = true;
            else if (!reader.IsDBNull(reader.GetOrdinal("PCCCode")))
                pccs.Add(reader.GetString(reader.GetOrdinal("PCCCode")));
        }

        if (hasAllPcc) return "1=1";
        if (pccs.Count > 0)
            return $"a.BookingPcc IN ({string.Join(",", pccs.Select(p => $"'{p}'"))})";
        return "1=0";
    }

    public async Task<AdmDashboardDto> GetAdmDashboardAsync(CancellationToken cancellationToken = default)
    {
        var empty = new AdmDashboardDto(
            new AdmKpiDto(0, 0, 0, 0, 0, 0),
            [], new AdmStatusPieDto(0, 0, 0), [], [], [], []);

        if (string.IsNullOrWhiteSpace(_logConnectionString) && string.IsNullOrWhiteSpace(_connectionString))
            return empty;

        var connString = _logConnectionString ?? _connectionString!;
        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        AdmKpiDto kpi;
        const string kpiSql = @"
            SELECT
                COUNT(DISTINCT a.Pnr)        AS TotalPnrs,
                COUNT(*)                     AS AdmCases,
                COUNT(*)                     AS PendingAdm,
                0                            AS ClosedAdm,
                IFNULL(SUM(s.TicketAmount),0) AS RevenueImpact,
                IFNULL(AVG(a.RiskScore),0)   AS AvgRiskScore
            FROM adm_analysis a
            LEFT JOIN adm_sales_audit s ON s.Id = a.SalesAuditId;";
        await using (var cmd = new MySqlCommand(kpiSql, conn))
        await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (!await r.ReadAsync(cancellationToken)) return empty;
            kpi = new AdmKpiDto(
                Convert.ToInt32(r["TotalPnrs"]),
                Convert.ToInt32(r["AdmCases"]),
                Convert.ToInt32(r["PendingAdm"]),
                Convert.ToInt32(r["ClosedAdm"]),
                Convert.ToDecimal(r["RevenueImpact"]),
                Convert.ToInt32(r["AvgRiskScore"]));
        }

        var trend = new List<AdmTrendPointDto>();
        const string trendSql = @"
            SELECT DATE(CreatedDate) AS Day, COUNT(*) AS Cnt
            FROM adm_analysis
            WHERE CreatedDate >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
            GROUP BY Day ORDER BY Day;";
        await using (var cmd = new MySqlCommand(trendSql, conn))
        await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
            while (await r.ReadAsync(cancellationToken))
                trend.Add(new AdmTrendPointDto(Convert.ToDateTime(r["Day"]).ToString("yyyy-MM-dd"), Convert.ToInt32(r["Cnt"])));

        AdmStatusPieDto pie;
        const string pieSql = @"
            SELECT
                SUM(CASE WHEN RiskScore >= 60 THEN 1 ELSE 0 END) AS Pending,
                SUM(CASE WHEN RiskScore >= 30 AND RiskScore < 60 THEN 1 ELSE 0 END) AS Closed,
                SUM(CASE WHEN RiskScore < 30  THEN 1 ELSE 0 END) AS Waived
            FROM adm_analysis;";
        await using (var cmd = new MySqlCommand(pieSql, conn))
        await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
            pie = await r.ReadAsync(cancellationToken)
                ? new AdmStatusPieDto(Convert.ToInt32(r["Pending"]), Convert.ToInt32(r["Closed"]), Convert.ToInt32(r["Waived"]))
                : new AdmStatusPieDto(0, 0, 0);

        var airlineWise = new List<AdmBarItemDto>();
        const string airlineSql = @"
            SELECT LEFT(TicketNo,3) AS Airline, COUNT(*) AS Cnt
            FROM adm_analysis
            WHERE TicketNo IS NOT NULL AND LENGTH(TicketNo) >= 3
            GROUP BY Airline ORDER BY Cnt DESC LIMIT 10;";
        await using (var cmd = new MySqlCommand(airlineSql, conn))
        await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
            while (await r.ReadAsync(cancellationToken))
                airlineWise.Add(new AdmBarItemDto(r["Airline"]?.ToString() ?? "Unknown", Convert.ToInt32(r["Cnt"])));

        var agentWise = new List<AdmBarItemDto>();
        const string agentSql = @"
            SELECT IFNULL(s.Agent,'Unknown') AS Agent, COUNT(*) AS Cnt
            FROM adm_analysis a
            LEFT JOIN adm_sales_audit s ON s.Id = a.SalesAuditId
            GROUP BY Agent ORDER BY Cnt DESC LIMIT 10;";
        await using (var cmd = new MySqlCommand(agentSql, conn))
        await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
            while (await r.ReadAsync(cancellationToken))
                agentWise.Add(new AdmBarItemDto(r["Agent"]?.ToString() ?? "Unknown", Convert.ToInt32(r["Cnt"])));

        var reasons = new List<AdmReasonDto>();
        const string reasonSql = @"
            SELECT
                SUM(CASE WHEN Remarks LIKE '%CrossBorder%' THEN 1 ELSE 0 END) AS ScheduleChange,
                SUM(CASE WHEN Remarks LIKE '%ChurnedSeg%'  THEN 1 ELSE 0 END) AS NoShow,
                SUM(CASE WHEN Remarks LIKE '%MarriedSeg%'  THEN 1 ELSE 0 END) AS DuplicateTicket,
                SUM(CASE WHEN RiskScore >= 60              THEN 1 ELSE 0 END) AS FareDifference
            FROM adm_analysis;";
        await using (var cmd = new MySqlCommand(reasonSql, conn))
        await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            if (await r.ReadAsync(cancellationToken))
            {
                reasons.Add(new AdmReasonDto("Schedule Change",  Convert.ToInt32(r["ScheduleChange"])));
                reasons.Add(new AdmReasonDto("No Show",          Convert.ToInt32(r["NoShow"])));
                reasons.Add(new AdmReasonDto("Duplicate Ticket", Convert.ToInt32(r["DuplicateTicket"])));
                reasons.Add(new AdmReasonDto("Fare Difference",  Convert.ToInt32(r["FareDifference"])));
            }
        }

        var revTrend = new List<AdmRevenueTrendDto>();
        const string revSql = @"
            SELECT DATE(a.CreatedDate) AS Day, IFNULL(SUM(s.TicketAmount),0) AS Amt
            FROM adm_analysis a
            LEFT JOIN adm_sales_audit s ON s.Id = a.SalesAuditId
            WHERE a.CreatedDate >= DATE_SUB(CURDATE(), INTERVAL 30 DAY)
            GROUP BY Day ORDER BY Day;";
        await using (var cmd = new MySqlCommand(revSql, conn))
        await using (var r = await cmd.ExecuteReaderAsync(cancellationToken))
            while (await r.ReadAsync(cancellationToken))
                revTrend.Add(new AdmRevenueTrendDto(Convert.ToDateTime(r["Day"]).ToString("yyyy-MM-dd"), Convert.ToDecimal(r["Amt"])));

        return new AdmDashboardDto(kpi, trend, pie, airlineWise, agentWise, reasons, revTrend);
    }
}
