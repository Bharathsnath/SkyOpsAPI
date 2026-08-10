using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IConnectionCredentialStore
{
    event EventHandler? Reloaded;
    bool IsConfigured { get; }
    IReadOnlyList<ConnectionCredential> GetConnectionCredentials();
    IReadOnlyList<ConnectionCredential> GetByPcc(string pccCode);
    string? GetConnectionString(string name);
    string? GetTagValue(string pccCode, string tagName);
    Task LoadAsync(CancellationToken cancellationToken = default);
}

public interface ICredentialStore
{
    bool IsConfigured { get; }
    IReadOnlyList<PccCredential> GetAll();
    IReadOnlyList<PccCredential> GetByPcc(string pccCode);
    string? GetTagValue(string pccCode, string tagName);
    Task LoadAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PccCredential>> GetAllFullAsync(CancellationToken cancellationToken = default);
}
