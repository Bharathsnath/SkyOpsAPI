namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public sealed record UserDirectoryEntry(long Id, string Username, string? Email, long Mobile);

public interface IUserDirectoryCache
{
    IReadOnlyDictionary<long, UserDirectoryEntry> Users { get; }
    void Replace(IEnumerable<UserDirectoryEntry> users);
}
