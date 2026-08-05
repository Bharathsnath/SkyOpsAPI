using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IMarketCompanyBranchService
{
    Task<IReadOnlyList<MarketMaster>> GetMarketsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CompanyMaster>> GetCompaniesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BranchMaster>> GetBranchesAsync(CancellationToken ct = default);
}
