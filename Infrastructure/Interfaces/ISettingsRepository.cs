using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface ISettingsRepository
{
    Task<IReadOnlyList<AppConfiguration>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AppConfiguration>> GetByCategoryAsync(string category, CancellationToken ct = default);
    Task<AppConfiguration?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<long> CreateAsync(PccCredential credential, CancellationToken ct = default);
    Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersByPccAsync(string pccCode, CancellationToken ct = default);
    Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersByPccsAsync(IEnumerable<string> pccCodes, CancellationToken ct = default);
    Task<long> CreatePccAgentEmailMasterAsync(PccAgentEmailMaster entry, CancellationToken ct = default);
    Task<bool> UpdateAsync(AppConfiguration config, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
    Task<List<AppConfiguration>> GetLoggingConfigurationsAsync(CancellationToken ct = default);
    Task UpdateConfigurationAsync(string configKey, bool enabled, int modifiedUser, CancellationToken ct = default);
    Task UpdateConnectionTagAsync(long credId, string tagName, string tagValue, int modifiedUser, bool isEnabled, CancellationToken ct = default);
    Task<IReadOnlyList<PccCredential>> GetPccCredentialsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PccCredential>> GetPccCredentialsByPccAsync(string pccCode, CancellationToken ct = default);
    Task<IReadOnlyList<PccListEntry>> GetPccListAsync(CancellationToken ct = default);
    Task<long> CreatePccCredentialAsync(PccCredential credential, CancellationToken ct = default);
    Task<long> CreatePccCredentialSkyopsAsync(PccCredential credential, CancellationToken ct = default);
    Task<bool> UpdatePccCredentialAsync(long credId, PccCredential credential, CancellationToken ct = default);
    Task<bool> SetPccCredentialStatusAsync(long credId, int recordStatus, int modifiedUser, CancellationToken ct = default);
    Task<long> CreateAppConfigurationAsync(AppConfiguration config, CancellationToken ct = default);
}
