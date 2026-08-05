using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Request;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface ISettingsService
{
    Task<IReadOnlyList<AppConfiguration>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AppConfiguration>> GetByCategoryAsync(string category, CancellationToken ct = default);
    Task<AppConfiguration?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<long> CreateAsync(PccCredentialRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersByPccAsync(string pccCode, CancellationToken ct = default);
    Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersByPccsAsync(IEnumerable<string> pccCodes, CancellationToken ct = default);
    Task<long> CreatePccAgentEmailMasterAsync(PccAgentEmailMasterRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(AppConfigurationRequest request, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
    Task<List<AppConfiguration>> GetLoggingConfigurationsAsync(CancellationToken ct = default);
    Task UpdateConfigurationAsync(string configKey, bool enabled, int modifiedUser, CancellationToken ct = default);
    Task UpdateConnectionTagAsync(long credId, string tagName, string tagValue, int modifiedUser, bool isEnabled, CancellationToken ct = default);
    Task<IReadOnlyList<PccCredential>> GetPccCredentialsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PccCredential>> GetPccCredentialsByPccAsync(string pccCode, CancellationToken ct = default);
    Task<IReadOnlyList<PccGroupEntry>> GetPccListAsync(CancellationToken ct = default);
    Task<long> CreatePccCredentialAsync(PccCredentialRequest request, CancellationToken ct = default);
    Task<long> CreatePccCredentialSkyopsAsync(PccCredentialRequest request, CancellationToken ct = default);
    Task<bool> UpdatePccCredentialAsync(long credId, PccCredentialRequest request, CancellationToken ct = default);
    Task<bool> SetPccCredentialStatusAsync(long credId, int recordStatus, int modifiedUser, CancellationToken ct = default);
    Task<long> CreateAppConfigurationAsync(AppConfigurationRequest request, CancellationToken ct = default);
}
