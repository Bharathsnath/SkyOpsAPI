using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public sealed class CredentialStore : ICredentialStore
{
    private readonly string? _connectionString;
    private readonly ILogger<CredentialStore> _logger;
    private readonly IServiceProvider _serviceProvider;
    private List<StorePccCredential> _credentials = new List<StorePccCredential>();

    public CredentialStore(
        IConfiguration configuration,
        IConnectionCredentialStore connectionCredentialStore,
        ILogger<CredentialStore> logger,
        IServiceProvider serviceProvider)
    {
        _connectionString = connectionCredentialStore.GetConnectionString("masterDBconnection")
            ?? configuration.GetConnectionString("masterDBconnection");
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public IReadOnlyList<StorePccCredential> GetAll() => _credentials;

    public IReadOnlyList<StorePccCredential> GetByPcc(string pccCode) =>
        _credentials.Where(c => c.PCCMasterCode.Equals(pccCode, StringComparison.OrdinalIgnoreCase)).ToList();

    public string? GetTagValue(string pccCode, string tagName) =>
        _credentials.FirstOrDefault(c =>
            c.PCCMasterCode.Equals(pccCode, StringComparison.OrdinalIgnoreCase) &&
            c.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase))?.TagValue;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("PCC credentials not loaded: ConnectionStrings:masterDBconnection is empty.");
            return;
        }

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                 SELECT * FROM wpset_credentialdetails
                WHERE RecordStatus = '0' 
                AND Provider IN ('AB', 'SB')
                AND (PCCMasterCode LIKE '%AB_1V08_COCHINTDESK_DOM'
                    OR PCCMasterCode LIKE '%AB_1VZ8_PONNANITDESK_DOM'
                    OR PCCMasterCode LIKE '%HO PCC'
                    OR PCCMasterCode LIKE '%1SKSAONLINE%');
                """;

            await using var cmd = new MySqlCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var list = new List<StorePccCredential>();

            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new StorePccCredential
                {
                    Cred_ID = reader.GetInt64("Cred_ID"),
                    PCCMasterCode = GetStringOrEmpty(reader, "PCCMasterCode"),
                    Provider = GetStringOrEmpty(reader, "Provider"),
                    ServiceType = GetStringOrEmpty(reader, "ServiceType"),
                    SectorType = GetStringOrEmpty(reader, "SectorType"),
                    TagName = GetStringOrEmpty(reader, "TagName"),
                    TagValue = GetStringOrEmpty(reader, "TagValue"),
                    RecordStatus = GetIntOrDefault(reader, "RecordStatus"),
                    CreatedUser = GetIntOrDefault(reader, "CreatedUser"),
                    CreatedDate = reader.IsDBNull(reader.GetOrdinal("CreatedDate")) ? null : reader.GetDateTime("CreatedDate"),
                    ModifiedUser = GetIntOrDefault(reader, "ModifiedUser"),
                    ModifiedDate = reader.IsDBNull(reader.GetOrdinal("ModifiedDate")) ? null : reader.GetDateTime("ModifiedDate"),
                    AirlineCurrencyCode = GetStringOrEmpty(reader, "AirlineCurrencyCode")
                });
            }

            _credentials = list;
            _logger.LogInformation("Loaded {Count} PCC credential rows from master table.", list.Count);
            await LogMasterDbUsageAsync("LoadAsync", "Success", $"Loaded {list.Count} rows", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load PCC credentials from master table.");
            await LogMasterDbUsageAsync("LoadAsync", "Failed", ex.Message, cancellationToken);
            await TryLogToDbAsync(ex, cancellationToken);
        }
    }

    private async Task TryLogToDbAsync(Exception ex, CancellationToken ct)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var errorLogService = scope.ServiceProvider.GetService<IErrorLogService>();
            if (errorLogService is not null)
                await errorLogService.LogAsync(ex, "CredentialStore", "SkyOpsQueueIntelligence", "STORE", nameof(LoadAsync), nameof(CredentialStore), null, ct);
        }
        catch { /* non-critical */ }
    }

    private async Task LogMasterDbUsageAsync(string operation, string status, string? details, CancellationToken ct)
    {
        try
        {
            var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "log_MasterDB.log");
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} | {operation} | {status} | {details}{Environment.NewLine}";
            await File.AppendAllTextAsync(logFile, line, ct);
            _logger.LogInformation("MasterDB log written to: {Path}", logFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write log_MasterDB log file.");
        }
    }

    private static string GetStringOrEmpty(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetString(ordinal);
    }

    private static int GetIntOrDefault(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return 0;
        }

        var value = reader.GetValue(ordinal);
        return value switch
        {
            int i => i,
            long l => checked((int)l),
            short s => s,
            byte b => b,
            bool bo => bo ? 1 : 0,
            decimal d => decimal.ToInt32(d),
            double d => Convert.ToInt32(d, System.Globalization.CultureInfo.InvariantCulture),
            float f => Convert.ToInt32(f, System.Globalization.CultureInfo.InvariantCulture),
            string s when int.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => int.TryParse(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var fallback)
                ? fallback
                : 0
        };
    }
}
