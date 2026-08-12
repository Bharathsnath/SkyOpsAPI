using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public sealed class SettingsRepository : ISettingsRepository
{
    private readonly string? _connectionString;
    private readonly IConnectionCredentialStore _credentialStore;
    private readonly ILogger<SettingsRepository> _logger;

    public SettingsRepository(IConfiguration configuration, IConnectionCredentialStore credentialStore, ILogger<SettingsRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("SkyOpsDBconnection");
        _credentialStore = credentialStore;
        _logger = logger;
    }

    private string? MasterDbConnectionString => _credentialStore.GetConnectionString("MasterDBConnection") ?? _connectionString;

    public async Task<IReadOnlyList<AppConfiguration>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = "SELECT Id, Category, ConfigKey, ConfigValue, ProviderName, IsEnabled, IsActive, CreatedDate, ModifiedUser, ModifiedDate FROM AppConfigurations WHERE IsActive = 1 ORDER BY Category, ConfigKey";
        return await QueryAsync(conn, sql, ct);
        
    }

    public async Task<IReadOnlyList<AppConfiguration>> GetByCategoryAsync(string category, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = "SELECT Id, Category, ConfigKey, ConfigValue, ProviderName, IsEnabled, IsActive, CreatedDate, ModifiedUser, ModifiedDate FROM AppConfigurations WHERE Category = @Category AND IsActive = 1 ORDER BY ConfigKey";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Category", category);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<AppConfiguration?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = "SELECT Id, Category, ConfigKey, ConfigValue, ProviderName, IsEnabled, IsActive, CreatedDate, ModifiedUser, ModifiedDate FROM AppConfigurations WHERE Id = @Id";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    

    public async Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            SELECT Id, PCCCode, Emails, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
            FROM skyops.pccagentemailmaster
            ORDER BY PCCCode, Emails
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        return await ReadPccAgentEmailMasterListAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersByPccAsync(string pccCode, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            SELECT Id, PCCCode, Emails, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
            FROM skyops.pccagentemailmaster
            WHERE PCCCode LIKE @PccCode
            ORDER BY Emails
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@PccCode", $"%{pccCode}%");
        return await ReadPccAgentEmailMasterListAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersByPccsAsync(IEnumerable<string> pccCodes, CancellationToken ct = default)
    {
        var codes = pccCodes?.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? new List<string>();
        if (codes.Count == 0) return Array.Empty<PccAgentEmailMaster>();

        await using var conn = await OpenAsync(ct);
        var placeholders = string.Join(",", codes.Select((_, i) => $"@Pcc{i}"));
        var sql = $"""
            SELECT Id, PCCCode, Emails, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
            FROM skyops.pccagentemailmaster
            WHERE PCCCode IN ({placeholders})
            ORDER BY PCCCode, Emails
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        for (int i = 0; i < codes.Count; i++)
        {
            cmd.Parameters.AddWithValue($"@Pcc{i}", codes[i].Trim());
        }
        return await ReadPccAgentEmailMasterListAsync(cmd, ct);
    }

    public async Task<long> CreatePccAgentEmailMasterAsync(PccAgentEmailMaster entry, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var indianNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));

        const string sql = """
            INSERT INTO skyops.pccagentemailmaster
            (PCCCode, Emails, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate)
            VALUES
            (@PCCCode, @Emails, @IsActive, @CreatedBy, @IndianNow, @ModifiedBy, @IndianNow)
            ON DUPLICATE KEY UPDATE
                Emails = VALUES(Emails),
                IsActive = VALUES(IsActive),
                CreatedBy = VALUES(CreatedBy),
                ModifiedBy = VALUES(ModifiedBy),
                ModifiedDate = VALUES(ModifiedDate)
            """;

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@PCCCode", entry.PCCCode);
        cmd.Parameters.AddWithValue("@Emails", entry.Emails);
        cmd.Parameters.AddWithValue("@IsActive", entry.IsActive);
        cmd.Parameters.AddWithValue("@CreatedBy", entry.CreatedBy);
        cmd.Parameters.AddWithValue("@ModifiedBy", entry.ModifiedBy);
        cmd.Parameters.AddWithValue("@IndianNow", indianNow);
        await cmd.ExecuteNonQueryAsync(ct);
        return cmd.LastInsertedId;
    }

    public async Task<bool> UpdateAsync(AppConfiguration config, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            UPDATE AppConfigurations
            SET Category = @Category, ConfigKey = @ConfigKey, ConfigValue = @ConfigValue,
                ProviderName = @ProviderName, IsEnabled = @IsEnabled, ModifiedUser = @ModifiedUser, ModifiedDate = NOW()
            WHERE Id = @Id
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", config.Id);
        cmd.Parameters.AddWithValue("@Category", config.Category);
        cmd.Parameters.AddWithValue("@ConfigKey", config.ConfigKey);
        cmd.Parameters.AddWithValue("@ConfigValue", config.ConfigValue);
        cmd.Parameters.AddWithValue("@ProviderName", (object?)config.ProviderName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsEnabled", config.IsEnabled);
        cmd.Parameters.AddWithValue("@ModifiedUser", (object?)config.ModifiedUser ?? DBNull.Value);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = "UPDATE AppConfigurations SET IsActive = 0, ModifiedDate = NOW() WHERE Id = @Id";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<List<AppConfiguration>> GetLoggingConfigurationsAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            SELECT Id, Category, ConfigKey, ConfigValue, ProviderName, IsEnabled
            FROM AppConfigurations
            WHERE Category = @Category AND IsActive = 1
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Category", "Logging");
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<AppConfiguration>();
        while (await reader.ReadAsync(ct))
            list.Add(new AppConfiguration
            {
                Id = reader.IsDBNull(reader.GetOrdinal("Id")) ? 0 : reader.GetInt64("Id"),
                Category = reader.GetString("Category"),
                ConfigKey = reader.GetString("ConfigKey"),
                ConfigValue = reader.IsDBNull(reader.GetOrdinal("ConfigValue")) ? string.Empty : reader.GetString("ConfigValue"),
                ProviderName = reader.IsDBNull(reader.GetOrdinal("ProviderName")) ? null : reader.GetString("ProviderName"),
                IsEnabled = reader.IsDBNull(reader.GetOrdinal("IsEnabled")) ? false : reader.GetBoolean("IsEnabled")
            });
        return list;
    }

    public async Task UpdateConfigurationAsync(string configKey, bool enabled, int modifiedUser, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            UPDATE AppConfigurations
            SET ConfigValue = @ConfigValue, IsEnabled = @IsEnabled, ModifiedUser = @ModifiedUser, ModifiedDate = NOW()
            WHERE Category = @Category AND ConfigKey = @ConfigKey AND IsActive = 1
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Category", "Logging");
        cmd.Parameters.AddWithValue("@ConfigKey", configKey);
        cmd.Parameters.AddWithValue("@ConfigValue", enabled ? "true" : "false");
        cmd.Parameters.AddWithValue("@IsEnabled", enabled);
        cmd.Parameters.AddWithValue("@ModifiedUser", modifiedUser);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> CreateAppConfigurationAsync(AppConfiguration config, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            INSERT INTO AppConfigurations (Category, ConfigKey, ConfigValue, ProviderName, IsEnabled, IsActive, CreatedDate, ModifiedUser, ModifiedDate)
            VALUES (@Category, @ConfigKey, @ConfigValue, @ProviderName, @IsEnabled, 1, NOW(), @ModifiedUser, NOW());
            SELECT LAST_INSERT_ID();
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Category", config.Category);
        cmd.Parameters.AddWithValue("@ConfigKey", config.ConfigKey);
        cmd.Parameters.AddWithValue("@ConfigValue", config.ConfigValue);
        cmd.Parameters.AddWithValue("@ProviderName", (object?)config.ProviderName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@IsEnabled", config.IsEnabled);
        cmd.Parameters.AddWithValue("@ModifiedUser", (object?)config.ModifiedUser ?? DBNull.Value);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task UpdateConnectionTagAsync(long credId, string tagName, string tagValue, int modifiedUser, bool isEnabled, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            INSERT INTO skyops.wpset_credentialdetails (Cred_ID, TagName, TagValue, RecordStatus, ModifiedUser, ModifiedDate)
            VALUES (@CredId, @TagName, @TagValue, @RecordStatus, @ModifiedUser, @IndianNow)
            ON DUPLICATE KEY UPDATE TagName = @TagName, TagValue = @TagValue, ModifiedUser = @ModifiedUser, RecordStatus = @RecordStatus, ModifiedDate = @IndianNow
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        var indianNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        cmd.Parameters.AddWithValue("@IndianNow", indianNow);
        cmd.Parameters.AddWithValue("@CredId", credId);
        cmd.Parameters.AddWithValue("@TagName", tagName);
        cmd.Parameters.AddWithValue("@TagValue", tagValue);
        cmd.Parameters.AddWithValue("@RecordStatus", isEnabled ? 0 : 1);
        cmd.Parameters.AddWithValue("@ModifiedUser", modifiedUser);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PccCredential>> GetPccCredentialsAsync(CancellationToken ct = default)
    {
        var credentials = new List<PccCredential>();

        credentials.AddRange(await ReadCredentialsFromConnectionAsync(
            OpenMasterAsync,
            "connection-credentials DB",
            ct));

       
        return credentials
            .OrderBy(c => c.PCCMasterCode)
            .ThenBy(c => c.TagName)
            .ThenBy(c => c.Cred_ID)
            .ToList();
    }

    public async Task<IReadOnlyList<PccCredential>> GetPccCredentialsByPccAsync(string pccCode, CancellationToken ct = default)
    {
        var credentials = new List<PccCredential>();

        credentials.AddRange(await ReadCredentialsFromConnectionAsync(
            OpenMasterAsync,
            "connection-credentials DB",
            ct,
            pccCode));

        

        return credentials
            .OrderBy(c => c.PCCMasterCode)
            .ThenBy(c => c.TagName)
            .ThenBy(c => c.Cred_ID)
            .ToList();
    }

    public async Task<IReadOnlyList<PccListEntry>> GetPccListAsync(CancellationToken ct = default)
    {
        var results = new List<PccListEntry>();

        foreach (var openConn in new Func<CancellationToken, Task<MySqlConnection>>[] { OpenMasterAsync, OpenAsync })
        {
            await using var conn = await openConn(ct);
            const string sql = """
                SELECT DISTINCT Provider, TagValue FROM wpset_credentialdetails
                WHERE TagName = 'SourceOffice' AND Provider = 'AB' AND RecordStatus = 0
                """;
            await using var cmd = new MySqlCommand(sql, conn);
            results.AddRange(await ReadPccListAsync(cmd, ct));
        }

        return results
            .DistinctBy(x => (x.Provider, x.TagValue))
            .OrderBy(x => x.TagValue)
            .ToList();
    }

    private static async Task<IReadOnlyList<PccListEntry>> ReadPccListAsync(MySqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<PccListEntry>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PccListEntry
            {
                Provider = reader.IsDBNull(reader.GetOrdinal("Provider")) ? string.Empty : reader.GetString("Provider"),
                TagValue = reader.IsDBNull(reader.GetOrdinal("TagValue")) ? string.Empty : reader.GetString("TagValue")
            });
        }
        return list;
    }

    private async Task<IReadOnlyList<PccCredential>> ReadCredentialsFromConnectionAsync(
        Func<CancellationToken, Task<MySqlConnection>> openConnectionAsync,
        string sourceName,
        CancellationToken ct,
        string? pccCode = null)
    {
        try
        {
            await using var conn = await openConnectionAsync(ct);
            var sql = """
                SELECT Cred_ID, PCCMasterCode, Provider, ServiceType, SectorType, TagName, TagValue, RecordStatus
                FROM wpset_credentialdetails
                WHERE RecordStatus = '0' AND Provider = 'AB'
                """;

            if (!string.IsNullOrWhiteSpace(pccCode))
            {
                sql += " AND PCCMasterCode LIKE @PccCode";
            }

            sql += " ORDER BY PCCMasterCode, TagName";

            await using var cmd = new MySqlCommand(sql, conn);
            if (!string.IsNullOrWhiteSpace(pccCode))
            {
                cmd.Parameters.AddWithValue("@PccCode", $"%{pccCode}%");
            }


            var list = await ReadCredentialListAsync(cmd, ct);
            _logger.LogInformation("Loaded {Count} PCC credentials from {Source}.", list.Count, sourceName);
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load PCC credentials from {Source}.", sourceName);
            return Array.Empty<PccCredential>();
        }
    }

    private static async Task<IReadOnlyList<PccCredential>> ReadCredentialListAsync(MySqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<PccCredential>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PccCredential
            {
                Cred_ID = reader.GetInt64("Cred_ID"),
                PCCMasterCode = reader.IsDBNull(reader.GetOrdinal("PCCMasterCode")) ? string.Empty : reader.GetString("PCCMasterCode"),
                Provider = reader.IsDBNull(reader.GetOrdinal("Provider")) ? string.Empty : reader.GetString("Provider"),
                ServiceType = reader.IsDBNull(reader.GetOrdinal("ServiceType")) ? string.Empty : reader.GetString("ServiceType"),
                SectorType = reader.IsDBNull(reader.GetOrdinal("SectorType")) ? string.Empty : reader.GetString("SectorType"),
                TagName = reader.IsDBNull(reader.GetOrdinal("TagName")) ? string.Empty : reader.GetString("TagName"),
                TagValue = reader.IsDBNull(reader.GetOrdinal("TagValue")) ? string.Empty : reader.GetString("TagValue"),
                RecordStatus = reader.IsDBNull(reader.GetOrdinal("RecordStatus")) ? 0 : reader.GetInt32("RecordStatus")
            });
        }
        return list;
    }

    public async Task<long> CreatePccCredentialAsync(PccCredential credential, CancellationToken ct = default)
    {
        await using var conn = await OpenMasterAsync(ct);
        var indianNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        const string sql = """
            INSERT INTO wpset_credentialdetails
            (PCCMasterCode, Provider, ServiceType, SectorType, TagName, TagValue, RecordStatus,
             CreatedUser, CreatedDate, ModifiedUser, ModifiedDate, AirlineCurrencyCode)
            VALUES
            (@PCCMasterCode, @Provider, @ServiceType, @SectorType, @TagName, @TagValue, @RecordStatus,
             @CreatedUser, @IndianNow, @ModifiedUser, @IndianNow,  @AirlineCurrencyCode);
            SELECT LAST_INSERT_ID();
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@IndianNow", indianNow);
        cmd.Parameters.AddWithValue("@PCCMasterCode", credential.PCCMasterCode);
        cmd.Parameters.AddWithValue("@Provider", credential.Provider);
        cmd.Parameters.AddWithValue("@ServiceType", credential.ServiceType);
        cmd.Parameters.AddWithValue("@SectorType", credential.SectorType);
        cmd.Parameters.AddWithValue("@TagName", credential.TagName);
        cmd.Parameters.AddWithValue("@TagValue", credential.TagValue);
        cmd.Parameters.AddWithValue("@RecordStatus", credential.RecordStatus);
        cmd.Parameters.AddWithValue("@CreatedUser", credential.CreatedUser);
        cmd.Parameters.AddWithValue("@ModifiedUser", credential.ModifiedUser);
        cmd.Parameters.AddWithValue("@AirlineCurrencyCode", credential.AirlineCurrencyCode);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }
    public async Task<long> CreatePccCredentialSkyopsAsync(PccCredential credential, CancellationToken ct = default)
    {
        await using var conn = await OpenMasterAsync(ct);
        var indianNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        const string sql = """
            INSERT INTO skyops.wpset_credentialdetails
            (PCCMasterCode, Provider, ServiceType, SectorType, TagName, TagValue, RecordStatus,
             CreatedUser, CreatedDate, ModifiedUser, ModifiedDate, AirlineCurrencyCode)
            VALUES
            (@PCCMasterCode, @Provider, @ServiceType, @SectorType, @TagName, @TagValue, @RecordStatus,
             @CreatedUser, @IndianNow, @ModifiedUser, @IndianNow,  @AirlineCurrencyCode);
            SELECT LAST_INSERT_ID();
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@IndianNow", indianNow);
        cmd.Parameters.AddWithValue("@PCCMasterCode", credential.PCCMasterCode);
        cmd.Parameters.AddWithValue("@Provider", credential.Provider);
        cmd.Parameters.AddWithValue("@ServiceType", credential.ServiceType);
        cmd.Parameters.AddWithValue("@SectorType", credential.SectorType);
        cmd.Parameters.AddWithValue("@TagName", credential.TagName);
        cmd.Parameters.AddWithValue("@TagValue", credential.TagValue);
        cmd.Parameters.AddWithValue("@RecordStatus", credential.RecordStatus);
        cmd.Parameters.AddWithValue("@CreatedUser", credential.CreatedUser);
        cmd.Parameters.AddWithValue("@ModifiedUser", credential.ModifiedUser);
        cmd.Parameters.AddWithValue("@AirlineCurrencyCode", credential.AirlineCurrencyCode);
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<bool> UpdatePccCredentialAsync(long credId, PccCredential credential, CancellationToken ct = default)
    {
        var indianNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        var preferredSource = NormalizeSourceDb(credential.SourceDb);
        if (preferredSource == "skyops")
        {
            var skyopsUpdated = await UpdatePccCredentialOnConnectionAsync(
                credId,
                credential,
                indianNow,
                OpenAsync,
                ct);

            if (skyopsUpdated)
            {
                return true;
            }

            _logger.LogInformation("PCC credential {CredId} was not found in SkyOps DB. Falling back to connection-credentials DB.", credId);
            return await UpdatePccCredentialOnConnectionAsync(credId, credential, indianNow, OpenMasterAsync, ct);
        }

        if (preferredSource == "master" || preferredSource == "connection")
        {
            var masterUpdated = await UpdatePccCredentialOnConnectionAsync(
                credId,
                credential,
                indianNow,
                OpenMasterAsync,
                ct);

            if (masterUpdated)
            {
                return true;
            }

            _logger.LogInformation("PCC credential {CredId} was not found in the connection-credentials DB. Falling back to SkyOps DB.", credId);
            return await UpdatePccCredentialOnConnectionAsync(credId, credential, indianNow, OpenAsync, ct);
        }

        var updated = await UpdatePccCredentialOnConnectionAsync(credId, credential, indianNow, OpenAsync, ct);
        if (updated)
        {
            return true;
        }

        _logger.LogInformation("PCC credential {CredId} was not found in SkyOps DB. Falling back to connection-credentials DB.", credId);
        return await UpdatePccCredentialOnConnectionAsync(credId, credential, indianNow, OpenMasterAsync, ct);
    }

    public async Task<bool> SetPccCredentialStatusAsync(long credId, int recordStatus, int modifiedUser, CancellationToken ct = default)
    {
        await using var conn = await OpenMasterAsync(ct);
        var indianNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));
        const string sql = "UPDATE wpset_credentialdetails SET RecordStatus = @Status, ModifiedUser = @ModifiedUser, ModifiedDate = @IndianNow WHERE Cred_ID = @CredId";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@CredId", credId);
        cmd.Parameters.AddWithValue("@Status", recordStatus);
        cmd.Parameters.AddWithValue("@ModifiedUser", modifiedUser);
        cmd.Parameters.AddWithValue("@IndianNow", indianNow);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private async Task<MySqlConnection> OpenMasterAsync(CancellationToken ct)
    {
        var connStr = MasterDbConnectionString ?? throw new InvalidOperationException("Master DB connection not configured.");
        var conn = new MySqlConnection(connStr);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static string NormalizeSourceDb(string? sourceDb) =>
        string.IsNullOrWhiteSpace(sourceDb)
            ? string.Empty
            : sourceDb.Trim().ToLowerInvariant() switch
            {
                "skyops" => "skyops",
                "skyops db" => "skyops",
                "skyopsdb" => "skyops",
                "master" => "master",
                "master db" => "master",
                "connection" => "connection",
                "connection db" => "connection",
                "connection-credentials db" => "connection",
                "connectioncredentials db" => "connection",
                _ => string.Empty
            };

    private async Task<bool> UpdatePccCredentialOnConnectionAsync(
        long credId,
        PccCredential credential,
        DateTime indianNow,
        Func<CancellationToken, Task<MySqlConnection>> openConnectionAsync,
        CancellationToken ct)
    {
        try
        {
            await using var conn = await openConnectionAsync(ct);
            const string sql = """
                UPDATE wpset_credentialdetails
                SET PCCMasterCode = @PCCMasterCode, Provider = @Provider, ServiceType = @ServiceType,
                    SectorType = @SectorType, TagName = @TagName, TagValue = @TagValue,
                    ModifiedUser = @ModifiedUser, ModifiedDate = @IndianNow
                WHERE Cred_ID = @CredId
                """;
            await using var cmd = new MySqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@CredId", credId);
            cmd.Parameters.AddWithValue("@PCCMasterCode", credential.PCCMasterCode);
            cmd.Parameters.AddWithValue("@Provider", credential.Provider);
            cmd.Parameters.AddWithValue("@ServiceType", credential.ServiceType);
            cmd.Parameters.AddWithValue("@SectorType", credential.SectorType);
            cmd.Parameters.AddWithValue("@TagName", credential.TagName);
            cmd.Parameters.AddWithValue("@TagValue", credential.TagValue);
            cmd.Parameters.AddWithValue("@ModifiedUser", credential.ModifiedUser);
            cmd.Parameters.AddWithValue("@IndianNow", indianNow);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update PCC credential {CredId} on the primary connection.", credId);
            return false;
        }
    }

    private async Task<IReadOnlyList<AppConfiguration>> QueryAsync(MySqlConnection conn, string sql, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand(sql, conn);
        return await ReadListAsync(cmd, ct);
    }

    private static async Task<IReadOnlyList<AppConfiguration>> ReadListAsync(MySqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<AppConfiguration>();
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    private static async Task<IReadOnlyList<PccAgentEmailMaster>> ReadPccAgentEmailMasterListAsync(MySqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<PccAgentEmailMaster>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new PccAgentEmailMaster
            {
                Id = reader.GetInt64("Id"),
                PccValue = reader.IsDBNull(reader.GetOrdinal("PCCCode")) ? string.Empty : reader.GetString("PCCCode"),
                Emails = reader.IsDBNull(reader.GetOrdinal("Emails")) ? string.Empty : reader.GetString("Emails"),
                IsActive = reader.IsDBNull(reader.GetOrdinal("IsActive")) ? 0 : reader.GetInt32("IsActive"),
                CreatedBy = reader.IsDBNull(reader.GetOrdinal("CreatedBy")) ? string.Empty : reader.GetString("CreatedBy"),
                CreatedDate = reader.IsDBNull(reader.GetOrdinal("CreatedDate")) ? null : reader.GetDateTime("CreatedDate"),
                ModifiedBy = reader.IsDBNull(reader.GetOrdinal("ModifiedBy")) ? string.Empty : reader.GetString("ModifiedBy"),
                ModifiedDate = reader.IsDBNull(reader.GetOrdinal("ModifiedDate")) ? null : reader.GetDateTime("ModifiedDate")
            });
        }
        return list;
    }

    private static AppConfiguration Map(MySqlDataReader r) => new()
    {
        Id = r.GetInt64("Id"),
        Category = r.GetString("Category"),
        ConfigKey = r.GetString("ConfigKey"),
        ConfigValue = r.GetString("ConfigValue"),
        ProviderName = r.IsDBNull(r.GetOrdinal("ProviderName")) ? null : r.GetString("ProviderName"),
        IsEnabled = r.GetBoolean("IsEnabled"),
        IsActive = r.GetBoolean("IsActive"),
        CreatedDate = r.GetDateTime("CreatedDate"),
        ModifiedUser = r.IsDBNull(r.GetOrdinal("ModifiedUser")) ? null : r.GetInt32("ModifiedUser"),
        ModifiedDate = r.IsDBNull(r.GetOrdinal("ModifiedDate")) ? null : r.GetDateTime("ModifiedDate")
    };


}
