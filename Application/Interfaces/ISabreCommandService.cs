using SkyOpsQueueIntelligence.Application.DTO.Request;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface ISabreCommandService
{
    Task<SabreCommandResponse> ExecuteEwrAsync(SabreCommandRequest request, CancellationToken cancellationToken = default);
    Task<SabreCommandResponse> ExecuteQrAsync(SabreCommandRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<SabreCommandResponse>> ExecuteBothAsync(SabreCommandRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<SabreCommandResponse>> ExecuteQueueAsync(SabreQueueProcessRequest request, CancellationToken cancellationToken = default);
}
