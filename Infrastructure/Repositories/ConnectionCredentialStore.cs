using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;
using System.Globalization;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public sealed class ConnectionCredentialStore : IConnectionCredentialStore
{
    private readonly string? _connectionString;
    private readonly ILogger<ConnectionCredentialStore> _logger;
    private readonly IServiceProvider _serviceProvider;
    private List<ConnectionCredential> _credentials = new List<ConnectionCredential>();

    public event EventHandler? Reloaded;

    public ConnectionCredentialStore(IConfiguration configuration, ILogger<ConnectionCredentialStore> logger, IServiceProvider serviceProvider)
    {
        _connectionString = configuration.GetConnectionString("SkyOpsDBconnection");
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);

    public IReadOnlyList<ConnectionCredential> GetConnectionCredentials() => _credentials;

    public IReadOnlyList<ConnectionCredential> GetByPcc(string pccCode) =>
        _credentials.Where(c => c.PCCMasterCode.Equals(pccCode, StringComparison.OrdinalIgnoreCase)).ToList();

    public string? GetConnectionString(string name)
    {
        var groupName = ResolveConnectionGroupName(name);
        var values = _credentials
            .Where(c => c.PCCMasterCode.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.TagName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().TagValue, StringComparer.OrdinalIgnoreCase);

        if (values.Count == 0)
        {
            return null;
        }

        var requiredKeys = new[] { "server", "database", "user", "password" };
        if (requiredKeys.Any(key => !values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)))
        {
            _logger.LogWarning("Connection group {GroupName} is missing one or more required values.", groupName);
            return null;
        }

        var parts = new List<string>
        {
            $"server={values["server"]}",
            $"database={values["database"]}",
            $"user={values["user"]}",
            $"password={values["password"]}"
        };

        if (values.TryGetValue("port", out var port) && !string.IsNullOrWhiteSpace(port))
        {
            parts.Insert(1, $"port={port}");
        }

        return string.Join(';', parts) + ";";
    }


    public string? GetTagValue(string pccCode, string tagName) =>
        _credentials.FirstOrDefault(c =>
            c.PCCMasterCode.Equals(pccCode, StringComparison.OrdinalIgnoreCase) &&
            c.TagName.Equals(tagName, StringComparison.OrdinalIgnoreCase))?.TagValue;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Connection credentials not loaded: ConnectionStrings:SkyOpsDBconnection is empty.");
            return;
        }

        try
        {
            await using var connection = new MySqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                SELECT * FROM skyops.wpset_credentialdetails
                WHERE RecordStatus = '0'
                  AND UPPER(ServiceType) = 'CON';
                """;

            await using var cmd = new MySqlCommand(sql, connection);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

            var list = new List<ConnectionCredential>();

            while (await reader.ReadAsync(cancellationToken))
            {
                list.Add(new ConnectionCredential
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
            _logger.LogInformation("Loaded {Count} connection credential rows from skyops.wpset_credentialdetails.", list.Count);
            Reloaded?.Invoke(this, EventArgs.Empty);
            await LogSkyOpsDbUsageAsync("LoadAsync", "Success", $"Loaded {list.Count} rows", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load connection credentials from skyops.wpset_credentialdetails.");
            await LogSkyOpsDbUsageAsync("LoadAsync", "Failed", ex.Message, cancellationToken);
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
                await errorLogService.LogAsync(ex, "CredentialStore", "SkyOpsQueueIntelligence", "STORE", nameof(LoadAsync), nameof(ConnectionCredentialStore), null, ct);
        }
        catch { /* non-critical */ }
    }

    private async Task LogSkyOpsDbUsageAsync(string operation, string status, string? details, CancellationToken ct)
    {
        try
        {
            var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "log_SkyOpsConnectionCredentials.log");
            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} | {operation} | {status} | {details}{Environment.NewLine}";
            await File.AppendAllTextAsync(logFile, line, ct);
            _logger.LogInformation("SkyOps connection credential log written to: {Path}", logFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write log_SkyOpsConnectionCredentials log file.");
        }
    }

    private static string GetStringOrEmpty(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        if (reader.IsDBNull(ordinal))
        {
            return string.Empty;
        }

        var value = reader.GetValue(ordinal);
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
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
            double d => Convert.ToInt32(d, CultureInfo.InvariantCulture),
            float f => Convert.ToInt32(f, CultureInfo.InvariantCulture),
            _ => int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0
        };
    }

    private static string ResolveConnectionGroupName(string name) =>
        name.ToUpperInvariant() switch
        {
            "TRANSDBCONNECTION" => "transaction",
            "LOGDBCONNECTION" => "log",
            "MASTERDBCONNECTION" => "master",
            "SKYOPSDBCONNECTION" => "master",
            _ => name
        };
}
