using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Services;

public sealed class ErrorLogService : IErrorLogService
{
    private readonly IConfiguration _configuration;
    private readonly IConnectionCredentialStore _connectionCredentialStore;
    private readonly ILogger<ErrorLogService> _logger;

    public ErrorLogService(
        IConfiguration configuration,
        IConnectionCredentialStore connectionCredentialStore,
        ILogger<ErrorLogService> logger)
    {
        _configuration = configuration;
        _connectionCredentialStore = connectionCredentialStore;
        _logger = logger;
    }

    public async Task LogAsync(
        Exception exception,
        string workflow,
        string moduleName,
        string moduleCode,
        string? procedureName = null,
        string? className = null,
        HttpContext? httpContext = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var logConnectionString = ResolveLogConnectionString();

            if (string.IsNullOrWhiteSpace(logConnectionString))
            {
                _logger.LogWarning("Skipping error log persistence because no log database connection is configured.");
                return;
            }

            await using var connection = new MySqlConnection(logConnectionString);
            await connection.OpenAsync(cancellationToken);
            

            const string sql = """
                INSERT INTO log_errorlog (
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
                    Log_ErrorCode_VC,
                    Log_Remarks_VC,
                    Log_LogDate_DT,
                    Log_AUI_VC,
                    Log_UTL_VC,
                    Log_TransactionID_NB,
                    Log_AirlineCodes_VC,
                    Log_Level_VC,
                    Log_ErrorTypeID_NB,
                    Log_IPDetails_VC,
                    Log_SessionID_VC,
                    Log_ValidatingAirlineCode_VC
                ) VALUES (
                    @Upl,
                    @Workflow,
                    @UserId,
                    @UserType,
                    @CompanyId,
                    @CompanyType,
                    @ModuleName,
                    @ModuleCode,
                    @ClassName,
                    @ProcedureName,
                    @ErrorCode,
                    @Remarks,
                    CONVERT_TZ(UTC_TIMESTAMP(), '+00:00', '+05:30'),
                    @Aui,
                    @Utl,
                    @TransactionId,
                    @AirlineCodes,
                    @Level,
                    @ErrorTypeId,
                    @IpDetails,
                    @SessionId,
                    @ValidatingAirlineCode
                );
                """;

            var userId = GetUserId(httpContext);
            var userType = GetClaim(httpContext, ClaimTypes.Role) ?? "";
            var companyId = GetClaim(httpContext, "CompanyId") ?? "";
            var companyType = GetClaim(httpContext, "CompanyType") ?? "";
            var ipDetails = httpContext?.Connection?.RemoteIpAddress?.ToString() ?? "";
            var sessionId = GetSessionId(httpContext);
            var transactionId = GetNumericTransactionId(httpContext?.TraceIdentifier ?? Guid.NewGuid().ToString("N"));
            var airlineCodes = GetClaim(httpContext, "AirlineCode") ?? "";
            var validatingAirlineCode = GetClaim(httpContext, "ValidatingAirlineCode") ?? "";
            var remarks = BuildRemarks(exception);
            var errorCode = exception.HResult.ToString();

            await using var cmd = new MySqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Upl", Guid.NewGuid().ToString("N")[..20]);
            cmd.Parameters.AddWithValue("@Workflow", workflow ?? "Unknown");
            cmd.Parameters.AddWithValue("@UserId", string.IsNullOrWhiteSpace(userId) ? 0L : ParseLong(userId));
            cmd.Parameters.AddWithValue("@UserType", userType);
            cmd.Parameters.AddWithValue("@CompanyId", string.IsNullOrWhiteSpace(companyId) ? 0L : ParseLong(companyId));
            cmd.Parameters.AddWithValue("@CompanyType", companyType);
            cmd.Parameters.AddWithValue("@ModuleName", moduleName ?? "Unknown");
            cmd.Parameters.AddWithValue("@ModuleCode", moduleCode ?? "Unknown");
            cmd.Parameters.AddWithValue("@ClassName", className ?? "Unknown");
            cmd.Parameters.AddWithValue("@ProcedureName", procedureName ?? "Unknown");
            cmd.Parameters.AddWithValue("@ErrorCode", errorCode);
            cmd.Parameters.AddWithValue("@Remarks", remarks);
            cmd.Parameters.AddWithValue("@Aui", "");
            cmd.Parameters.AddWithValue("@Utl", "");
            cmd.Parameters.AddWithValue("@TransactionId", transactionId);
            cmd.Parameters.AddWithValue("@AirlineCodes", airlineCodes);
            cmd.Parameters.AddWithValue("@Level", "ERROR");
            cmd.Parameters.AddWithValue("@ErrorTypeId", 1);
            cmd.Parameters.AddWithValue("@IpDetails", ipDetails);
            cmd.Parameters.AddWithValue("@SessionId", sessionId);
            cmd.Parameters.AddWithValue("@ValidatingAirlineCode", validatingAirlineCode);

            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist exception to bd_log_errorlog. Original error: {Message}", exception.Message);
        }
    }

   

    private string? ResolveLogConnectionString()
    {
        return _connectionCredentialStore.GetConnectionString("LogDBConnection")
            ?? _configuration.GetConnectionString("LogDBConnection")
            ?? _configuration.GetConnectionString("SkyOpsDBconnection");
    }

    private static string BuildRemarks(Exception exception)
    {
        var message = exception?.Message ?? "Unknown error";
        var stack = exception?.StackTrace ?? string.Empty;
        var inner = exception?.InnerException?.Message;
        var details = string.IsNullOrWhiteSpace(inner) ? string.Empty : $" | Inner: {inner}";
        return $"{message}{details} | Stack: {stack}";
    }

    private static string GetUserId(HttpContext? httpContext)
    {
        var claim = GetClaim(httpContext, ClaimTypes.NameIdentifier) ?? GetClaim(httpContext, "UserId") ?? GetClaim(httpContext, "user_id");
        return claim ?? string.Empty;
    }

    private static string GetSessionId(HttpContext? httpContext)
    {
        if (httpContext is null)
        {
            return string.Empty;
        }

        try
        {
            return httpContext.Features.Get<ISessionFeature>()?.Session?.Id ?? httpContext.TraceIdentifier;
        }
        catch
        {
            return httpContext.TraceIdentifier;
        }
    }

    private static long ParseLong(string value)
    {
        return long.TryParse(value, out var parsed) ? parsed : 0L;
    }

    private static int GetNumericTransactionId(string value)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (var ch in value)
            {
                hash ^= (byte)ch;
                hash *= 1099511628211UL;
            }

            return (int)(hash % (ulong)int.MaxValue);
        }
    }

    private static string? GetClaim(HttpContext? httpContext, string claimType)
    {
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        return httpContext.User.FindFirst(claimType)?.Value;
    }
}
