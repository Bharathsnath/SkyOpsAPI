using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IUserRepository
{
    Task<IReadOnlyList<User>> GetAllAsync(int? role = null, long? callerUserId = null, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task UpdateLastLoginAsync(string username, CancellationToken ct = default);
    Task IncrementFailedAttemptsAsync(string username, CancellationToken ct = default);
    Task ResetFailedAttemptsAsync(string username, CancellationToken ct = default);
    Task<long> CreateUserAsync(User user, CancellationToken ct = default);
    Task<bool> UpdateUserAsync(User user, CancellationToken ct = default);
    Task<IEnumerable<RoleMaster>> GetRolesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetEmailsByUserIdsAsync(IEnumerable<int> userIds, CancellationToken ct = default);
    Task<IReadOnlyList<UserMarketPermission>> GetMarketPermissionsAsync(long userId, CancellationToken ct = default);
    Task SaveMarketPermissionsAsync(long userId, IEnumerable<SaveMarketPermissionRequest> permissions, CancellationToken ct = default);
}
