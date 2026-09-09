namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface ICrmActionTokenService
{
    Task<string> CreateAsync(string caseNumber, string recipient, string recipientType, IReadOnlyCollection<string> allowedActions, TimeSpan lifetime, CancellationToken ct = default);
    Task<CrmActionToken?> ConsumeAsync(string token, string actionCode, CancellationToken ct = default);
}

public sealed record CrmActionToken(string CaseNumber, string Recipient, string RecipientType, IReadOnlySet<string> AllowedActions);
