using Microsoft.Extensions.Configuration;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public class UserPccMappingRepository : IUserPccMappingRepository
{
    private readonly string? _connectionString;

    public UserPccMappingRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("SkyOpsDBconnection");
    }

    public async Task<IReadOnlyList<UserPccMapping>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            SELECT Id, UserId, PccCode, AccessType, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
            FROM userpccmapping ORDER BY UserId, PccCode
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<IReadOnlyList<UserPccMapping>> GetByUserIdAsync(int userId, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            SELECT Id, UserId, PccCode, AccessType, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate
            FROM userpccmapping WHERE UserId = @UserId and IsActive = 1 ORDER BY PccCode
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UserId", userId);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<long> CreateAsync(UserPccMapping mapping, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        var indianNow = IndianNow();
        const string sql = """
            INSERT INTO userpccmapping (UserId, PccCode, AccessType, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate)
            VALUES (@UserId, @PccCode, @AccessType, @IsActive, @CreatedBy, @Now, @ModifiedBy, @Now);
            SELECT LAST_INSERT_ID();
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@UserId", mapping.UserId);
        cmd.Parameters.AddWithValue("@PccCode", mapping.PccCode);
        cmd.Parameters.AddWithValue("@AccessType", mapping.AccessType);
        cmd.Parameters.AddWithValue("@IsActive", mapping.IsActive);
        cmd.Parameters.AddWithValue("@CreatedBy", mapping.CreatedBy);
        cmd.Parameters.AddWithValue("@ModifiedBy", mapping.ModifiedBy);
        cmd.Parameters.AddWithValue("@Now", indianNow);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<bool> UpdateAsync(UserPccMapping mapping, CancellationToken ct = default)
    {
        await using var conn = await OpenAsync(ct);
        const string sql = """
            UPDATE userpccmapping
            SET UserId = @UserId, PccCode = @PccCode, AccessType = @AccessType, IsActive = @IsActive, ModifiedBy = @ModifiedBy, ModifiedDate = @Now
            WHERE Id = @Id
            """;
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", mapping.Id);
        cmd.Parameters.AddWithValue("@UserId", mapping.UserId);
        cmd.Parameters.AddWithValue("@PccCode", mapping.PccCode);
        cmd.Parameters.AddWithValue("@AccessType", mapping.AccessType);
        cmd.Parameters.AddWithValue("@IsActive", mapping.IsActive);
        cmd.Parameters.AddWithValue("@ModifiedBy", mapping.ModifiedBy);
        cmd.Parameters.AddWithValue("@Now", IndianNow());
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        return conn;
    }

    private static DateTime IndianNow() =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));

    private static async Task<IReadOnlyList<UserPccMapping>> ReadListAsync(MySqlCommand cmd, CancellationToken ct)
    {
        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var list = new List<UserPccMapping>();
        while (await rdr.ReadAsync(ct))
            list.Add(new UserPccMapping
            {
                Id = rdr.GetInt64("Id"),
                UserId = rdr.GetInt32("UserId"),
                // Use conversion instead of GetString because MySQL can return numeric
                // values for legacy/mis-typed columns, while the API contract is string.
                PccCode = ReadString(rdr, "PccCode", string.Empty),
                AccessType = ReadString(rdr, "AccessType", "PCC"),
                IsActive = rdr.IsDBNull(rdr.GetOrdinal("IsActive")) ? 0 : rdr.GetInt32("IsActive"),
                CreatedBy = ReadInt32(rdr, "CreatedBy"),
                CreatedDate = rdr.IsDBNull(rdr.GetOrdinal("CreatedDate")) ? null : rdr.GetDateTime("CreatedDate"),
                ModifiedBy = ReadInt32(rdr, "ModifiedBy"),
                ModifiedDate = rdr.IsDBNull(rdr.GetOrdinal("ModifiedDate")) ? null : rdr.GetDateTime("ModifiedDate")
            });
        return list;
    }

    private static string ReadString(MySqlDataReader reader, string column, string fallback)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? fallback : Convert.ToString(reader.GetValue(ordinal)) ?? fallback;
    }

    private static int ReadInt32(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }
}
