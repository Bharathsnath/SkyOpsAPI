using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public sealed class DashboardRepository : IDashboardRepository
{
    private const int MaxLogXmlLength = 8192;
    private readonly IConfiguration _configuration;
    private readonly IConnectionCredentialStore _connectionCredentialStore;
    private readonly ILogger<DashboardRepository> _logger;
    private string? _connectionString;
    private string? _skyOpsConnectionString;

    public DashboardRepository(
        IConfiguration configuration,
        IConnectionCredentialStore connectionCredentialStore,
        ILogger<DashboardRepository> logger)
    {
        _configuration = configuration;
        _connectionCredentialStore = connectionCredentialStore;
        _logger = logger;
        RefreshConnectionString();
        _connectionCredentialStore.Reloaded += OnConnectionCredentialsReloaded;
    }

    private void OnConnectionCredentialsReloaded(object? sender, EventArgs e) => RefreshConnectionString();

    private void RefreshConnectionString()
    {
        _connectionString = _connectionCredentialStore.GetConnectionString("TransDBConnection")
            ?? _configuration.GetConnectionString("TransDBConnection");
        _skyOpsConnectionString = _configuration.GetConnectionString("SkyOpsDBconnection");
    }

    public async Task<ExecutiveDashboardDto> GetExecutiveDashboardAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        var summary = (await QueryAsync(conn, $"""
            SELECT
            -- Unique PNRs with HX status
            COUNT(DISTINCT CASE WHEN ActionTaken = 1 AND StatusCode = 'HX' THEN Pnr END) AS CancelledPNR,
            -- Unique PNRs with UN status
            COUNT(DISTINCT CASE WHEN ActionTaken = 1 AND StatusCode = 'UN' THEN Pnr END) AS TotalFlightCanceled,
            -- Unique PNRs with TK status
            COUNT(DISTINCT CASE WHEN ActionTaken = 1 AND StatusCode = 'TK' THEN Pnr END) AS TimeChanges,
            -- Unique PNRs with UC status 
            COUNT(DISTINCT CASE WHEN ActionTaken = 1 AND StatusCode = 'UC' THEN Pnr END) AS Unconfirmed,
            -- Total unique PCCs processed
            COUNT(DISTINCT CASE WHEN ActionTaken = 1 THEN PCC END) AS TotalPCC,
            -- Unique PNRs processed today
            COUNT(DISTINCT CASE WHEN ActionTaken = 1 AND DATE(UpdatedAt) = CURDATE() THEN Pnr END) AS TodayActions,
            -- Unique PNRs ActionTaken in the last 7 days
            COUNT(DISTINCT CASE WHEN ActionTaken = 0 AND UpdatedAt >= CURDATE() - INTERVAL 7 DAY THEN Pnr END) AS ActionsTakenLast7Days
            FROM WpTravelItineraryFlightQueueReports AS qr
            WHERE {accessFilter};
            """,
            r => new ExecutiveSummaryDto(
                r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3),
                r.GetInt64(4), r.GetInt64(5), r.GetInt64(6)),
            ct, ("@userId", userId))).First();

        var breakdown = await QueryAsync(conn, $"""
            SELECT qr.Pnr, qr.Flight, qr.TransactionId, qr.StatusCode, qr.QueueNumber, qr.ActionText, qr.ReasonText, qr.PCC, qr.ProviderName, qr.UpdatedAt
            FROM wptravelitineraryflightqueuereports AS qr
            WHERE StatusCode IN ('HX','TK','UN','UC') AND ActionTaken=1 
              AND {accessFilter}
            ORDER BY UpdatedAt DESC limit 25;
            """,
            r => new QueueBreakdownItemDto(
                r.IsDBNull(0) ? null : r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.GetDateTime(9)),
            ct, ("@userId", userId));

        return new ExecutiveDashboardDto(summary, breakdown);
    }

    public async Task<QueuePerformanceDto> GetQueuePerformanceAsync(int userId, int? queueNumber, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);
        var filter = queueNumber.HasValue ? $"WHERE QueueNumber={queueNumber.Value} AND {accessFilter}" : $"WHERE {accessFilter}";
        var hourFilter = queueNumber.HasValue
            ? $"WHERE QueueNumber={queueNumber.Value} AND DATE(UpdatedAt)=CURDATE() AND {accessFilter}"
            : $"WHERE DATE(UpdatedAt)=CURDATE() AND {accessFilter}";

        var byStatus = await QueryAsync(conn, $"""
            SELECT QueueNumber, StatusCode, COUNT(*) FROM wptravelitineraryflightqueuereports AS qr {filter}
            GROUP BY QueueNumber, StatusCode ORDER BY QueueNumber, COUNT(*) DESC
            """, r => new QueueByStatusDto(r.GetInt32(0), r.GetString(1), r.GetInt64(2)), ct, ("@userId", userId));

        var byHour = await QueryAsync(conn, $"""
            SELECT HOUR(UpdatedAt), COUNT(*) FROM wptravelitineraryflightqueuereports AS qr {hourFilter}
            GROUP BY HOUR(UpdatedAt) ORDER BY HOUR(UpdatedAt)
            """, r => new QueueByHourDto(r.GetInt32(0), r.GetInt64(1)), ct, ("@userId", userId));

        return new QueuePerformanceDto(byStatus, byHour);
    }

    public async Task<PccPerformanceDto> GetPccPerformanceAsync(int userId, string? pcc, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        var ranking = await QueryAsync(conn, $"""
            SELECT PCC, COUNT(*), SUM(CASE WHEN StatusCode IN ('HX','UN','UC') THEN 1 ELSE 0 END),
                   SUM(CASE WHEN StatusCode='TK' THEN 1 ELSE 0 END), COUNT(DISTINCT Pnr)
            FROM wptravelitineraryflightqueuereports AS qr WHERE ActionTaken=1 AND {accessFilter}
            GROUP BY PCC ORDER BY COUNT(*) DESC;
            """,
            r => new PccPerformanceItemDto(
                r.IsDBNull(0) ? "Null" : r.GetString(0),
                r.IsDBNull(1) ? 0 : r.GetInt64(1),
                r.IsDBNull(2) ? 0 : r.GetInt64(2),
                r.IsDBNull(3) ? 0 : r.GetInt64(3),
                r.IsDBNull(4) ? 0 : r.GetInt64(4)),
            ct, ("@userId", userId));

        return new PccPerformanceDto(ranking);
    }

    public async Task<FlightStatusDto> GetFlightStatusAsync(int userId, string? statusCode, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        var flights = await QueryAsync(conn, $"""
            SELECT Pnr, Flight, TransactionId, StatusCode, QueueNumber, ActionText, PCC, ProviderName, UpdatedAt
            FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='UN' AND ActionTaken=1 AND {accessFilter}
            ORDER BY UpdatedAt DESC
            """,
            r => new FlightStatusItemDto(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetDateTime(8)),
            ct, ("@userId", userId));

        var summary = await QueryAsync(conn, $"""
            SELECT StatusCode, COUNT(*) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='UN' AND ActionTaken=1 AND {accessFilter}
            GROUP BY StatusCode ORDER BY COUNT(*) DESC
            """,
            r => new FlightStatusSummaryDto(r.GetString(0), r.GetInt64(1)),
            ct, ("@userId", userId));

        return new FlightStatusDto(flights, summary);
    }

    public async Task<CriticalQueueDto> GetCriticalQueueAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        var critical = await QueryAsync(conn, $"""
            SELECT Pnr, Flight, TransactionId, StatusCode, QueueNumber, ActionText, ReasonText, PCC, ProviderName, UpdatedAt
             FROM wptravelitineraryflightqueuereports AS qr
              WHERE StatusCode='HX' AND ActionTaken=1 AND {accessFilter} 
              ORDER BY UpdatedAt DESC
            """,
            r => new CriticalQueueItemDto(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetDateTime(9)),
            ct, ("@userId", userId));
        var Unticketedcritical = await QueryAsync(conn, $"""
            SELECT
             q.Pnr, q.Flight,q.TransactionId,q.StatusCode,q.QueueNumber,q.ActionText,
            q.ReasonText,q.PCC,q.ProviderName,q.UpdatedAt
            FROM wptravelitineraryflightqueuereports q
            WHERE q.isTicketed = 0
                AND q.ActionTaken = 1
            AND {accessFilter.Replace("qr.", "q.")}
              AND EXISTS
            (
            SELECT 1
            FROM wptravelitineraryflightqueuereports x
              WHERE x.Pnr = q.Pnr
                AND x.isTicketed = 0
                AND x.ActionTaken = 1
            AND {accessFilter.Replace("qr.", "x.")}
             GROUP BY x.Pnr
              HAVING COUNT(*) = SUM(CASE WHEN x.StatusCode='HX' THEN 1 ELSE 0 END)
            )
            ORDER BY q.Pnr,q.SegmentNumber
            """,
            r => new CriticalQueueItemDto(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetDateTime(9)),
            ct, ("@userId", userId));

        var total = await ScalarAsync<long>(conn, $"SELECT COUNT(*) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='HX' AND ActionTaken=1 AND {accessFilter}", ct, ("@userId", userId));
        var ticketedtotal = await ScalarAsync<long>(conn, $"SELECT COUNT(*) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='HX' AND ActionTaken=1 and isTicketed = 1 AND {accessFilter}", ct, ("@userId", userId));
        var unticketedtotal = await ScalarAsync<long>(conn, $"SELECT COUNT(*) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='HX' AND ActionTaken=1 and isTicketed = 0 AND {accessFilter}", ct, ("@userId", userId));
        return new CriticalQueueDto(total, critical, Unticketedcritical, ticketedtotal, unticketedtotal);
    }

    public async Task<DelayAnalysisDto> GetDelayAnalysisAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        var delays = await QueryAsync(conn, $"""
            SELECT Pnr, Flight, TransactionId, DelayMinutes, DelayHours, QueueNumber, PCC, ProviderName, UpdatedAt
            FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='tk' AND ActionTaken=1 AND {accessFilter}
            ORDER BY UpdatedAt DESC
            """,
            r => new DelayItemDto(
                r.GetString(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetDecimal(4),
                r.GetInt32(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetDateTime(8)),
            ct, ("@userId", userId));

        var postponed = await ScalarAsync<decimal?>(conn, $"SELECT COUNT(DelayMinutes) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='tk' AND DelayMinutes>0 AND ActionTaken=1 AND {accessFilter}", ct, ("@userId", userId));
        var preponed = await ScalarAsync<int?>(conn, $"SELECT COUNT(DelayMinutes) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='tk' AND DelayMinutes<0 AND ActionTaken=1 AND {accessFilter}", ct, ("@userId", userId));
        var ontime = await ScalarAsync<int?>(conn, $"SELECT COUNT(DelayMinutes) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='tk' AND DelayMinutes=0 AND ActionTaken=1 AND {accessFilter}", ct, ("@userId", userId));
        var scheduleChange = await ScalarAsync<long>(conn, $"SELECT COUNT(*) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='tk' AND ActionTaken=1 AND {accessFilter}", ct, ("@userId", userId));

        return new DelayAnalysisDto(scheduleChange, postponed, preponed, ontime, delays);
    }

    public async Task<FlightImpactDto> GetFlightImpactAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        var impacted = await QueryAsync(conn, $"""
            SELECT Pnr, Flight, TransactionId, StatusCode, QueueNumber, ActionText, ReasonText, PCC, ProviderName, UpdatedAt
            FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='UC' AND ActionTaken=1 AND {accessFilter}
            ORDER BY UpdatedAt DESC
            """,
            r => new FlightImpactItemDto(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetDateTime(9)),
            ct, ("@userId", userId));

        var total = await ScalarAsync<long>(conn, $"SELECT COUNT(*) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='UC' AND ActionTaken=1 AND {accessFilter}", ct, ("@userId", userId));
        return new FlightImpactDto(impacted, total);
    }

    public async Task<PnrAnalysisDto> GetPnrAnalysisAsync(int userId, string? pnr, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        if (!string.IsNullOrWhiteSpace(pnr))
        {
            var segments = await QueryAsync(conn, $"""
                SELECT Pnr, Flight, StatusCode, QueueNumber, SegmentNumber, ActionText,
                       DelayMinutes, ReasonText, RecommendedFutureCommand, Summary, PCC, UpdatedAt
                FROM wptravelitineraryflightqueuereports AS qr WHERE Pnr=@Pnr AND {accessFilter} ORDER BY SegmentNumber
                """,
                r => new PnrSegmentDto(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4),
                    r.GetString(5), r.IsDBNull(6) ? null : r.GetInt32(6), r.IsDBNull(7) ? null : r.GetString(7),
                    r.IsDBNull(8) ? null : r.GetString(8), r.GetString(9), r.IsDBNull(10) ? null : r.GetString(10), r.GetDateTime(11)),
                ct, ("@userId", userId), ("@Pnr", pnr));
            return new PnrAnalysisDto(pnr, segments, null);
        }

        var topPnrs = await QueryAsync(conn, $"""
            SELECT Pnr, COUNT(*), GROUP_CONCAT(DISTINCT StatusCode)
            FROM wptravelitineraryflightqueuereports AS qr WHERE {accessFilter} GROUP BY Pnr ORDER BY COUNT(*) DESC LIMIT 20
            """,
            r => new TopPnrDto(r.GetString(0), r.GetInt64(1), r.GetString(2)), ct, ("@userId", userId));

        return new PnrAnalysisDto(null, null, topPnrs);
    }

    public async Task<PnrsDto> GetPnrsAsync(int userId, string? pnr, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        var byPNR = await QueryAsync(conn, $"""
            SELECT PNR, PCC, ProviderName,
                SUM(CASE WHEN StatusCode='TK' THEN 1 ELSE 0 END),
                SUM(CASE WHEN StatusCode='HX' THEN 1 ELSE 0 END),
                SUM(CASE WHEN StatusCode='UN' THEN 1 ELSE 0 END),
                SUM(CASE WHEN StatusCode='UC' THEN 1 ELSE 0 END)
            FROM (SELECT DISTINCT PNR, PCC, ProviderName, SegmentNumber, Flight, StatusCode
                  FROM WpTravelItineraryFlightQueueReports AS qr WHERE ActionTaken=1 AND {accessFilter}) AS x
            GROUP BY PNR, PCC, ProviderName ORDER BY PNR;
            """,
            r => new PnrRowDto(
                r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)),
            ct, ("@userId", userId));

        var count = await QueryAsync(conn,
            $"SELECT COUNT(DISTINCT qr.PCC) FROM WpTravelItineraryFlightQueueReports AS qr WHERE ActionTaken=1 AND {accessFilter}",
            r => new PnrCountDto(r.GetInt64(0)), ct, ("@userId", userId));

        return new PnrsDto(byPNR, count);
    }

    public async Task<OperationalDashboardDto> GetOperationalDashboardAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        var openCritical = await ScalarAsync<long>(conn, $"SELECT COUNT(*) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode IN ('HX','UN','UC') AND ActionTaken=1 AND DATE(UpdatedAt)=CURDATE() AND {accessFilter}", ct, ("@userId", userId));
        var tkMonitor = await ScalarAsync<long>(conn, $"SELECT COUNT(*) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode='TK' AND ActionTaken=1 AND DATE(UpdatedAt)=CURDATE() AND {accessFilter}", ct, ("@userId", userId));
        var hxUnUC = await ScalarAsync<long>(conn, $"SELECT COUNT(*) FROM wptravelitineraryflightqueuereports AS qr WHERE StatusCode IN ('HX','UN','UC') AND ActionTaken=1 AND DATE(UpdatedAt)=CURDATE() AND {accessFilter}", ct, ("@userId", userId));

        var livePNR = await QueryAsync(conn, $"""
            SELECT Pnr, Flight, TransactionId, StatusCode, QueueNumber, ActionText, PCC, ProviderName, UpdatedAt
            FROM wptravelitineraryflightqueuereports AS qr
            WHERE StatusCode IN ('HX','TK','UN','UC') AND ActionTaken=1 AND DATE(UpdatedAt)=CURDATE() AND {accessFilter}
            ORDER BY UpdatedAt DESC
            """,
            r => new LivePnrItemDto(
                r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4),
                r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.GetDateTime(8)),
            ct, ("@userId", userId));

        return new OperationalDashboardDto(openCritical, tkMonitor, hxUnUC, livePNR);
    }

    public async Task<ManagementDashboardDto> GetManagementDashboardAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);

        var pccRanking = await QueryAsync(conn, """
            SELECT PCC, COUNT(*),
                   SUM(CASE WHEN StatusCode IN ('HX','UN','UC') THEN 1 ELSE 0 END),
                   ROUND(SUM(CASE WHEN StatusCode IN ('HX','UN','UC') THEN 1 ELSE 0 END)*100.0/COUNT(*),2),
                   SUM(CASE WHEN StatusCode='tk' THEN 1 ELSE 0 END),
                   ROUND(SUM(CASE WHEN StatusCode='tk' THEN 1 ELSE 0 END)*100/COUNT(*),2)
            FROM wptravelitineraryflightqueuereports WHERE PCC IS NOT NULL AND ActionTaken=1
            GROUP BY PCC ORDER BY COUNT(*) DESC
            """,
            r => new PccRankingDto(
                r.IsDBNull(0) ? null : r.GetString(0),
                r.IsDBNull(1) ? 0 : r.GetInt64(1),
                r.IsDBNull(2) ? 0 : r.GetInt64(2),
                r.IsDBNull(3) ? 0 : r.GetDecimal(3),
                r.IsDBNull(4) ? 0 : r.GetInt64(4),
                r.IsDBNull(5) ? 0 : r.GetDecimal(5)),
            ct);

        var totalToday = await ScalarAsync<long>(conn, "SELECT COUNT(*) FROM wptravelitineraryflightqueuereports WHERE DATE(UpdatedAt)=CURDATE() AND ActionTaken=1", ct);
        var criticalToday = await ScalarAsync<long>(conn, "SELECT COUNT(*) FROM wptravelitineraryflightqueuereports WHERE StatusCode IN ('HX','UN','UC') AND DATE(UpdatedAt)=CURDATE() AND ActionTaken=1", ct);
        var criticalPct = totalToday > 0 ? Math.Round(criticalToday * 100.0 / totalToday, 2) : 0;

        var queueEfficiency = await QueryAsync(conn, """
            SELECT QueueNumber, COUNT(DISTINCT Pnr), COUNT(*)
            FROM wptravelitineraryflightqueuereports WHERE DATE(UpdatedAt)=CURDATE() AND ActionTaken=1
            GROUP BY QueueNumber ORDER BY QueueNumber
            """,
            r => new QueueEfficiencyDto(
                r.IsDBNull(0) ? 0 : r.GetInt32(0),
                r.IsDBNull(1) ? 0 : r.GetInt64(1),
                r.IsDBNull(2) ? 0 : r.GetInt64(2)),
            ct);

        var providerRanking = await QueryAsync(conn, """
            SELECT ProviderName, COUNT(*),
                   SUM(CASE WHEN StatusCode IN ('HX','UN','UC') THEN 1 ELSE 0 END),
                   ROUND(SUM(CASE WHEN StatusCode IN ('HX','UN','UC') THEN 1 ELSE 0 END)*100.0/COUNT(*),2)
            FROM wptravelitineraryflightqueuereports WHERE ProviderName IS NOT NULL AND ActionTaken=1
            GROUP BY ProviderName ORDER BY COUNT(*) DESC
            """,
            r => new ProviderRankingDto(
                r.IsDBNull(0) ? null : r.GetString(0),
                r.IsDBNull(1) ? 0 : r.GetInt64(1),
                r.IsDBNull(2) ? 0 : r.GetInt64(2),
                r.IsDBNull(3) ? 0 : r.GetDecimal(3)),
            ct);

        var transactionRanking = await QueryAsync(conn, """
            SELECT TransactionId, PCC, COUNT(*),
                   SUM(CASE WHEN StatusCode IN ('HX','UN','UC') THEN 1 ELSE 0 END),
                   ROUND(SUM(CASE WHEN StatusCode IN ('HX','UN','UC') THEN 1 ELSE 0 END)*100.0/COUNT(*),0)
            FROM wptravelitineraryflightqueuereports WHERE PCC IS NOT NULL AND ActionTaken=1
            GROUP BY PCC, TransactionId ORDER BY COUNT(*) DESC
            """,
            r => new TransactionRankingDto(
                r.IsDBNull(0) ? null : r.GetString(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? 0 : r.GetInt64(2),
                r.IsDBNull(3) ? 0 : r.GetInt64(3),
                r.IsDBNull(4) ? 0 : r.GetDecimal(4)),
            ct);

        return new ManagementDashboardDto(pccRanking, queueEfficiency, providerRanking, transactionRanking, totalToday, criticalToday, criticalPct);
    }

    public async Task<XmlLogsDto> GetXmlLogsAsync(CancellationToken ct = default)
    {
        var logConnStr = _connectionCredentialStore.GetConnectionString("LogDBConnection")
            ?? _configuration.GetConnectionString("LogDBConnection");
        await using var conn = new MySqlConnection(logConnStr);
        await conn.OpenAsync(ct);

        var logs = await QueryAsync(conn, """
            SELECT Log_LogID_ID, Log_UPL_VC, Log_WorkFlow_VC, Log_ModuleName_VC, Log_ModuleCode_VC,
                   Log_ClassName_VC, Log_LogCode_VC, Log_LogXML_VC, Log_Remarks_VC, Log_Date
            FROM wp_xmllog WHERE Log_Date>=DATE_SUB(NOW(),INTERVAL 7 DAY)
            ORDER BY Log_Date DESC LIMIT 1000;
            """,
            r => new XmlLogItemDto(
                r.GetInt64(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6),
                Truncate(r.IsDBNull(7) ? null : r.GetString(7), MaxLogXmlLength), r.IsDBNull(8) ? null : r.GetString(8), r.GetDateTime(9)),
            ct);

        return new XmlLogsDto(logs.Count, logs);
    }

    public async Task<ActionTakenDto> GetActionTakenAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var accessFilter = await BuildAccessFilterAsync(userId, ct);

        var pending = await QueryAsync(conn, $"""
            SELECT Pnr, TransactionId, ReceivedDateTime, SegmentNumber, Flight, StatusCode,
                   ActionText, UpdatedAt, PCC, Remarks, ProviderName, CustomeRemarks
            FROM WpTravelItineraryFlightQueueReports AS qr
            WHERE ActionTaken=0 AND UpdatedAt>=DATE_SUB(NOW(),INTERVAL 7 DAY) AND {accessFilter}
            ORDER BY UpdatedAt DESC;
            """,
            r => new ActionTakenItemDto(
                r.IsDBNull(0) ? null : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetDateTime(2), r.IsDBNull(3) ? null : r.GetInt32(3),
                r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetDateTime(7),
                r.IsDBNull(8) ? null : r.GetString(8), r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10), r.IsDBNull(11) ? null : r.GetString(11)),
            ct, ("@userId", userId));

        return new ActionTakenDto(pending.Count, pending);
    }

    public async Task<ErrorLogsDto> GetErrorLogsAsync(CancellationToken ct = default)
    {
        var logConnStr = _connectionCredentialStore.GetConnectionString("LogDBConnection")
            ?? _configuration.GetConnectionString("LogDBConnection");
        await using var conn = new MySqlConnection(logConnStr);
        await conn.OpenAsync(ct);

        var logs = await QueryAsync(conn, """
            SELECT Log_LogID_ID, Log_UPL_VC, Log_WorkFlow_VC, Log_UserID_NB, Log_UserType_VC,
                   Log_ModuleName_VC, Log_ModuleCode_VC, Log_ClassName_VC, Log_ProcedureName_VC,
                   Log_ErrorCode_VC, Log_Remarks_VC, Log_LogDate_DT, Log_Level_VC,
                   Log_IPDetails_VC, Log_SessionID_VC
            FROM log_errorlog
            WHERE Log_LogDate_DT >= DATE_SUB(NOW(), INTERVAL 7 DAY)
            ORDER BY Log_LogDate_DT DESC LIMIT 1000;
            """,
            r => new ErrorLogItemDto(
                r.GetInt64(0),
                r.IsDBNull(1) ? null : r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt64(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetString(7),
                r.IsDBNull(8) ? null : r.GetString(8),
                r.IsDBNull(9) ? null : r.GetString(9),
                r.IsDBNull(10) ? null : r.GetString(10),
                r.IsDBNull(11) ? null : r.GetDateTime(11),
                r.IsDBNull(12) ? null : r.GetString(12),
                r.IsDBNull(13) ? null : r.GetString(13),
                r.IsDBNull(14) ? null : r.GetString(14)),
            ct);

        return new ErrorLogsDto(logs.Count, logs);
    }

    public async Task<PriorityPnrStatusDto> GetPriorityPnrStatusAsync(CancellationToken ct = default)
    {

        await using var skyConn = await OpenSkyOpsConnectionAsync(ct);

        var priorityPnrs = await QueryAsync(skyConn, $"""
             SELECT
            Id,
            Pnr,
            PriorityLevel,
            TravelDate,
            NotifyEmail
                 FROM prioritypnrmaster
            WHERE IsActive = 1
                ORDER BY CreatedDate DESC;

            """,
            r => new PriorityPnrStatusCardDto(
            r.GetInt64(0),
            r.GetString(1),
            r.IsDBNull(2) ? "MEDIUM" : r.GetString(2),
            r.IsDBNull(3) ? null : r.GetDateTime(3),
            r.IsDBNull(4) ? "" : r.GetString(4),
            0,
            0,
            0,
            0,
            false),
        ct);

        if (!priorityPnrs.Any())
            return new PriorityPnrStatusDto(priorityPnrs);

        await using var conn = await OpenAsync(ct);

        // Separate real PNRs from email-keyed entries
        var realPnrs = priorityPnrs.Where(x => !x.Pnr.Contains('@')).Select(x => x.Pnr).Distinct().ToList();
        var emailPnrs = priorityPnrs.Where(x => x.Pnr.Contains('@')).ToList();

        var lookup = new Dictionary<string, QueueStatusDto>(StringComparer.OrdinalIgnoreCase);

        // --- lookup by actual PNR ---
        if (realPnrs.Count > 0)
        {
            var parameters = string.Join(",", realPnrs.Select((_, i) => $"@p{i}"));
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"""
                SELECT Pnr,
                    SUM(CASE WHEN StatusCode='HX' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN StatusCode='TK' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN StatusCode='UN' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN StatusCode='UC' THEN 1 ELSE 0 END),
                    COUNT(*)
                FROM wptravelitineraryflightqueuereports
                WHERE ActionTaken=1 AND Pnr IN ({parameters})
                GROUP BY Pnr;
                """;
            for (int i = 0; i < realPnrs.Count; i++)
                cmd.Parameters.AddWithValue($"@p{i}", realPnrs[i]);
            await using var r1 = await cmd.ExecuteReaderAsync(ct);
            while (await r1.ReadAsync(ct))
                lookup[r1.GetString(0)] = new QueueStatusDto { Pnr = r1.GetString(0), HX = r1.GetInt64(1), TK = r1.GetInt64(2), UN = r1.GetInt64(3), UC = r1.GetInt64(4), TotalFound = r1.GetInt64(5) };
        }

        // --- lookup by RemarkEmail for email-keyed entries ---
        foreach (var ep in emailPnrs)
        {
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = """
                SELECT Pnr,
                    SUM(CASE WHEN StatusCode='HX' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN StatusCode='TK' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN StatusCode='UN' THEN 1 ELSE 0 END),
                    SUM(CASE WHEN StatusCode='UC' THEN 1 ELSE 0 END),
                    COUNT(*)
                FROM wptravelitineraryflightqueuereports
                WHERE ActionTaken=1 AND FIND_IN_SET(@email, REPLACE(RemarkEmail, ';', ',')) > 0
                GROUP BY Pnr
                LIMIT 1;
                """;
            cmd2.Parameters.AddWithValue("@email", ep.Pnr.ToLowerInvariant());
            await using var r2 = await cmd2.ExecuteReaderAsync(ct);
            if (await r2.ReadAsync(ct))
            {
                var actualPnr = r2.GetString(0);
                // store under the email key so the merge below finds it
                lookup[ep.Pnr] = new QueueStatusDto { Pnr = actualPnr, HX = r2.GetInt64(1), TK = r2.GetInt64(2), UN = r2.GetInt64(3), UC = r2.GetInt64(4), TotalFound = r2.GetInt64(5) };
            }
        }

        var result = priorityPnrs
            .Select(p =>
            {
                lookup.TryGetValue(p.Pnr, out var q);
                // For email-keyed entries show the real PNR found in queue, else show the stored key
                var displayPnr = (q is not null && p.Pnr.Contains('@')) ? q.Pnr : p.Pnr;
                return new PriorityPnrStatusCardDto(
                    p.Id, displayPnr, p.PriorityLevel, p.TravelDate, p.NotifyEmail,
                    q?.HX ?? 0, q?.TK ?? 0, q?.UN ?? 0, q?.UC ?? 0,
                    (q?.TotalFound ?? 0) > 0);
            })
            .ToList();

        return new PriorityPnrStatusDto(result);

    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task<object> GetAccessFilterDebugAsync(int userId, CancellationToken ct)
    {
        var skyOpsConnStr = _configuration.GetConnectionString("SkyOpsDBconnection");
        await using var conn = new MySqlConnection(skyOpsConnStr);
        await conn.OpenAsync(ct);

        await using var pccCmd = new MySqlCommand("""
            SELECT AccessType, PCCCode FROM UserPCCMapping
            WHERE UserId = @userId AND IsActive = 1
            """, conn);
        pccCmd.Parameters.AddWithValue("@userId", userId);
        await using var pccReader = await pccCmd.ExecuteReaderAsync(ct);
        var pccRows = new List<object>();
        while (await pccReader.ReadAsync(ct))
            pccRows.Add(new { AccessType = pccReader.GetString(0), PCCCode = pccReader.IsDBNull(1) ? null : pccReader.GetString(1) });
        await pccReader.DisposeAsync();

        await using var mktCmd = new MySqlCommand("""
            SELECT ump.PermissionType, ump.ReferenceId, cm.TransactionPrefix, cm.CompanyName
            FROM UserMarketPermission ump
            INNER JOIN CompanyMaster cm
                ON (ump.PermissionType = 'C' AND cm.Id = ump.ReferenceId)
                OR (ump.PermissionType = 'M' AND cm.MarketId = ump.ReferenceId)
            WHERE ump.UserId = @userId AND ump.IsActive = 1 AND cm.IsActive = 1
            """, conn);
        mktCmd.Parameters.AddWithValue("@userId", userId);
        await using var mktReader = await mktCmd.ExecuteReaderAsync(ct);
        var companyRows = new List<object>();
        while (await mktReader.ReadAsync(ct))
            companyRows.Add(new
            {
                PermissionType = mktReader.GetString(0),
                ReferenceId = mktReader.GetInt32(1),
                TransactionPrefix = mktReader.IsDBNull(2) ? null : mktReader.GetString(2),
                CompanyName = mktReader.IsDBNull(3) ? null : mktReader.GetString(3)
            });

        var filter = await BuildAccessFilterAsync(userId, ct);
        return new { UserId = userId, PccMappings = pccRows, ResolvedCompanies = companyRows, GeneratedFilter = filter };
    }

    private async Task<string> BuildAccessFilterAsync(int userId, CancellationToken ct)
    {
        var skyOpsConnStr = _configuration.GetConnectionString("SkyOpsDBconnection");

        await using var conn = new MySqlConnection(skyOpsConnStr);
        await conn.OpenAsync(ct);

        // =====================================================
        // PCC ACCESS
        // =====================================================

        bool hasAllPcc = false;
        var pccs = new List<string>();

        await using (var cmd = new MySqlCommand(@"
        SELECT AccessType, PCCCode
        FROM UserPCCMapping
        WHERE UserId=@UserId
          AND IsActive=1", conn))
        {
            cmd.Parameters.AddWithValue("@UserId", userId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var accessType = reader.GetString(reader.GetOrdinal("AccessType"));

                if (accessType.Equals("ALL", StringComparison.OrdinalIgnoreCase))
                {
                    hasAllPcc = true;
                }
                else if (!reader.IsDBNull(reader.GetOrdinal("PCCCode")))
                {
                    pccs.Add(reader.GetString(reader.GetOrdinal("PCCCode")));
                }
            }
        }

        // =====================================================
        // COMPANY ACCESS
        // =====================================================

        var prefixes = new List<string>();

        await using (var cmd = new MySqlCommand(@"
        SELECT DISTINCT cm.TransactionPrefix
        FROM UserMarketPermission ump
        INNER JOIN CompanyMaster cm
            ON (
                ump.PermissionType='C'
                AND ump.ReferenceId = cm.Id
            )
        WHERE ump.UserId=@UserId
          AND ump.IsActive=1
          AND cm.IsActive=1", conn))
        {
            cmd.Parameters.AddWithValue("@UserId", userId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                if (!reader.IsDBNull(0))
                    prefixes.Add(reader.GetString(0));
            }
        }

        // =====================================================
        // PCC FILTER
        // =====================================================

        string pccFilter = hasAllPcc
            ? "1=1"
            : pccs.Count > 0
                ? $"qr.PCC IN ({string.Join(",", pccs.Select(x => $"'{x}'"))})"
                : "1=0";

        // =====================================================
        // COMPANY FILTER
        // =====================================================

        string companyFilter = prefixes.Count > 0
            ? $"({string.Join(" OR ", prefixes.Select(x => $"qr.TransactionId LIKE '{x}%'"))})"
            : "1=0";

        return $"({pccFilter}) AND ({companyFilter})";
    }


    private static async Task<T?> ScalarAsync<T>(MySqlConnection conn, string sql, CancellationToken ct, params (string name, object value)[] parameters)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        foreach (var parameter in parameters)
            cmd.Parameters.AddWithValue(parameter.name, parameter.value);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is DBNull or null) return default;
        var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);
        return (T)Convert.ChangeType(result, targetType);
    }

    private static async Task<List<T>> QueryAsync<T>(MySqlConnection conn, string sql, Func<MySqlDataReader, T> map, CancellationToken ct, params (string name, object value)[] parameters)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        foreach (var parameter in parameters)
            cmd.Parameters.AddWithValue(parameter.name, parameter.value);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<T>();
        while (await reader.ReadAsync(ct))
            list.Add(map(reader));
        return list;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }

    private static string ExtractDatabase(string? connectionString, string name = "connection")
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException($"{name} is not configured.");
        var builder = new MySqlConnectionStringBuilder(connectionString);
        return builder.Database;
    }

    private async Task<MySqlConnection> OpenSkyOpsConnectionAsync(CancellationToken ct)
    {
        var conn = new MySqlConnection(_skyOpsConnectionString);
        await conn.OpenAsync(ct);
        return conn;
    }
}

