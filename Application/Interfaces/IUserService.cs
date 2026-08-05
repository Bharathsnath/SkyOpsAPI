using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IUserService
{
    Task<IReadOnlyList<User>> GetAllAsync(int? role = null, CancellationToken ct = default);
    Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task<long> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<bool> UpdateUserAsync(long id, UpdateUserRequest request, CancellationToken ct = default);
    Task<IEnumerable<RoleMaster>> GetRolesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserMarketPermission>> GetMarketPermissionsAsync(long userId, CancellationToken ct = default);
    Task SaveMarketPermissionsAsync(long userId, IEnumerable<SaveMarketPermissionRequest> permissions, CancellationToken ct = default);
}
