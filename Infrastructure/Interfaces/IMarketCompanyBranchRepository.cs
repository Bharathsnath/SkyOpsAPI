using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IMarketCompanyBranchRepository
{
    Task<IReadOnlyList<MarketMaster>> GetMarketsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CompanyMaster>> GetCompaniesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<BranchMaster>> GetBranchesAsync(CancellationToken ct = default);
}
