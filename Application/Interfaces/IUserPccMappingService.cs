using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Request;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IUserPccMappingService
{
    Task<IReadOnlyList<UserPccMapping>> GetAllAsync(CancellationToken ct = default);
    Task<IReadOnlyList<UserPccMapping>> GetByUserIdAsync(int userId, CancellationToken ct = default);
    Task<long> CreateAsync(UserPccMappingRequest request, CancellationToken ct = default);
    Task<bool> UpdateAsync(long id, UserPccMappingRequest request, CancellationToken ct = default);
}
