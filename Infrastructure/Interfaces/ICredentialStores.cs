using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IConnectionCredentialStore
{
    event EventHandler? Reloaded;
    bool IsConfigured { get; }
    IReadOnlyList<ConnectionCredential> GetAll();
    IReadOnlyList<ConnectionCredential> GetByPcc(string pccCode);
    string? GetConnectionString(string name);
    string? GetTagValue(string pccCode, string tagName);
    Task LoadAsync(CancellationToken cancellationToken = default);
}

public interface ICredentialStore
{
    bool IsConfigured { get; }
    IReadOnlyList<StorePccCredential> GetAll();
    IReadOnlyList<StorePccCredential> GetByPcc(string pccCode);
    string? GetTagValue(string pccCode, string tagName);
    Task LoadAsync(CancellationToken cancellationToken = default);
}
