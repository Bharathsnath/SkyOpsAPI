using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IPriorityPnrRepository
{
    Task<long> AddAsync(PriorityPnrEntry entry, CancellationToken ct = default);
    Task<bool> UpdateAsync(long id, PriorityPnrEntry entry, CancellationToken ct = default);
    Task<IReadOnlyList<PriorityPnrEntry>> GetAllAsync(CancellationToken ct = default);
    Task<PriorityPnrEntry?> GetByPnrAsync(string pnr, CancellationToken ct = default);
    Task<PriorityPnrEntry?> GetByRemarkEmailAsync(string email, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, int modifiedBy, CancellationToken ct = default);
}
