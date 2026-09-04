using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public sealed class QueueActionRepository : IQueueActionRepository
{
    private const int MaxApiRawResponseLength = 8192;
    private readonly IConfiguration _configuration;
    private readonly IConnectionCredentialStore _connectionCredentialStore;
    private readonly ILogger<QueueActionRepository> _logger;
    private string? _connectionString;
    private string? _logConnectionString;

    public QueueActionRepository(
        IConfiguration configuration,
        IConnectionCredentialStore connectionCredentialStore,
        ILogger<QueueActionRepository> logger)
    {
        _configuration = configuration;
        _connectionCredentialStore = connectionCredentialStore;
        _logger = logger;
        RefreshConnectionStrings();
        _connectionCredentialStore.Reloaded += OnConnectionCredentialsReloaded;
        
        // Debug logging for connection strings
        _logger.LogInformation("QueueActionRepository initialized. IsConfigured: {IsConfigured}, IsLogConfigured: {IsLogConfigured}",
            IsConfigured, IsLogConfigured);
    }

    private void OnConnectionCredentialsReloaded(object? sender, EventArgs e) => RefreshConnectionStrings();

    private void RefreshConnectionStrings()
    {
        _connectionString = _connectionCredentialStore.GetConnectionString("TransDBConnection");
           
        _logConnectionString = _connectionCredentialStore.GetConnectionString("LogDBConnection");
        
        _logger.LogInformation("Connection strings refreshed. TransDB Configured: {TransConfigured}, LogDB Configured: {LogConfigured}",
            !string.IsNullOrWhiteSpace(_connectionString),
            !string.IsNullOrWhiteSpace(_logConnectionString));
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_connectionString);
    public bool IsLogConfigured => !string.IsNullOrWhiteSpace(_logConnectionString);

    public async Task<(int Saved, IReadOnlyList<QueueAnalysisResult> ChangedResults)> SaveRecommendedActionsAsync(
        IReadOnlyList<QueueAnalysisResult> analysisResults,
        string uplId = "",
        string providerName = "",
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("MySQL persistence skipped because TransDBConnection is empty.");
            return (0, Array.Empty<QueueAnalysisResult>());
        }

        _logger.LogInformation("Saving {ResultCount} queue results ({ActionCount} actions) for provider {ProviderName}.",
            analysisResults.Count, analysisResults.Sum(result => result.Actions.Count), providerName);

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var saved = 0;
        var changedPnrs = new HashSet<string>();

        foreach (var result in analysisResults)
        {
            foreach (var action in result.Actions)
            {
                var affected = await UpsertActionAsync(connection, result, action, uplId, providerName, cancellationToken);
                // MySQL: INSERT=1, UPDATE=2, no-change=0
                if (affected > 0)
                {
                    saved++;
                    if (action.ShouldNotify)
                        changedPnrs.Add(result.Pnr);
                }
            }
        }

        var changedResults = analysisResults.Where(r => changedPnrs.Contains(r.Pnr)).ToList();
        return (saved, changedResults);
    }

    public async Task<int> MarkPnrsNotInQueueAsync(
        int queueNumber,
        IReadOnlyCollection<string> currentPnrs,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Missing-PNR reconciliation skipped because TransDBConnection is empty.");
            return 0;
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var pnrs = currentPnrs
            .Where(pnr => !string.IsNullOrWhiteSpace(pnr))
            .Select(pnr => pnr.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var parameterNames = pnrs.Select((_, index) => $"@CurrentPnr{index}").ToArray();
        var notInClause = parameterNames.Length == 0
            ? string.Empty
            : $" AND TRIM(UPPER(Pnr)) NOT IN ({string.Join(", ", parameterNames)})";

        var sql = $"""
            UPDATE wptravelitineraryflightqueuereports
            SET ActionTaken = 0,
                Remarks = 'Updated in Sabre CRM - New Test',
                UpdatedAt = CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30')
            WHERE QueueNumber = @QueueNumber
              AND Pnr IS NOT NULL
              {notInClause};
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@QueueNumber", queueNumber);
        for (var index = 0; index < pnrs.Length; index++)
            command.Parameters.AddWithValue(parameterNames[index], pnrs[index]);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveProcessingLogAsync(
        string sourceId,
        string contentHash,
        int pnrCount,
        int actionCount,
        int pccCount,
        string status,
        string message,
        string uplId = "",
        CancellationToken cancellationToken = default)
    {
        if (!IsLogConfigured)
        {
            _logger.LogWarning("MySQL processing log skipped because LogDBConnection is empty.");
            return;
        }

        await using var connection = new MySqlConnection(_logConnectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            INSERT INTO log_flightqueue (
                SourceId,
                ContentHash,
                PnrCount,
                ActionCount,
                PccCount,
                Status,
                Message,
                UplId,
                ProcessedAt,
                Operation,
                Details
            )
            VALUES (
                @SourceId,
                @ContentHash,
                @PnrCount,
                @ActionCount,
                @PccCount,
                @Status,
                @Message,
                @UplId,
                CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30'),
                @Operation,
                @Details
            );
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@SourceId", sourceId);
        command.Parameters.AddWithValue("@ContentHash", contentHash);
        command.Parameters.AddWithValue("@PnrCount", pnrCount);
        command.Parameters.AddWithValue("@ActionCount", actionCount);
        command.Parameters.AddWithValue("@PccCount", pccCount);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@Message", message);
        command.Parameters.AddWithValue("@UplId", uplId);
        command.Parameters.AddWithValue("@Operation", sourceId);
        command.Parameters.AddWithValue("@Details", message);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Returns 1 for insert or update, 0 for no change (skipped).
    /// Inserts if row doesn't exist. Updates if any of these fields changed:
    /// SegmentNumber, Flight, StatusCode, DelayMinutes, Airline, Origin, Destination, DepartureTime, ArrivalTime, DepartureDate
    /// On update: sets ActionTaken = 1 and ReasonText = NULL
    /// </summary>
    private static async Task<int> UpsertActionAsync(
        MySqlConnection connection,
        QueueAnalysisResult result,
        ActionFinding action,
        string uplId,
        string providerName,
        CancellationToken cancellationToken)
    {
        // Check if existing row exists
        const string checkSql = """
            SELECT SegmentNumber, Flight, StatusCode, DelayMinutes, Airline, Origin, Destination, DepartureTime, ArrivalTime, DepartureDate, IsTicketed
            FROM wptravelitineraryflightqueuereports
            WHERE QueueNumber = @QueueNumber
              AND Pnr = @Pnr
              AND SegmentNumber = @SegmentNumber
              AND Flight = @Flight
              AND StatusCode = @StatusCode
            LIMIT 1;
            """;

        bool recordExists = false;
        bool hasChanges = false;

        await using (var check = new MySqlCommand(checkSql, connection))
        {
            check.Parameters.AddWithValue("@QueueNumber", result.Queue);
            check.Parameters.AddWithValue("@Pnr", result.Pnr);
            check.Parameters.AddWithValue("@SegmentNumber", action.Segment);
            check.Parameters.AddWithValue("@Flight", action.Flight);
            check.Parameters.AddWithValue("@StatusCode", action.Status);

            await using var reader = await check.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                recordExists = true;
                
                // Compare the specific fields that matter
                var existingSegment = reader.GetInt32(0);
                var existingFlight = reader.GetString(1);
                var existingStatus = reader.GetString(2);
                var existingDelay = reader.IsDBNull(3) ? (int?)null : reader.GetInt32(3);
                var existingAirline = reader.IsDBNull(4) ? "" : reader.GetString(4);
                var existingOrigin = reader.IsDBNull(5) ? "" : reader.GetString(5);
                var existingDest = reader.IsDBNull(6) ? "" : reader.GetString(6);
                var existingDepTime = reader.IsDBNull(7) ? "" : reader.GetString(7);
                var existingArrTime = reader.IsDBNull(8) ? "" : reader.GetString(8);
                var existingDepDate   = reader.IsDBNull(9)  ? "" : reader.GetString(9);
                var existingTicketed  = !reader.IsDBNull(10) && reader.GetBoolean(10);

                hasChanges = existingSegment   != action.Segment
                    || existingFlight          != action.Flight
                    || existingStatus          != action.Status
                    || existingDelay           != action.DelayMinutes
                    || existingAirline         != (result.Airline ?? "")
                    || existingOrigin          != (action.Origin ?? "")
                    || existingDest            != (action.Destination ?? "")
                    || existingDepTime         != (action.DepartureTime ?? "")
                    || existingArrTime         != (action.ArrivalTime ?? "")
                    || existingDepDate         != (action.DepartureDate ?? "")
                    || existingTicketed        != result.IsTicketed;
            }
        }

        // If row doesn't exist, insert it
        if (!recordExists)
        {
            const string insertSql = """
                INSERT INTO wptravelitineraryflightqueuereports (
                    QueueNumber, Pnr, TransactionId, PCC, ReceivedDateTime, CurrencyCode,
                    SegmentNumber, Flight, StatusCode, ActionText,
                    DelayMinutes, DelayHours, RecommendedFutureCommand, ReasonText,
                    QueueRecommendationJson, Summary, UplId, ProviderName,
                    Origin, Destination, DepartureTime, ArrivalTime, DepartureDate,
                    BaseFare, Taxes, TotalFare, PassengersJson, TicketingDeadline,
                    UpdatedAt, ActionTaken, RemarkEmail, RawResponse, Airline, IsTicketed
                )
                VALUES (
                    @QueueNumber, @Pnr, @TransactionId, @PCC, @ReceivedDateTime, @CurrencyCode,
                    @SegmentNumber, @Flight, @StatusCode, @ActionText,
                    @DelayMinutes, @DelayHours, @RecommendedFutureCommand, @ReasonText,
                    @QueueRecommendationJson, @Summary, @UplId, @ProviderName,
                    @Origin, @Destination, @DepartureTime, @ArrivalTime, @DepartureDate,
                    @BaseFare, @Taxes, @TotalFare, @PassengersJson, @TicketingDeadline,
                    CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30'), @ActionTaken, @RemarkEmail, @RawResponse, @Airline, @IsTicketed
                );
                """;

            await using var insertCmd = new MySqlCommand(insertSql, connection);
            insertCmd.Parameters.AddWithValue("@QueueNumber", result.Queue);
            insertCmd.Parameters.AddWithValue("@Pnr", result.Pnr);
            insertCmd.Parameters.AddWithValue("@TransactionId", result.ReceivedFrom is null ? DBNull.Value : result.ReceivedFrom);
            insertCmd.Parameters.AddWithValue("@PCC", result.PCC is null ? DBNull.Value : result.PCC);
            insertCmd.Parameters.AddWithValue("@ReceivedDateTime", result.ReceivedDateTime is null ? DBNull.Value : result.ReceivedDateTime);
            insertCmd.Parameters.AddWithValue("@CurrencyCode", result.CurrencyCode is null ? DBNull.Value : result.CurrencyCode);
            insertCmd.Parameters.AddWithValue("@SegmentNumber", action.Segment);
            insertCmd.Parameters.AddWithValue("@Flight", action.Flight);
            insertCmd.Parameters.AddWithValue("@StatusCode", action.Status);
            insertCmd.Parameters.AddWithValue("@ActionText", action.Action);
            insertCmd.Parameters.AddWithValue("@DelayMinutes", action.DelayMinutes is null ? DBNull.Value : action.DelayMinutes);
            insertCmd.Parameters.AddWithValue("@DelayHours", action.DelayHours is null ? DBNull.Value : action.DelayHours);
            insertCmd.Parameters.AddWithValue("@RecommendedFutureCommand", action.RecommendedFutureCommand is null ? DBNull.Value : action.RecommendedFutureCommand);
            insertCmd.Parameters.AddWithValue("@ReasonText", DBNull.Value);
            insertCmd.Parameters.AddWithValue("@ActionTaken", action.ShouldNotify ? 1 : 0);
            insertCmd.Parameters.AddWithValue("@QueueRecommendationJson", action.QueueRecommendation is null
                ? DBNull.Value
                : JsonSerializer.Serialize(action.QueueRecommendation));
            insertCmd.Parameters.AddWithValue("@Summary", result.Summary);
            insertCmd.Parameters.AddWithValue("@UplId", uplId);
            insertCmd.Parameters.AddWithValue("@ProviderName", providerName);
            insertCmd.Parameters.AddWithValue("@Origin", action.Origin ?? "");
            insertCmd.Parameters.AddWithValue("@Destination", action.Destination ?? "");
            insertCmd.Parameters.AddWithValue("@DepartureTime", action.DepartureTime ?? "");
            insertCmd.Parameters.AddWithValue("@ArrivalTime", action.ArrivalTime ?? "");
            insertCmd.Parameters.AddWithValue("@DepartureDate", action.DepartureDate ?? "");
            insertCmd.Parameters.AddWithValue("@BaseFare", result.BaseFare is null ? DBNull.Value : result.BaseFare);
            insertCmd.Parameters.AddWithValue("@Taxes", result.Taxes is null ? DBNull.Value : result.Taxes);
            insertCmd.Parameters.AddWithValue("@TotalFare", result.TotalFare is null ? DBNull.Value : result.TotalFare);
            insertCmd.Parameters.AddWithValue("@PassengersJson", result.Passengers is null || result.Passengers.Count == 0
                ? DBNull.Value
                : JsonSerializer.Serialize(result.Passengers));
            insertCmd.Parameters.AddWithValue("@TicketingDeadline", result.TicketingDeadline ?? "");
            insertCmd.Parameters.AddWithValue("@RemarkEmail", result.RemarkEmail ?? "");
            insertCmd.Parameters.AddWithValue("@RawResponse", result.RawResponse is null ? DBNull.Value : result.RawResponse);
            insertCmd.Parameters.AddWithValue("@Airline", result.Airline ?? "");
            insertCmd.Parameters.AddWithValue("@IsTicketed", result.IsTicketed ? 1 : 0);

            return await insertCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // If record exists but no changes, skip
        if (!hasChanges)
        {
            return 0;
        }

        // Record exists and has changes. Preserve ActionTaken for PNRs still present in Sabre.
        const string updateSql = """
            UPDATE wptravelitineraryflightqueuereports
            SET SegmentNumber = @SegmentNumber,
                Flight = @Flight,
                StatusCode = @StatusCode,
                DelayMinutes = @DelayMinutes,
                Airline = @Airline,
                Origin = @Origin,
                Destination = @Destination,
                DepartureTime = @DepartureTime,
                ArrivalTime = @ArrivalTime,
                DepartureDate = @DepartureDate,
                IsTicketed = @IsTicketed,
                ReasonText = NULL,
                UpdatedAt = CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30')
            WHERE QueueNumber = @QueueNumber
              AND Pnr = @Pnr
              AND SegmentNumber = @OldSegmentNumber
              AND Flight = @OldFlight
              AND StatusCode = @OldStatusCode;
            """;

        await using var updateCmd = new MySqlCommand(updateSql, connection);
        updateCmd.Parameters.AddWithValue("@QueueNumber", result.Queue);
        updateCmd.Parameters.AddWithValue("@Pnr", result.Pnr);
        updateCmd.Parameters.AddWithValue("@OldSegmentNumber", action.Segment);
        updateCmd.Parameters.AddWithValue("@OldFlight", action.Flight);
        updateCmd.Parameters.AddWithValue("@OldStatusCode", action.Status);
        updateCmd.Parameters.AddWithValue("@SegmentNumber", action.Segment);
        updateCmd.Parameters.AddWithValue("@Flight", action.Flight);
        updateCmd.Parameters.AddWithValue("@StatusCode", action.Status);
        updateCmd.Parameters.AddWithValue("@DelayMinutes", action.DelayMinutes is null ? DBNull.Value : action.DelayMinutes);
        updateCmd.Parameters.AddWithValue("@Airline", result.Airline ?? "");
        updateCmd.Parameters.AddWithValue("@Origin", action.Origin ?? "");
        updateCmd.Parameters.AddWithValue("@Destination", action.Destination ?? "");
        updateCmd.Parameters.AddWithValue("@DepartureTime", action.DepartureTime ?? "");
        updateCmd.Parameters.AddWithValue("@ArrivalTime", action.ArrivalTime ?? "");
        updateCmd.Parameters.AddWithValue("@DepartureDate", action.DepartureDate ?? "");
        updateCmd.Parameters.AddWithValue("@IsTicketed", result.IsTicketed ? 1 : 0);

        return await updateCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveApiLogAsync(
        string pccCode,
        string serviceName,
        string hostCommand,
        string requestXml,
        string responseXml,
        int httpStatusCode,
        string status,
        string uplId,
        string workFlow = "QueuePolling",
        string moduleName = "SabreQueueMCP",
        string moduleCode = "QUEUE",
        CancellationToken cancellationToken = default)
    {
        if (!IsLogConfigured)
        {
            _logger.LogWarning("⚠ SKIPPING API LOG - LogDBConnection NOT CONFIGURED. Add 'log' credentials to wpset_credentialdetails. Command: {Command}, PCC: {PCC}", 
                hostCommand, pccCode);
            return;
        }

        try
        {
            _logger.LogInformation("→ Logging Sabre API. Command: {Command}, PCC: {PCC}, HTTP: {HttpStatus}", 
                hostCommand, pccCode, httpStatusCode);

            await using var connection = new MySqlConnection(_logConnectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = """
                INSERT INTO wp_xmllog (
                    Log_UPL_VC,
                    Log_WorkFlow_VC,
                    Log_UserID_NB,
                    Log_UserType_VC,
                    Log_CompanyID_NB,
                    Log_CompanyType_VC,
                    Log_ModuleName_VC,
                    Log_ModuleCode_VC,
                    Log_ClassName_VC,
                    Log_ProcedureName_VC,
                    Log_LogCode_VC,
                    Log_LogXML_VC,
                    Log_Remarks_VC,
                    Log_LogDate_DT,
                    Log_AUI_VC,
                    Log_UTL_VC,
                    Log_TransactionID_NB,
                    Log_Date,
                    Log_RegionID_NB
                )
                VALUES (
                    @UplId,
                    @WorkFlow,
                    0,
                    '',
                    0,
                    '',
                    @ModuleName,
                    @ModuleCode,
                    @ClassName,
                    @ProcedureName,
                    @LogCode,
                    @LogXml,
                    @Remarks,
                    CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30'),
                    '',
                    '',
                    0,
                    CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30'),
                    0
                );
                """;

            var logXml = $"<ApiLog><Request><![CDATA[{requestXml}]]></Request><Response><![CDATA[{responseXml}]]></Response></ApiLog>";
            var remarks = $"PCC:{pccCode}|Command:{hostCommand}|HTTP:{httpStatusCode}|Status:{status}";

            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@UplId", uplId);
            cmd.Parameters.AddWithValue("@WorkFlow", workFlow);
            cmd.Parameters.AddWithValue("@ModuleName", moduleName);
            cmd.Parameters.AddWithValue("@ModuleCode", moduleCode);
            cmd.Parameters.AddWithValue("@ClassName", serviceName);
            cmd.Parameters.AddWithValue("@ProcedureName", hostCommand);
            cmd.Parameters.AddWithValue("@LogCode", status);
            cmd.Parameters.AddWithValue("@LogXml", logXml);
            cmd.Parameters.AddWithValue("@Remarks", remarks);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
            
            _logger.LogInformation("✓ Logged: {Command} | {Status}", hostCommand, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "✗ Failed to log API. Command: {Command}, Error: {Error}", hostCommand, ex.Message);
        }
    }

     public async Task<bool> UpdateAgentRemarksAsync(
        string pnr,
        int segmentNumber,
        string flight,
        string statusCode,
        string remarks,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Remarks update skipped because TransDBConnection is empty.");
            return false;
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE wptravelitineraryflightqueuereports
            SET CustomeRemarks = @Remarks,
                CustomerActiontaken = 0,
                UpdatedAt = CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30')
            WHERE Pnr = @Pnr;
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Pnr", pnr);
        command.Parameters.AddWithValue("@Remarks", remarks);
       

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }
    

    public async Task<bool> UpdateRemarksAsync(
        string pnr,
        int segmentNumber,
        string flight,
        string statusCode,
        string remarks,
        int remarkUpdatedBy,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("Remarks update skipped because TransDBConnection is empty.");
            return false;
        }

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            UPDATE wptravelitineraryflightqueuereports
            SET Remarks = @Remarks,
                RemarkUpdatedBy = @RemarkUpdatedBy,
                ActionTaken = 0,
                UpdatedAt = CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30')
            WHERE Pnr = @Pnr;
            """;

        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Pnr", pnr);
        command.Parameters.AddWithValue("@Remarks", remarks);
        command.Parameters.AddWithValue("@RemarkUpdatedBy", remarkUpdatedBy);

        var affected = await command.ExecuteNonQueryAsync(cancellationToken);
        return affected > 0;
    }

    public async Task<PnrDelayAnalysisDto?> GetDelayAnalysisByPnrAsync(string pnr, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return null;

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string sql = """
            SELECT Pnr, TransactionId, SegmentNumber, StatusCode, PCC, Flight, Origin, Destination,
                   DepartureTime, ArrivalTime, DepartureDate, CurrencyCode, RawResponse,
                   BaseFare, Taxes, TotalFare, PassengersJson, TicketingDeadline,
                   DelayMinutes, DelayHours, ActionText, ReasonText, ProviderName, Airline, isTicketed
            FROM wptravelitineraryflightqueuereports
            WHERE Pnr = @Pnr
            GROUP BY SegmentNumber, Flight, StatusCode
            ORDER BY SegmentNumber, Flight, StatusCode;
            """;

        await using var cmd = new MySqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("@Pnr", pnr);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        string? pnrValue = null, agentCode = null, receivedFrom = null, currencyCode = null, ticketingDeadline = null, rawResponse = null;
        List<PnrPassenger>? passengers = null;
        decimal? baseFare = null, taxes = null, totalFare = null;
        var isTicketed = false;
        var segments = new List<PnrSegmentDetailDto>();

        while (await reader.ReadAsync(cancellationToken))
        {
            if (pnrValue is null)
            {
                pnrValue = reader.GetString("Pnr");
                receivedFrom = GetNullableString(reader, "TransactionId");
                agentCode = GetNullableString(reader, "PCC");
                currencyCode = GetNullableString(reader, "CurrencyCode");
                ticketingDeadline = GetNullableString(reader, "TicketingDeadline") ?? "";
                rawResponse = Truncate(GetNullableString(reader, "RawResponse"), MaxApiRawResponseLength);
                baseFare = reader.IsDBNull(reader.GetOrdinal("BaseFare")) ? null : reader.GetDecimal("BaseFare");
                taxes = reader.IsDBNull(reader.GetOrdinal("Taxes")) ? null : reader.GetDecimal("Taxes");
                totalFare = reader.IsDBNull(reader.GetOrdinal("TotalFare")) ? null : reader.GetDecimal("TotalFare");
                isTicketed = !reader.IsDBNull(reader.GetOrdinal("isTicketed"))
                    && Convert.ToBoolean(reader["isTicketed"]);
                var passengersJson = GetNullableString(reader, "PassengersJson");
                if (!string.IsNullOrWhiteSpace(passengersJson))
                    passengers = JsonSerializer.Deserialize<List<PnrPassenger>>(passengersJson);
            }

            segments.Add(new PnrSegmentDetailDto(
                reader.GetInt32("SegmentNumber"),
                reader.GetString("Flight"),
                reader.GetString("StatusCode"),
                GetNullableString(reader, "ProviderName") ?? "",
                GetNullableString(reader, "Origin") ?? "",
                GetNullableString(reader, "Destination") ?? "",
                GetNullableString(reader, "DepartureTime") ?? "",
                GetNullableString(reader, "ArrivalTime") ?? "",
                GetNullableString(reader, "DepartureDate") ?? "",
                reader.IsDBNull(reader.GetOrdinal("DelayMinutes")) ? null : reader.GetInt32("DelayMinutes"),
                reader.IsDBNull(reader.GetOrdinal("DelayHours")) ? null : reader.GetDecimal("DelayHours"),
                GetNullableString(reader, "ActionText") ?? "",
                GetNullableString(reader, "ReasonText") ?? ""));
        }

        if (segments.Count == 0) return null;

        return new PnrDelayAnalysisDto(
            pnrValue!, receivedFrom, agentCode, currencyCode, rawResponse,
            segments, new PnrFareSummaryDto(baseFare, taxes, totalFare),
            passengers, ticketingDeadline, isTicketed);
    }

    private static string? GetNullableString(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;

        return value[..maxLength];
    }
}
