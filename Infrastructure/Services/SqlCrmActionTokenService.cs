using System.Security.Cryptography;
using System.Text;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Services;

public sealed class SqlCrmActionTokenService : ICrmActionTokenService
{
    private readonly string? connectionString;
    public SqlCrmActionTokenService(IConfiguration configuration) => connectionString = configuration.GetConnectionString("SkyOpsDBconnection");
    public async Task<string> CreateAsync(string caseNumber, string recipient, string recipientType, IReadOnlyCollection<string> allowedActions, TimeSpan lifetime, CancellationToken ct = default)
    {
        var tokenBytes = RandomNumberGenerator.GetBytes(32); var token = Convert.ToBase64String(tokenBytes).Replace('+','-').Replace('/','_').TrimEnd('='); var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        await using var connection = Open(); await connection.OpenAsync(ct); await using var command = new MySqlCommand("INSERT INTO CrmActionTokens (TokenHash,CaseId,Recipient,RecipientType,AllowedActions,ExpiresAt) SELECT @Hash,Id,@Recipient,@RecipientType,@AllowedActions,UTC_TIMESTAMP(6)+INTERVAL @Minutes MINUTE FROM CrmCases WHERE CaseNumber=@CaseNumber", connection);
        command.Parameters.Add("@Hash", MySqlDbType.Binary, 32).Value = hash; command.Parameters.AddWithValue("@CaseNumber",caseNumber); command.Parameters.AddWithValue("@Recipient",recipient); command.Parameters.AddWithValue("@RecipientType",recipientType); command.Parameters.AddWithValue("@AllowedActions",string.Join(',',allowedActions)); command.Parameters.AddWithValue("@Minutes",Math.Max(1,(int)Math.Ceiling(lifetime.TotalMinutes))); if(await command.ExecuteNonQueryAsync(ct)!=1) throw new KeyNotFoundException($"CRM case '{caseNumber}' was not found."); return token;
    }
    public async Task<CrmActionToken?> ConsumeAsync(string token, string actionCode, CancellationToken ct = default)
    {
        if(string.IsNullOrWhiteSpace(token)||string.IsNullOrWhiteSpace(actionCode)) return null; var hash=SHA256.HashData(Encoding.UTF8.GetBytes(token)); await using var connection=Open(); await connection.OpenAsync(ct); await using var transaction=await connection.BeginTransactionAsync(ct);
        await using var command=new MySqlCommand("SELECT t.Id,c.CaseNumber,t.Recipient,t.RecipientType,t.AllowedActions FROM CrmActionTokens t JOIN CrmCases c ON c.Id=t.CaseId WHERE t.TokenHash=@Hash AND t.ExpiresAt>UTC_TIMESTAMP(6) AND t.UsedAt IS NULL FOR UPDATE",connection,transaction); command.Parameters.Add("@Hash",MySqlDbType.Binary,32).Value=hash; await using var reader=await command.ExecuteReaderAsync(ct); if(!await reader.ReadAsync(ct)) return null; var id=reader.GetInt64(0); var result=new CrmActionToken(reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetString(4).Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)); if(!result.AllowedActions.Contains(actionCode)) return null; await reader.CloseAsync(); await using var update=new MySqlCommand("UPDATE CrmActionTokens SET UsedAt=UTC_TIMESTAMP(6) WHERE Id=@Id AND UsedAt IS NULL",connection,transaction); update.Parameters.AddWithValue("@Id",id); if(await update.ExecuteNonQueryAsync(ct)!=1) return null; await transaction.CommitAsync(ct); return result;
    }
    private MySqlConnection Open()=>string.IsNullOrWhiteSpace(connectionString)?throw new InvalidOperationException("ConnectionStrings:SkyOpsDBconnection is not configured."):new MySqlConnection(connectionString);
}
