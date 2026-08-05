namespace SkyOpsQueueIntelligence.Application.Proxy;

public interface ISabreSessionService
{
    Task<SabreSession?> CreateSessionAsync(string username, string password, string pcc, CancellationToken cancellationToken = default);
    Task CloseSessionAsync(SabreSession session, CancellationToken cancellationToken = default);
}

public sealed record SabreSession(string BinarySecurityToken, string ConversationId, string UplId);
