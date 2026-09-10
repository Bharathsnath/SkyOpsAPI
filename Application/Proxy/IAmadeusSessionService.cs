namespace SkyOpsQueueIntelligence.Application.Proxy;

public interface IAmadeusSessionService
{
    Task<AmadeusSession?> CreateSessionAsync(string pccCode, CancellationToken cancellationToken = default);
    Task<string> SendCommandAsync(AmadeusSession session, string command, CancellationToken cancellationToken = default);
    Task CloseSessionAsync(AmadeusSession session, CancellationToken cancellationToken = default);
}

public sealed record AmadeusSession(string SessionId, int SequenceNumber, string SecurityToken, string PccCode, string UplId, string CommandEndpoint);