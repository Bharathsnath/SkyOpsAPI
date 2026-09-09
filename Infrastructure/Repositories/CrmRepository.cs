using System.Text.Json;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public sealed class SqlCrmRepository : ICrmRepository
{
    private readonly string? connectionString;
    public SqlCrmRepository(IConfiguration configuration) => connectionString = configuration.GetConnectionString("SkyOpsDBconnection");

    private MySqlConnection OpenConnection() => string.IsNullOrWhiteSpace(connectionString)
        ? throw new InvalidOperationException("ConnectionStrings:SkyOpsDBconnection is not configured.")
        : new MySqlConnection(connectionString);

    public async Task<IReadOnlyList<CrmEmailDto>> GetEmailsAsync(CrmSearchRequest request, CancellationToken ct = default)
    {
        await using var connection = OpenConnection(); await connection.OpenAsync(ct);
        const string sql = """
            SELECT e.Id, c.CaseNumber, COALESCE(cu.Name, e.FromEmail), e.FromEmail, e.Subject,
                   LEFT(REPLACE(REPLACE(e.BodyHtml, CHAR(13), ' '), CHAR(10), ' '), 240), e.BodyHtml,
                   e.EmailType, COALESCE(e.Pnr, c.Pnr), c.Priority, c.Status, e.ReceivedAt,
                   e.IsStarred, e.ToEmail, e.Cc, c.CaseType
            FROM CrmEmails e JOIN CrmCases c ON c.Id = e.CaseId
            LEFT JOIN CrmCustomers cu ON cu.Id = c.CustomerId
            WHERE (@Query = '' OR
              (@Field = 'pnr' AND COALESCE(e.Pnr, c.Pnr) LIKE @LikeQuery) OR
              (@Field = 'customer' AND COALESCE(cu.Name, '') LIKE @LikeQuery) OR
              (@Field = 'email' AND (e.FromEmail LIKE @LikeQuery OR e.ToEmail LIKE @LikeQuery)) OR
              (@Field = 'subject' AND e.Subject LIKE @LikeQuery) OR
              (@Field = 'case' AND c.CaseNumber LIKE @LikeQuery) OR
              (@Field = 'all' AND (e.Subject LIKE @LikeQuery OR e.FromEmail LIKE @LikeQuery OR c.CaseNumber LIKE @LikeQuery OR COALESCE(e.Pnr, c.Pnr) LIKE @LikeQuery)))
              AND (@Type = '' OR e.EmailType = @Type)
            ORDER BY e.ReceivedAt DESC LIMIT @Offset, @PageSize
            """;
        await using var command = new MySqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Query", request.Query?.Trim() ?? string.Empty);
        command.Parameters.AddWithValue("@LikeQuery", $"%{request.Query?.Trim() ?? string.Empty}%");
        command.Parameters.AddWithValue("@Field", string.IsNullOrWhiteSpace(request.Field) ? "all" : request.Field);
        command.Parameters.AddWithValue("@Type", request.Type ?? string.Empty);
        command.Parameters.AddWithValue("@Offset", Math.Max(0, request.Page - 1) * Math.Clamp(request.PageSize, 1, 100));
        command.Parameters.AddWithValue("@PageSize", Math.Clamp(request.PageSize, 1, 100));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<CrmEmailDto>(); while (await reader.ReadAsync(ct)) result.Add(ReadEmail(reader)); return result;
    }

    public async Task<CrmEmailDto?> GetEmailAsync(long id, CancellationToken ct = default)
    {
        var items = await GetEmailsAsync(new CrmSearchRequest(id.ToString(), "all", null, 1, 100), ct);
        return items.FirstOrDefault(item => item.Id == id);
    }

    public async Task<CrmCaseDto?> GetCaseAsync(string caseNumber, CancellationToken ct = default)
    {
        await using var connection = OpenConnection(); await connection.OpenAsync(ct);
        const string sql = "SELECT c.Id,c.CaseNumber,c.Pnr,cu.Name,cu.Email,c.CaseType,c.Priority,c.Status,c.CreatedAt,c.UpdatedAt,c.SlaDueAt FROM CrmCases c LEFT JOIN CrmCustomers cu ON cu.Id=c.CustomerId WHERE c.CaseNumber=@CaseNumber";
        await using var command = new MySqlCommand(sql, connection); command.Parameters.AddWithValue("@CaseNumber", caseNumber);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new CrmCaseDto(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), NullString(reader, 3), NullString(reader, 4), reader.GetString(5), reader.GetString(6), reader.GetString(7), ReadOffset(reader, 8), ReadOffset(reader, 9), reader.IsDBNull(10) ? null : ReadOffset(reader, 10)) : null;
    }

    public async Task<IReadOnlyList<CrmTimelineItemDto>> GetTimelineAsync(string caseNumber, CancellationToken ct = default)
    {
        await using var connection = OpenConnection(); await connection.OpenAsync(ct);
        const string sql = "SELECT m.Id,m.SenderType,m.SenderId,m.Message,m.CreatedAt,a.ActionCode FROM CrmMessages m JOIN CrmCases c ON c.Id=m.CaseId LEFT JOIN CrmActionEvents a ON a.MessageId=m.Id WHERE c.CaseNumber=@CaseNumber ORDER BY m.CreatedAt";
        await using var command = new MySqlCommand(sql, connection); command.Parameters.AddWithValue("@CaseNumber", caseNumber);
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<CrmTimelineItemDto>();
        while (await reader.ReadAsync(ct)) result.Add(new CrmTimelineItemDto(reader.GetInt64(0), reader.GetString(1), NullString(reader, 2), reader.GetString(3), ReadOffset(reader, 4), NullString(reader, 5)));
        return result;
    }

    public async Task<CrmTimelineItemDto> AddMessageAsync(string caseNumber, string senderType, string? senderId, string message, CancellationToken ct = default)
    {
        Validate(message, 4000, nameof(message)); await using var connection = OpenConnection(); await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("INSERT INTO CrmMessages (CaseId,SenderType,SenderId,Message,CreatedAt) SELECT Id,@SenderType,@SenderId,@Message,UTC_TIMESTAMP(6) FROM CrmCases WHERE CaseNumber=@CaseNumber; SELECT LAST_INSERT_ID(),UTC_TIMESTAMP(6);", connection);
        command.Parameters.AddWithValue("@CaseNumber", caseNumber); command.Parameters.AddWithValue("@SenderType", senderType); command.Parameters.AddWithValue("@SenderId", senderId ?? (object)DBNull.Value); command.Parameters.AddWithValue("@Message", message.Trim());
        await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException($"CRM case '{caseNumber}' was not found.");
        return new CrmTimelineItemDto(reader.GetInt64(0), senderType, senderId, message.Trim(), ReadOffset(reader, 1));
    }

    public async Task<CrmTimelineItemDto> AddActionAsync(string caseNumber, long? messageId, string actorType, string? actorId, CrmActionRequest request, CancellationToken ct = default)
    {
        Validate(request.ActionCode, 80, nameof(request.ActionCode)); Validate(request.Source, 80, nameof(request.Source));
        await using var connection = OpenConnection(); await connection.OpenAsync(ct); await using var transaction = await connection.BeginTransactionAsync(ct);
        try
        {
            await using var message = new MySqlCommand("INSERT INTO CrmMessages (CaseId,SenderType,SenderId,Message,CreatedAt) SELECT Id,@ActorType,@ActorId,@Text,UTC_TIMESTAMP(6) FROM CrmCases WHERE CaseNumber=@CaseNumber; SELECT LAST_INSERT_ID(),UTC_TIMESTAMP(6);", connection, transaction);
            message.Parameters.AddWithValue("@CaseNumber", caseNumber); message.Parameters.AddWithValue("@ActorType", actorType); message.Parameters.AddWithValue("@ActorId", actorId ?? (object)DBNull.Value); message.Parameters.AddWithValue("@Text", $"Action received: {request.ActionCode}");
            await using var reader = await message.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) throw new KeyNotFoundException($"CRM case '{caseNumber}' was not found."); var id = reader.GetInt64(0); var created = ReadOffset(reader, 1); await reader.CloseAsync();
            await using var action = new MySqlCommand("INSERT INTO CrmActionEvents (CaseId,MessageId,ActorType,ActorId,ActionCode,ActionSource,ActionData,CreatedAt) SELECT Id,@MessageId,@ActorType,@ActorId,@ActionCode,@ActionSource,@ActionData,UTC_TIMESTAMP(6) FROM CrmCases WHERE CaseNumber=@CaseNumber", connection, transaction);
            action.Parameters.AddWithValue("@CaseNumber", caseNumber); action.Parameters.AddWithValue("@MessageId", id); action.Parameters.AddWithValue("@ActorType", actorType); action.Parameters.AddWithValue("@ActorId", actorId ?? (object)DBNull.Value); action.Parameters.AddWithValue("@ActionCode", request.ActionCode.Trim()); action.Parameters.AddWithValue("@ActionSource", request.Source.Trim()); action.Parameters.AddWithValue("@ActionData", request.ActionData is null ? DBNull.Value : JsonSerializer.Serialize(request.ActionData)); await action.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct); return new CrmTimelineItemDto(id, actorType, actorId, $"Action received: {request.ActionCode}", created, request.ActionCode);
        }
        catch { await transaction.RollbackAsync(ct); throw; }
    }

    public async Task<Customer360Dto?> GetCustomerAsync(long customerId, CancellationToken ct = default)
    {
        await using var connection = OpenConnection(); await connection.OpenAsync(ct);
        const string sql = "SELECT cu.Id,cu.Name,cu.Email,cu.Mobile,cu.Company,0, SUM(c.Status NOT IN ('CLOSED','CANCELLED')), SUM(c.Status IN ('CLOSED','CANCELLED')) FROM CrmCustomers cu LEFT JOIN CrmCases c ON c.CustomerId=cu.Id WHERE cu.Id=@Id GROUP BY cu.Id,cu.Name,cu.Email,cu.Mobile,cu.Company";
        await using var command = new MySqlCommand(sql, connection); command.Parameters.AddWithValue("@Id", customerId); await using var reader = await command.ExecuteReaderAsync(ct); if (!await reader.ReadAsync(ct)) return null;
        return new Customer360Dto(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), NullString(reader, 3), NullString(reader, 4), reader.GetInt32(5), reader.IsDBNull(6) ? 0 : reader.GetInt32(6), reader.IsDBNull(7) ? 0 : reader.GetInt32(7), Array.Empty<string>(), Array.Empty<string>());
    }

    public async Task<SmtpConfigurationRecord?> GetSmtpConfigurationAsync(CancellationToken ct = default)
    {
        await using var connection = OpenConnection(); await connection.OpenAsync(ct); await using var command = new MySqlCommand("SELECT Id,Provider,Host,Port,Username,EncryptedPassword,FromEmail,FromName,UseSsl,IsActive FROM SmtpConfigurations WHERE IsActive=1 ORDER BY Id DESC LIMIT 1", connection); await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? new SmtpConfigurationRecord(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.GetString(7), reader.GetBoolean(8), reader.GetBoolean(9)) : null;
    }

    public async Task SaveSmtpConfigurationAsync(SmtpConfigurationRecord configuration, CancellationToken ct = default)
    {
        await using var connection = OpenConnection(); await connection.OpenAsync(ct);
        await using var command = new MySqlCommand("UPDATE SmtpConfigurations SET Provider=@Provider,Host=@Host,Port=@Port,Username=@Username,EncryptedPassword=@Password,FromEmail=@FromEmail,FromName=@FromName,UseSsl=@UseSsl,IsActive=1,UpdatedAt=UTC_TIMESTAMP(6) WHERE IsActive=1; INSERT INTO SmtpConfigurations (Provider,Host,Port,Username,EncryptedPassword,FromEmail,FromName,UseSsl,IsActive,UpdatedAt) SELECT @Provider,@Host,@Port,@Username,@Password,@FromEmail,@FromName,@UseSsl,1,UTC_TIMESTAMP(6) WHERE ROW_COUNT()=0;", connection);
        command.Parameters.AddWithValue("@Provider", configuration.Provider); command.Parameters.AddWithValue("@Host", configuration.Host); command.Parameters.AddWithValue("@Port", configuration.Port); command.Parameters.AddWithValue("@Username", configuration.Username); command.Parameters.AddWithValue("@Password", configuration.EncryptedPassword); command.Parameters.AddWithValue("@FromEmail", configuration.FromEmail); command.Parameters.AddWithValue("@FromName", configuration.FromName); command.Parameters.AddWithValue("@UseSsl", configuration.UseSsl); await command.ExecuteNonQueryAsync(ct);
    }

    private static CrmEmailDto ReadEmail(MySqlDataReader r) => new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5),r.GetString(6),r.GetString(7),r.GetString(8),r.GetString(9),r.GetString(10),ReadOffset(r,11),r.GetBoolean(12),NullString(r,13),NullString(r,14),NullString(r,15));
    private static string? NullString(MySqlDataReader r,int i)=>r.IsDBNull(i)?null:r.GetString(i);
    private static DateTimeOffset ReadOffset(MySqlDataReader r,int i)=>new(DateTime.SpecifyKind(r.GetDateTime(i),DateTimeKind.Utc));
    private static void Validate(string value,int max,string name){if(string.IsNullOrWhiteSpace(value)||value.Length>max)throw new ArgumentException($"{name} is required and must be at most {max} characters.",name);}
}
