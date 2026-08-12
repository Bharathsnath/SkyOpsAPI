using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly string? _connectionString;
    private readonly ILogger<UserRepository> _logger;

    public UserRepository(IConfiguration configuration, ILogger<UserRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("SkyOpsDBconnection");
        _logger = logger;
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(int? role = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = @"SELECT Id, Username, Email,  IsActive, FailedLoginAttempts, LockedUntil,
                                    LastLogin, Role, Updatedby, CreatedAt, mobile
                             FROM Users
                             WHERE (@role IS NULL OR Role = @role)
                             ORDER BY CreatedAt DESC, Id DESC";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@role", (object?)role ?? DBNull.Value);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var users = new List<User>();
        while (await rdr.ReadAsync(ct))
        {
            users.Add(new User
            {
                Id = rdr.IsDBNull(0) ? 0L : rdr.GetInt64(0),
                Username = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1),
                Email = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                IsActive = !rdr.IsDBNull(3) && rdr.GetBoolean(3),
                FailedLoginAttempts = rdr.IsDBNull(4) ? 0 : rdr.GetInt32(4),
                LockedUntil = rdr.IsDBNull(5) ? null : rdr.GetDateTime(5),
                LastLogin = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6),
                Role = rdr.IsDBNull(7) ? 0 : rdr.GetInt32(7),
                UpdatedBy = rdr.IsDBNull(8) ? null : rdr.GetInt32(8),
                CreatedAt = rdr.IsDBNull(9) ? null : rdr.GetDateTime(9),
                mobile = rdr.IsDBNull(10) ? 0L : rdr.GetInt64(10)
            });
        }

        return users;
    }

    public async Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = @"SELECT Id, Username, Email, PasswordHash, IsActive, FailedLoginAttempts, LockedUntil, LastLogin, Role, mobile
                             FROM Users WHERE Username = @u LIMIT 1";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@u", username);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        if (!await rdr.ReadAsync(ct)) return null;

        return new User
        {
                Id = rdr.IsDBNull(0) ? 0L : rdr.GetInt64(0),
            Username = rdr.IsDBNull(1) ? string.Empty : rdr.GetString(1),
            Email = rdr.IsDBNull(2) ? null : rdr.GetString(2),
            PasswordHash = rdr.IsDBNull(3) ? null : rdr.GetString(3),
            IsActive = !rdr.IsDBNull(4) && rdr.GetBoolean(4),
            FailedLoginAttempts = rdr.IsDBNull(5) ? 0 : rdr.GetInt32(5),
            LockedUntil = rdr.IsDBNull(6) ? (DateTime?)null : rdr.GetDateTime(6),
            LastLogin = rdr.IsDBNull(7) ? (DateTime?)null : rdr.GetDateTime(7),
            Role = rdr.IsDBNull(8) ? 0 : rdr.GetInt32(8),
            mobile = rdr.IsDBNull(9) ? 0L : rdr.GetInt64(9)
        };
    }

    public async Task UpdateLastLoginAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "UPDATE Users SET LastLogin = NOW(), FailedLoginAttempts = 0 WHERE Username = @u";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@u", username);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task IncrementFailedAttemptsAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "UPDATE Users SET FailedLoginAttempts = FailedLoginAttempts + 1 WHERE Username = @u";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@u", username);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ResetFailedAttemptsAsync(string username, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "UPDATE Users SET FailedLoginAttempts = 0 WHERE Username = @u";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@u", username);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<long> CreateUserAsync(User user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = @"INSERT INTO Users (Username, Email, PasswordHash, Password, IsActive, Role, CreatedAt, Updatedby, mobile)
                             VALUES (@u, @e, SHA2(@p, 256), @p, @a, @admin, NOW(), @updatedby, @mobile);
                             SELECT LAST_INSERT_ID();";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@u", user.Username);
        cmd.Parameters.AddWithValue("@e", (object?)user.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@p", user.PasswordHash ?? string.Empty);
        cmd.Parameters.AddWithValue("@a", user.IsActive);
        cmd.Parameters.AddWithValue("@admin", user.Role);
        cmd.Parameters.AddWithValue("@updatedby", user.Id);
        cmd.Parameters.AddWithValue("@mobile", user.mobile);

        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task<bool> UpdateUserAsync(User user, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = @"UPDATE Users SET
                             Username = @u, Email = @e, IsActive = @a, Role = @admin, Updatedby = @updatedby, mobile = @mobile
                             WHERE Id = @id";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@u", user.Username);
        cmd.Parameters.AddWithValue("@e", (object?)user.Email ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@a", user.IsActive);
        cmd.Parameters.AddWithValue("@admin", user.Role);
        cmd.Parameters.AddWithValue("@updatedby", user.Id);
        cmd.Parameters.AddWithValue("@mobile", user.mobile);
        cmd.Parameters.AddWithValue("@id", user.Id);

        return await cmd.ExecuteNonQueryAsync(ct) > 0;
    }

    public async Task<IEnumerable<RoleMaster>> GetRolesAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = "SELECT RoleId, RoleName FROM RoleMaster ORDER BY RoleId";
        await using var cmd = new MySqlCommand(sql, conn);
        await using var rdr = await cmd.ExecuteReaderAsync(ct);

        var roles = new List<RoleMaster>();
        while (await rdr.ReadAsync(ct))
            roles.Add(new RoleMaster { Id = rdr.GetInt32(0), RoleName = rdr.GetString(1) });

        return roles;
    }

    public async Task<IReadOnlyList<string>> GetEmailsByUserIdsAsync(IEnumerable<int> userIds, CancellationToken ct = default)
    {
        var ids = userIds.ToList();
        if (ids.Count == 0) return Array.Empty<string>();

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        var paramNames = ids.Select((_, i) => $"@id{i}").ToArray();
        var sql = $"SELECT Email FROM Users WHERE Id IN ({string.Join(",", paramNames)}) AND IsActive = 1 AND Email IS NOT NULL";
        await using var cmd = new MySqlCommand(sql, conn);
        for (var i = 0; i < ids.Count; i++)
            cmd.Parameters.AddWithValue(paramNames[i], ids[i]);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var emails = new List<string>();
        while (await rdr.ReadAsync(ct))
            if (!rdr.IsDBNull(0)) emails.Add(rdr.GetString(0));
        return emails;
    }

    public async Task<IReadOnlyList<UserMarketPermission>> GetMarketPermissionsAsync(long userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        const string sql = @"SELECT Id, UserId, PermissionType, ReferenceId, IsActive
                             FROM UserMarketPermission
                             WHERE UserId = @userId AND IsActive = 1";
        await using var cmd = new MySqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@userId", userId);

        await using var rdr = await cmd.ExecuteReaderAsync(ct);
        var list = new List<UserMarketPermission>();
        while (await rdr.ReadAsync(ct))
            list.Add(new UserMarketPermission
            {
                Id = rdr.GetInt32(0),
                UserId = rdr.GetInt32(1),
                PermissionType = rdr.GetString(2),
                ReferenceId = rdr.GetInt32(3),
                IsActive = !rdr.IsDBNull(4) && rdr.GetBoolean(4)
            });
        return list;
    }

    public async Task SaveMarketPermissionsAsync(long userId, IEnumerable<SaveMarketPermissionRequest> permissions, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
            throw new InvalidOperationException("SkyOpsDBconnection not configured");

        var items = permissions.ToList();

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Deactivate all existing permissions for this user
        const string deactivateSql = "UPDATE UserMarketPermission SET IsActive = 0, ModifiedBy = @modifiedBy, ModifiedDate = NOW() WHERE UserId = @userId";
        await using (var deactivateCmd = new MySqlCommand(deactivateSql, conn, tx))
        {
            deactivateCmd.Parameters.AddWithValue("@userId", userId);
            deactivateCmd.Parameters.AddWithValue("@modifiedBy", items.FirstOrDefault()?.ModifiedBy ?? 0);
            await deactivateCmd.ExecuteNonQueryAsync(ct);
        }

        // Upsert each active permission
        const string upsertSql = @"INSERT INTO UserMarketPermission (UserId, PermissionType, ReferenceId, IsActive, CreatedBy, CreatedDate, ModifiedBy, ModifiedDate)
                                   VALUES (@userId, @type, @refId, 1, @modifiedBy, NOW(), @modifiedBy, NOW())
                                   ON DUPLICATE KEY UPDATE IsActive = 1, ModifiedBy = @modifiedBy, ModifiedDate = NOW()";
        foreach (var item in items.Where(i => i.IsActive))
        {
            await using var upsertCmd = new MySqlCommand(upsertSql, conn, tx);
            upsertCmd.Parameters.AddWithValue("@userId", userId);
            upsertCmd.Parameters.AddWithValue("@type", item.PermissionType);
            upsertCmd.Parameters.AddWithValue("@refId", item.ReferenceId);
            upsertCmd.Parameters.AddWithValue("@modifiedBy", item.ModifiedBy);
            await upsertCmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }
}
