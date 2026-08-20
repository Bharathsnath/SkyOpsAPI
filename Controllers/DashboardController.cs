using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[Authorize]
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;
    private readonly IQueueService _queueService;
    private readonly IQueue7PollingTrigger _pollingTrigger;

    public DashboardController(IDashboardService dashboardService, IQueueService queueService, IQueue7PollingTrigger pollingTrigger)
    {
        _dashboardService = dashboardService;
        _queueService = queueService;
        _pollingTrigger = pollingTrigger;
    }

    [HttpGet("executive")]
    public async Task<IActionResult> Executive(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized(new { message = "The authenticated user ID is missing or invalid." });

        return Ok(await _dashboardService.GetExecutiveDashboardAsync(userId, ct));
    }

    [HttpGet("queue-performance")]
    public async Task<IActionResult> QueuePerformance(int? queue, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetQueuePerformanceAsync(userId, queue, ct));
    }

    [HttpGet("pcc-performance")]
    public async Task<IActionResult> PccPerformance(string? pcc, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetPccPerformanceAsync(userId, pcc, ct));
    }

    [HttpGet("flight-status")]
    public async Task<IActionResult> FlightStatus(string? status, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetFlightStatusAsync(userId, status, ct));
    }

    [HttpGet("critical")]
    public async Task<IActionResult> Critical(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetCriticalQueueAsync(userId, ct));
    }

    [HttpGet("delay-analysis")]
    public async Task<IActionResult> DelayAnalysis(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetDelayAnalysisAsync(userId, ct));
    }

    [HttpGet("delay-analysis/{pnr}")]
    public async Task<IActionResult> DelayAnalysisByPnr(string pnr, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pnr))
            return BadRequest(new { message = "PNR is required." });

        var result = await _queueService.GetDelayAnalysisByPnrAsync(pnr.ToUpperInvariant(), ct);
        return result is null
            ? NotFound(new { pnr, message = "No delay analysis found for this PNR." })
            : Ok(result);
    }

    [HttpGet("flight-impact")]
    public async Task<IActionResult> FlightImpact(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetFlightImpactAsync(userId, ct));
    }

    [HttpGet("pnr-analysis")]
    public async Task<IActionResult> PnrAnalysis(string? pnr, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetPnrAnalysisAsync(userId, pnr, ct));
    }

    [HttpGet("pnrs")]
    public async Task<IActionResult> Pnrs(string? pnr, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetPnrsAsync(userId, pnr, ct));
    }

    [HttpGet("operational")]
    public async Task<IActionResult> Operational(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetOperationalDashboardAsync(userId, ct));
    }

    [HttpGet("management")]
    public async Task<IActionResult> Management(CancellationToken ct)
        => Ok(await _dashboardService.GetManagementDashboardAsync(ct));

    [HttpGet("xml-logs")]
    public async Task<IActionResult> XmlLogs(CancellationToken ct)
        => Ok(await _dashboardService.GetXmlLogsAsync(ct));

    [HttpGet("error-logs")]
    public async Task<IActionResult> ErrorLogs(CancellationToken ct)
        => Ok(await _dashboardService.GetErrorLogsAsync(ct));

    [HttpGet("action-Taken")]
    public async Task<IActionResult> ActionTaken(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        return Ok(await _dashboardService.GetActionTakenAsync(userId, ct));
    }

    [HttpGet("priority-pnr-status")]
    public async Task<IActionResult> PriorityPnrStatus(CancellationToken ct)
        => Ok(await _dashboardService.GetPriorityPnrStatusAsync(ct));

    [HttpGet("debug-access-filter")]
    public async Task<IActionResult> DebugAccessFilter(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var info = await _dashboardService.GetAccessFilterDebugAsync(userId, ct);
        return Ok(info);
    }

    private bool TryGetUserId(out int userId)
        => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    [HttpPost("process-queue")]
    public async Task<IActionResult> ProcessQueue(CancellationToken ct)
    {
        await _pollingTrigger.TriggerAsync(ct);
        return Ok(new { message = "Queue Processed successfully." });
    }

    [HttpPatch("queue-actions/remarks")]
    public async Task<IActionResult> UpdateRemarks([FromBody] UpdateRemarksByKeyRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { message = "Remarks payload is required." });

        var pnr = request.Pnr?.Trim().ToUpperInvariant() ?? string.Empty;
        var remarks = request.Remarks?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pnr))
            return BadRequest(new { message = "Pnr and Remarks are required." });

        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "The authenticated user ID is missing or invalid." });

        var updated = await _queueService.UpdateRemarksAsync(pnr, 0, "", "", remarks, userId, cancellationToken);
        if (!updated)
            return NotFound(new { pnr, message = "No queue action row was found for the supplied PNR." });

        return Ok(new { pnr, remarks, actionTaken = 0, message = "Remarks updated successfully." });
    }

    
}
