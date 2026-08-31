using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Application.Helpers.Adapters.DTOAdapters;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public sealed class SettingsService : ISettingsService
{
    private readonly ISettingsRepository _repository;
    private readonly ICredentialStore _credentialStore;
    private readonly IConnectionCredentialStore _connectionCredentialStore;
    private readonly SettingsDtoAdapter _adapter;

    public SettingsService(
        ISettingsRepository repository,
        ICredentialStore credentialStore,
        IConnectionCredentialStore connectionCredentialStore,
        SettingsDtoAdapter adapter)
    {
        _repository = repository;
        _credentialStore = credentialStore;
        _connectionCredentialStore = connectionCredentialStore;
        _adapter = adapter;
    }

    public Task<IReadOnlyList<AppConfiguration>> GetAllAsync(CancellationToken ct = default) => _repository.GetAllAsync(ct);
    public Task<IReadOnlyList<AppConfiguration>> GetByCategoryAsync(string category, CancellationToken ct = default) => _repository.GetByCategoryAsync(category, ct);
    public Task<AppConfiguration?> GetByIdAsync(long id, CancellationToken ct = default) => _repository.GetByIdAsync(id, ct);
    public Task<long> CreateAsync(PccCredentialRequest request, CancellationToken ct = default)
    {
        var credential = _adapter.ToPccCredential(request);
        return ReloadAfterChangeAsync(_repository.CreatePccCredentialSkyopsAsync(credential, ct), ct);
    }
    public Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersAsync(CancellationToken ct = default) => _repository.GetPccAgentEmailMastersAsync(ct);
    public Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersByPccAsync(string pccCode, CancellationToken ct = default) => _repository.GetPccAgentEmailMastersByPccAsync(pccCode, ct);
    public Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersByPccCompanyMarketAsync(string pccCode, string company, string market, CancellationToken ct = default) => _repository.GetPccAgentEmailMastersByPccCompanyMarketAsync(pccCode, company, market, ct);
    public Task<IReadOnlyList<PccAgentEmailMaster>> GetPccAgentEmailMastersByPccsAsync(IEnumerable<string> pccCodes, CancellationToken ct = default) => _repository.GetPccAgentEmailMastersByPccsAsync(pccCodes, ct);
    public Task<long> CreatePccAgentEmailMasterAsync(PccAgentEmailMasterRequest request, CancellationToken ct = default)
    {
        var entry = _adapter.ToPccAgentEmailMaster(request);
        return _repository.CreatePccAgentEmailMasterAsync(entry, ct);
    }
    public Task<bool> UpdateAsync(AppConfigurationRequest request, CancellationToken ct = default)
    {
        var config = _adapter.ToAppConfiguration(request, request.Id);
        return ReloadAfterChangeAsync(_repository.UpdateAsync(config, ct), ct);
    }
    public Task<bool> 
    DeleteAsync(long id, CancellationToken ct = default) => ReloadAfterChangeAsync(_repository.DeleteAsync(id, ct), ct);
    public Task<List<AppConfiguration>>
     GetLoggingConfigurationsAsync(CancellationToken ct = default) => _repository.GetLoggingConfigurationsAsync(ct);
    public Task
     UpdateConfigurationAsync(string configKey, bool enabled, int modifiedUser, CancellationToken ct = default) => _repository.UpdateConfigurationAsync(configKey, enabled, modifiedUser, ct);
    public Task<IReadOnlyList<PccCredential>> 
    GetPccCredentialsAsync(CancellationToken ct = default) => _repository.GetPccCredentialsAsync(ct);
    public Task<IReadOnlyList<PccCredential>> GetPccCredentialsByPccAsync(string pccCode, CancellationToken ct = default) => _repository.GetPccCredentialsByPccAsync(pccCode, ct);
    public async Task<IReadOnlyList<PccGroupEntry>> GetPccListAsync(CancellationToken ct = default)
    {
        var Fullcredentials = await _credentialStore.GetAllFullAsync(ct);
        var result = Fullcredentials
            .GroupBy(c => c.PCCMasterCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                PccCode = g.Key,
                Username = g.FirstOrDefault(c => c.TagName.Equals("UserName", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty,
                Password = g.FirstOrDefault(c => c.TagName.Equals("Password", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty,
                SourceOffice = g.FirstOrDefault(c => c.TagName.Equals("SourceOffice", StringComparison.OrdinalIgnoreCase))?.TagValue ?? g.Key,
                Company = g.FirstOrDefault(c => c.TagName.Equals("Company", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty,
                Branch = g.FirstOrDefault(c => c.TagName.Equals("Branch", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty,
                Market = g.FirstOrDefault(c => c.TagName.Equals("Market", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty,
                Provider = g.FirstOrDefault()?.Provider ?? string.Empty
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Username) && !string.IsNullOrWhiteSpace(x.Password))
            .GroupBy(x => $"{x.SourceOffice}|{x.Username}|{x.Password}|{x.Company}|{x.Branch}|{x.Market}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Select(x => new PccGroupEntry
            {
                PccCode = x.PccCode,
                Username = x.Username,
                Provider = x.Provider,
                SourceOffice = x.SourceOffice,
                Company = x.Company,
                Branch = x.Branch,
                Market = x.Market
            })
            .ToList();

        return result;
    }
    public Task<long> CreateAppConfigurationAsync(AppConfigurationRequest request, CancellationToken ct = default)
    {
        var config = _adapter.ToAppConfiguration(request);
        return _repository.CreateAppConfigurationAsync(config, ct);
    }

    public async Task UpdateConnectionTagAsync(long credId, string tagName, string tagValue, int modifiedUser, bool isEnabled, CancellationToken ct = default)
    {
        await _repository.UpdateConnectionTagAsync(credId, tagName, tagValue, modifiedUser, isEnabled, ct);
        await _connectionCredentialStore.LoadAsync(ct);
    }

    public async Task<long> CreatePccCredentialAsync(PccCredentialRequest request, CancellationToken ct = default)
        => await CreatePccCredentialSkyopsAsync(request, ct);

    public async Task<long> CreatePccCredentialSkyopsAsync(PccCredentialRequest request, CancellationToken ct = default)
    {
        var credential = _adapter.ToPccCredential(request);
        var id = await _repository.CreatePccCredentialSkyopsAsync(credential, ct);
        await _credentialStore.LoadAsync(ct);
        return id;
    }

    public async Task<bool> UpdatePccCredentialAsync(long credId, PccCredentialRequest request, CancellationToken ct = default)
    {
        var credential = _adapter.ToPccCredential(request, credId);
        var result = await _repository.UpdatePccCredentialAsync(credId, credential, ct);
        if (result) await _credentialStore.LoadAsync(ct);
        return result;
    }

    public async Task<bool> SetPccCredentialStatusAsync(long credId, int recordStatus, int modifiedUser, CancellationToken ct = default)
    {
        var result = await _repository.SetPccCredentialStatusAsync(credId, recordStatus, modifiedUser, ct);
        if (result) await _credentialStore.LoadAsync(ct);
        return result;
    }

    private async Task<T> ReloadAfterChangeAsync<T>(Task<T> operation, CancellationToken ct)
    {
        var result = await operation;
        if (result is bool updated && !updated) return result;
        await _credentialStore.LoadAsync(ct);
        await _connectionCredentialStore.LoadAsync(ct);
        return result;
    }
}
