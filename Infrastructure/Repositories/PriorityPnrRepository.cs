using Microsoft.Extensions.Configuration;
using MySqlConnector;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public sealed class PriorityPnrRepository : IPriorityPnrRepository
{
    private readonly string _connectionString;

    public PriorityPnrRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("SkyOpsDBconnection")
            ?? throw new InvalidOperationException("SkyOpsDBconnection is not configured.");
    }

    private static DateTime IndianNow =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"));

    public async Task<long> AddAsync(PriorityPnrEntry entry, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            INSERT INTO prioritypnrmaster
                (Pnr, PriorityLevel, TravelDate, NotifyEmail, Users, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate)
            VALUES
                (@Pnr, @PriorityLevel, @TravelDate, @NotifyEmail, @Users, 1, @CreatedBy, @Now, @CreatedBy, @Now)
            ON DUPLICATE KEY UPDATE
                PriorityLevel = VALUES(PriorityLevel),
                TravelDate    = VALUES(TravelDate),
                NotifyEmail   = VALUES(NotifyEmail),
                Users         = VALUES(Users),
                IsActive      = 1,
                ModifiedBy    = VALUES(CreatedBy),
                ModifiedDate  = VALUES(CreatedDate);
            SELECT LAST_INSERT_ID();
            """;

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Pnr", entry.Pnr.ToUpperInvariant());
        cmd.Parameters.AddWithValue("@PriorityLevel", entry.PriorityLevel);
        cmd.Parameters.AddWithValue("@TravelDate", (object?)entry.TravelDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NotifyEmail", (object?)entry.NotifyEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Users", (object?)entry.Users ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@CreatedBy", entry.CreatedBy);
        cmd.Parameters.AddWithValue("@Now", IndianNow);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<bool> UpdateAsync(long id, PriorityPnrEntry entry, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            UPDATE prioritypnrmaster
            SET PriorityLevel = @PriorityLevel,
                TravelDate    = @TravelDate,
                NotifyEmail   = @NotifyEmail,
                Users         = @Users,
                ModifiedBy    = @ModifiedBy,
                ModifiedDate  = @Now
            WHERE Id = @Id AND IsActive = 1;
            """;

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@PriorityLevel", entry.PriorityLevel);
        cmd.Parameters.AddWithValue("@TravelDate", (object?)entry.TravelDate ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@NotifyEmail", (object?)entry.NotifyEmail ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@Users", (object?)entry.Users ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@ModifiedBy", entry.ModifiedBy ?? entry.CreatedBy);
        cmd.Parameters.AddWithValue("@Now", IndianNow);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IReadOnlyList<PriorityPnrEntry>> GetAllAsync(CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT p.Id,p.PNR,p.PriorityLevel,p.TravelDate,p.NotifyEmail,p.IsActive,
                 p.CreatedBy,cu.UserName AS CreatedByUser,p.CreatedDate,
                 p.ModifiedBy,mu.UserName AS ModifiedByUser,p.ModifiedDate,p.users AS Users
                FROM prioritypnrmaster p
                LEFT JOIN users cu
                     ON p.CreatedBy = cu.Id
                LEFT JOIN users mu
                ON p.ModifiedBy = mu.Id
                WHERE p.IsActive = 1;
            """;

        await using var cmd = new MySqlCommand(sql, conn);
        return await ReadListAsync(cmd, ct);
    }

    public async Task<PriorityPnrEntry?> GetByPnrAsync(string pnr, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT p.Id, p.Pnr, p.PriorityLevel, p.TravelDate, p.NotifyEmail, p.users AS Users,
                   p.IsActive, p.CreatedBy, cu.UserName AS CreatedByUser, p.CreatedDate,
                   p.ModifiedBy, mu.UserName AS ModifiedByUser, p.ModifiedDate
            FROM prioritypnrmaster p
            LEFT JOIN users cu ON p.CreatedBy = cu.Id
            LEFT JOIN users mu ON p.ModifiedBy = mu.Id
            WHERE p.Pnr = @Pnr AND p.IsActive = 1
            LIMIT 1;
            """;

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Pnr", pnr.ToUpperInvariant());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<PriorityPnrEntry?> GetByRemarkEmailAsync(string email, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            SELECT p.Id, p.Pnr, p.PriorityLevel, p.TravelDate, p.NotifyEmail, p.users AS Users,
                   p.IsActive, p.CreatedBy, cu.UserName AS CreatedByUser, p.CreatedDate,
                   p.ModifiedBy, mu.UserName AS ModifiedByUser, p.ModifiedDate
            FROM prioritypnrmaster p
            LEFT JOIN users cu ON p.CreatedBy = cu.Id
            LEFT JOIN users mu ON p.ModifiedBy = mu.Id
            WHERE LOWER(p.Pnr) = LOWER(@Email) AND p.IsActive = 1
            LIMIT 1;
            """;

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Email", email.Trim());
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    public async Task<bool> DeleteAsync(long id, int modifiedBy, CancellationToken ct = default)
    {
        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = """
            UPDATE prioritypnrmaster
            SET IsActive = 0, ModifiedBy = @ModifiedBy, ModifiedDate = @Now
            WHERE Id = @Id;
            """;

        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Id", id);
        cmd.Parameters.AddWithValue("@ModifiedBy", modifiedBy);
        cmd.Parameters.AddWithValue("@Now", IndianNow);
        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    private static async Task<IReadOnlyList<PriorityPnrEntry>> ReadListAsync(MySqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<PriorityPnrEntry>();
        while (await reader.ReadAsync(ct))
            list.Add(Map(reader));
        return list;
    }

    private static PriorityPnrEntry Map(MySqlDataReader r) => new()
    {
        Id            = r.GetInt64("Id"),
        Pnr           = r.GetString("Pnr"),
        PriorityLevel = r.IsDBNull(r.GetOrdinal("PriorityLevel")) ? "MEDIUM" : r.GetString("PriorityLevel"),
        TravelDate    = r.IsDBNull(r.GetOrdinal("TravelDate"))    ? null : r.GetDateTime("TravelDate"),
        NotifyEmail   = r.IsDBNull(r.GetOrdinal("NotifyEmail"))   ? string.Empty : r.GetString("NotifyEmail"),
        Users         = r.IsDBNull(r.GetOrdinal("Users"))         ? null : r.GetString("Users"),
        IsActive      = r.GetInt32("IsActive"),
        CreatedBy     = r.IsDBNull(r.GetOrdinal("CreatedBy"))     ? 0 : r.GetInt32("CreatedBy"),
        CreatedByUser = r.IsDBNull(r.GetOrdinal("CreatedByUser")) ? null : r.GetString("CreatedByUser"),
        CreatedDate   = r.IsDBNull(r.GetOrdinal("CreatedDate"))   ? null : r.GetDateTime("CreatedDate"),
        ModifiedBy    = r.IsDBNull(r.GetOrdinal("ModifiedBy"))    ? null : r.GetInt32("ModifiedBy"),
        ModifiedByUser = r.IsDBNull(r.GetOrdinal("ModifiedByUser")) ? null : r.GetString("ModifiedByUser"),
        ModifiedDate  = r.IsDBNull(r.GetOrdinal("ModifiedDate"))  ? null : r.GetDateTime("ModifiedDate")
    };
}