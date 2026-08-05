using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[Authorize]
[ApiController]
[Route("api/sabre/execute")]
public sealed class SabreCommandController : ControllerBase
{
    private readonly ISabreCommandService _sabreCommandService;

    public SabreCommandController(ISabreCommandService sabreCommandService)
    {
        _sabreCommandService = sabreCommandService;
    }

    [HttpPost("ewr")]
    public async Task<IActionResult> ExecuteEwr([FromBody] SabreCommandRequest request, CancellationToken cancellationToken)
        => await HandleAsync(() => _sabreCommandService.ExecuteEwrAsync(request, cancellationToken));

    [HttpPost("qr")]
    public async Task<IActionResult> ExecuteQr([FromBody] SabreCommandRequest request, CancellationToken cancellationToken)
        => await HandleAsync(() => _sabreCommandService.ExecuteQrAsync(request, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> ExecuteBoth([FromBody] SabreCommandRequest request, CancellationToken cancellationToken)
        => await HandleAsync(() => _sabreCommandService.ExecuteBothAsync(request, cancellationToken));

    [HttpPost("queue")]
    public async Task<IActionResult> ExecuteQueue([FromBody] SabreQueueProcessRequest request, CancellationToken cancellationToken)
        => await HandleAsync(() => _sabreCommandService.ExecuteQueueAsync(request, cancellationToken));

    private async Task<IActionResult> HandleAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (KeyNotFoundException ex)   { return NotFound(new { message = ex.Message }); }
        catch (InvalidOperationException ex) { return StatusCode(503, new { message = ex.Message }); }
    }
}
