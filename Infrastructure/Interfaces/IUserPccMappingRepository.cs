using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IUserPccMappingRepository
{
    Task<IReadOnlyList<UserPccMapping>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserPccMapping>> GetByUserIdAsync(int userId, CancellationToken ct = default);
    Task<long> CreateAsync(UserPccMapping mapping, CancellationToken ct = default);
    Task<bool> UpdateAsync(UserPccMapping mapping, CancellationToken ct = default);
}
