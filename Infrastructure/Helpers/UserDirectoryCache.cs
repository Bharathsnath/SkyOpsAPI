using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Services;

public sealed class UserDirectoryCache : IUserDirectoryCache
{
    private IReadOnlyDictionary<long, UserDirectoryEntry> _users = new Dictionary<long, UserDirectoryEntry>();

    public IReadOnlyDictionary<long, UserDirectoryEntry> Users => _users;

    public void Replace(IEnumerable<UserDirectoryEntry> users)
        => _users = users.ToDictionary(user => user.Id);
}
