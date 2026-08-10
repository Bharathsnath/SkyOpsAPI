using SkyOpsQueueIntelligence.Application.DTO.Request;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface ISabreCommandService
{
    Task<SabreCommandResponse> ExecuteEwrAsync(SabreCommandRequest request, CancellationToken cancellationToken = default);
    Task<SabreCommandResponse> ExecuteQrAsync(SabreCommandRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<SabreCommandResponse>> ExecuteBothAsync(SabreCommandRequest request, CancellationToken cancellationToken = default);
    Task<IEnumerable<SabreCommandResponse>> ExecuteQueueAsync(SabreQueueProcessRequest request, CancellationToken cancellationToken = default);
    Task<string> ExecuteHostCommandAsync(string officeId, string hostCommand, CancellationToken cancellationToken = default, string moduleName = "SabreQueueMCP", string moduleCode = "QUEUE");
    Task<IReadOnlyList<string>> ExecutePagedHostCommandAsync(string officeId, string firstCommand, string nextPageCommand, string endMarker, int maxPages, CancellationToken cancellationToken = default, string moduleName = "SabreQueueMCP", string moduleCode = "QUEUE");
    Task<IReadOnlyList<string>> ExecuteSequentialCommandsAsync(string officeId, IEnumerable<string> commands, CancellationToken cancellationToken = default, string moduleName = "SabreQueueMCP", string moduleCode = "QUEUE", string? pnr = null);
}
