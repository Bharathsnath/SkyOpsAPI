using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[AllowAnonymous]
[ApiController]
public class PnrDetailsController : ControllerBase
{
    private readonly IQueueService _queueService;
    private readonly IEmailNotificationService _emailService;

    public PnrDetailsController(IQueueService queueService, IEmailNotificationService emailService)
    {
        _queueService = queueService;
        _emailService = emailService;
    }

    [HttpPost("/test-email")]
    public async Task<IActionResult> SendTestEmail(CancellationToken ct)
    {
        try
        {
            await _emailService.SendTestEmailAsync(ct);
            return Ok(new { message = "Test email sent successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Test email failed to send.", error = ex.Message });
        }
    }

    [HttpGet("api/pnr-details/{pnr}")]
    public async Task<IActionResult> GetPnrDetails(string pnr, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pnr))
            return BadRequest("PNR is required.");

        var data = await _queueService.GetDelayAnalysisByPnrAsync(pnr.ToUpperInvariant(), ct);
        if (data is null)
            return NotFound($"No data found for PNR: {pnr}");

        return Ok(data);
    }

    [HttpPatch("api/queue-actions/Agentremarks")]
    public async Task<IActionResult> UpdateAgentRemarks([FromBody] UpdateRemarksByKeyRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
            return BadRequest(new { message = "Remarks payload is required." });

        var pnr = request.Pnr?.Trim().ToUpperInvariant() ?? string.Empty;
        var remarks = request.Remarks?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(pnr))
            return BadRequest(new { message = "Pnr and Remarks are required." });

        var updated = await _queueService.UpdateAgentRemarksAsync(pnr, 0, "", "", remarks, cancellationToken);
        if (!updated)
            return NotFound(new { pnr, message = "No queue action row was found for the supplied PNR." });

        return Ok(new { pnr, remarks, actionTaken = 0, message = "Remarks updated successfully." });
    }

}