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
    private readonly ILogger<AdmAnalysisRepository> _logger;
    private string? _connectionString;
    private string? _logConnectionString;

    public AdmAnalysisRepository(
        IConfiguration configuration,
        IConnectionCredentialStore connectionCredentialStore,
        ILogger<AdmAnalysisRepository> logger)
    {
        _configuration = configuration;
        _connectionCredentialStore = connectionCredentialStore;
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

    public async Task SaveSalesAuditAsync(SalesAuditEntry entry, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString) && string.IsNullOrWhiteSpace(_logConnectionString))
        {
            _logger.LogWarning("SaveSalesAudit skipped because no DB configured.");
            return;
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist sales audit for PNR {pnr}", entry.Pnr);
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
                IsCrossBorder, ChangedSegmentCount, IsChangedSegment, MarriedSegmentCount, IsMarriedSegment,
                RiskScore, Remarks, CreatedDate
            )
            VALUES (
                @SalesAuditId, @Pnr, @TicketNo, @TicketPcc, @BookingPcc, @TicketMarket, @BookingMarket,
                @IsCrossBorder, @ChangedSegmentCount, @IsChangedSegment, @MarriedSegmentCount, @IsMarriedSegment,
                @RiskScore, @Remarks, @CreatedDate
            )
            ON DUPLICATE KEY UPDATE
                TicketPcc=VALUES(TicketPcc), BookingPcc=VALUES(BookingPcc),
                TicketMarket=VALUES(TicketMarket), BookingMarket=VALUES(BookingMarket),
                IsCrossBorder=VALUES(IsCrossBorder), ChangedSegmentCount=VALUES(ChangedSegmentCount),
                IsChangedSegment=VALUES(IsChangedSegment), MarriedSegmentCount=VALUES(MarriedSegmentCount),
                IsMarriedSegment=VALUES(IsMarriedSegment), RiskScore=VALUES(RiskScore),
                Remarks=VALUES(Remarks), CreatedDate=VALUES(CreatedDate);
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
        cmd.Parameters.AddWithValue("@ChangedSegmentCount", analysis.ChangedSegmentCount);
        cmd.Parameters.AddWithValue("@IsChangedSegment", analysis.IsChangedSegment);
        cmd.Parameters.AddWithValue("@MarriedSegmentCount", analysis.MarriedSegmentCount);
        cmd.Parameters.AddWithValue("@IsMarriedSegment", analysis.IsMarriedSegment);
        cmd.Parameters.AddWithValue("@RiskScore", analysis.RiskScore);
        cmd.Parameters.AddWithValue("@Remarks", (object?)analysis.Remarks ?? DBNull.Value);
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
                   IsCrossBorder, ChangedSegmentCount, IsChangedSegment, MarriedSegmentCount, IsMarriedSegment,
                   RiskScore, Remarks, CreatedDate
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
                    ChangedSegmentCount = reader["ChangedSegmentCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["ChangedSegmentCount"]),
                    IsChangedSegment = reader["IsChangedSegment"] != DBNull.Value && Convert.ToBoolean(reader["IsChangedSegment"]),
                    MarriedSegmentCount = reader["MarriedSegmentCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["MarriedSegmentCount"]),
                    IsMarriedSegment = reader["IsMarriedSegment"] != DBNull.Value && Convert.ToBoolean(reader["IsMarriedSegment"]),
                    RiskScore = reader["RiskScore"] == DBNull.Value ? 0 : Convert.ToInt32(reader["RiskScore"]),
                    Remarks = reader["Remarks"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Remarks"])!,
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
                   IsCrossBorder, ChangedSegmentCount, IsChangedSegment, MarriedSegmentCount, IsMarriedSegment,
                   RiskScore, Remarks, CreatedDate
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
                    ChangedSegmentCount = reader["ChangedSegmentCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["ChangedSegmentCount"]),
                    IsChangedSegment = reader["IsChangedSegment"] != DBNull.Value && Convert.ToBoolean(reader["IsChangedSegment"]),
                    MarriedSegmentCount = reader["MarriedSegmentCount"] == DBNull.Value ? 0 : Convert.ToInt32(reader["MarriedSegmentCount"]),
                    IsMarriedSegment = reader["IsMarriedSegment"] != DBNull.Value && Convert.ToBoolean(reader["IsMarriedSegment"]),
                    RiskScore = reader["RiskScore"] == DBNull.Value ? 0 : Convert.ToInt32(reader["RiskScore"]),
                    Remarks = reader["Remarks"] == DBNull.Value ? string.Empty : Convert.ToString(reader["Remarks"])!,
                    AnalyzedAt = reader["CreatedDate"] == DBNull.Value ? DateTime.MinValue : Convert.ToDateTime(reader["CreatedDate"]) 
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read adm row for PNR {pnr}", pnr);
                return null;
            }
    }

    public async Task<IReadOnlyDictionary<string, string>> GetPccMarketMapAsync(CancellationToken cancellationToken = default)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var connString = _logConnectionString ?? _connectionString;
        if (string.IsNullOrWhiteSpace(connString)) return map;

        try
        {
            await using var conn = new MySqlConnection(connString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new MySqlCommand("SELECT Pcc, Market FROM adm_pcc_market;", conn);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                map[reader.GetString(0)] = reader.GetString(1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load PCC market map; using empty map.");
        }

        return map;
    }

    public async Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_logConnectionString) && string.IsNullOrWhiteSpace(_connectionString))
            return new DashboardDto();

        var connString = _logConnectionString ?? _connectionString!;
        await using var conn = new MySqlConnection(connString);
        await conn.OpenAsync(cancellationToken);

        const string sql = @"
            SELECT
                COUNT(*) AS Total,
                SUM(CASE WHEN RiskScore < 30 THEN 1 ELSE 0 END) AS Low,
                SUM(CASE WHEN RiskScore >= 30 AND RiskScore < 60 THEN 1 ELSE 0 END) AS Medium,
                SUM(CASE WHEN RiskScore >= 60 AND RiskScore < 90 THEN 1 ELSE 0 END) AS High,
                SUM(CASE WHEN RiskScore >= 90 THEN 1 ELSE 0 END) AS Critical
            FROM adm_analysis;
        ";

        await using var cmd = new MySqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return new DashboardDto();

        return new DashboardDto
        {
            TotalAnalyzed = Convert.ToInt32(reader["Total"]),
            Low = Convert.ToInt32(reader["Low"]),
            Medium = Convert.ToInt32(reader["Medium"]),
            High = Convert.ToInt32(reader["High"]),
            Critical = Convert.ToInt32(reader["Critical"])
        };
    }
}
