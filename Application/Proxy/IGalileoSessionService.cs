namespace SkyOpsQueueIntelligence.Application.Proxy;

public interface IGalileoSessionService
{
    Task<GalileoSession?> CreateSessionAsync(
        string pccCode,
        string? profile = null,
        CancellationToken cancellationToken = default);
}

public sealed record GalileoSession(string SessionToken, string Profile, string UplId);