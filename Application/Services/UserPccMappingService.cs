using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public class UserPccMappingService : IUserPccMappingService
{
    private readonly IUserPccMappingRepository _repo;

    public UserPccMappingService(IUserPccMappingRepository repo) => _repo = repo;

    public Task<IReadOnlyList<UserPccMapping>> GetAllAsync(CancellationToken ct = default)
        => _repo.GetAllAsync(ct);

    public Task<IReadOnlyList<UserPccMapping>> GetByUserIdAsync(int userId, CancellationToken ct = default)
        => _repo.GetByUserIdAsync(userId, ct);

    public Task<long> CreateAsync(UserPccMappingRequest request, CancellationToken ct = default)
        => _repo.CreateAsync(new UserPccMapping
        {
            UserId = request.UserId,
            PccCode = request.PccCode,
            AccessType = request.AccessType,
            IsActive = request.IsActive,
            CreatedBy = request.ModifiedBy,
            ModifiedBy = request.ModifiedBy
        }, ct);

    public Task<bool> UpdateAsync(long id, UserPccMappingRequest request, CancellationToken ct = default)
        => _repo.UpdateAsync(new UserPccMapping
        {
            Id = id,
            UserId = request.UserId,
            PccCode = request.PccCode,
            AccessType = request.AccessType,
            IsActive = request.IsActive,
            ModifiedBy = request.ModifiedBy
        }, ct);
}
