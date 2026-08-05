using System.Security.Claims;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IErrorLogService
{
    Task LogAsync(
        Exception exception,
        string workflow,
        string moduleName,
        string moduleCode,
        string? procedureName = null,
        string? className = null,
        HttpContext? httpContext = null,
        CancellationToken cancellationToken = default);
}
