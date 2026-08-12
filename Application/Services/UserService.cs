using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IUserDirectoryCache _userDirectoryCache;

    public UserService(IUserRepository userRepository, IUserDirectoryCache userDirectoryCache)
    {
        _userRepository = userRepository;
        _userDirectoryCache = userDirectoryCache;
    }

    public async Task<IReadOnlyList<User>> GetAllAsync(int? role = null, CancellationToken ct = default)
    {
        var users = await _userRepository.GetAllAsync(role, ct);
        var allUsers = role is null ? users : await _userRepository.GetAllAsync(ct: ct);
        RefreshDirectoryCache(allUsers);
        return users;
    }

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct = default)
        => _userRepository.GetByUsernameAsync(username, ct);

    public async Task<long> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default)
    {
        var user = new User
        {
            Username = request.Username,
            PasswordHash = request.Password,
            Email = request.Email,
            IsActive = request.IsActive,
            Role = request.Role,
            Id = request.UpdatedBy,
            mobile = request.Mobile
        };
        var id = await _userRepository.CreateUserAsync(user, ct);
        RefreshDirectoryCache(await _userRepository.GetAllAsync(ct: ct));
        return id;
    }

    public async Task<bool> UpdateUserAsync(long id, UpdateUserRequest request, CancellationToken ct = default)
    {
        var user = new User
        {
            Id = id,
            Username = request.Username,
            Email = request.Email,
            IsActive = request.IsActive,
            Role = request.Role,
            mobile = request.Mobile
        };
        var updated = await _userRepository.UpdateUserAsync(user, ct);
        if (updated)
            RefreshDirectoryCache(await _userRepository.GetAllAsync(ct: ct));
        return updated;
    }

    public Task<IEnumerable<RoleMaster>> GetRolesAsync(CancellationToken ct = default)
        => _userRepository.GetRolesAsync(ct);

    public Task<IReadOnlyList<UserMarketPermission>> GetMarketPermissionsAsync(long userId, CancellationToken ct = default)
        => _userRepository.GetMarketPermissionsAsync(userId, ct);

    public Task SaveMarketPermissionsAsync(long userId, IEnumerable<SaveMarketPermissionRequest> permissions, CancellationToken ct = default)
        => _userRepository.SaveMarketPermissionsAsync(userId, permissions, ct);

    private void RefreshDirectoryCache(IReadOnlyList<User> users)
        => _userDirectoryCache.Replace(users
            .Where(user => user.IsActive)
            .Select(user => new UserDirectoryEntry(user.Id, user.Username, user.Email, user.mobile)));
}
