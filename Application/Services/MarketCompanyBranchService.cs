using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public sealed class MarketCompanyBranchService : IMarketCompanyBranchService
{
    private readonly IMarketCompanyBranchRepository _repository;

    private IReadOnlyList<MarketMaster>? _markets;
    private IReadOnlyList<CompanyMaster>? _companies;
    private IReadOnlyList<BranchMaster>? _branches;

    private readonly SemaphoreSlim _lock = new(1, 1);

    public MarketCompanyBranchService(IMarketCompanyBranchRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<MarketMaster>> GetMarketsAsync(CancellationToken ct = default)
    {
        if (_markets is not null) return _markets;
        await _lock.WaitAsync(ct);
        try { return _markets ??= await _repository.GetMarketsAsync(ct); }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<CompanyMaster>> GetCompaniesAsync(CancellationToken ct = default)
    {
        if (_companies is not null) return _companies;
        await _lock.WaitAsync(ct);
        try { return _companies ??= await _repository.GetCompaniesAsync(ct); }
        finally { _lock.Release(); }
    }

    public async Task<IReadOnlyList<BranchMaster>> GetBranchesAsync(CancellationToken ct = default)
    {
        if (_branches is not null) return _branches;
        await _lock.WaitAsync(ct);
        try { return _branches ??= await _repository.GetBranchesAsync(ct); }
        finally { _lock.Release(); }
    }
}
