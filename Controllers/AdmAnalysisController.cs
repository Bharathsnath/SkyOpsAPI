using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;
using System.Security.Claims;

namespace SkyOpsQueueIntelligence.Controllers;

[ApiController]
[Route("api/adm-analysis")]
public class AdmAnalysisController : ControllerBase
{
    private readonly IAdmAnalysisService _service;

    public AdmAnalysisController(IAdmAnalysisService service)
    {
        _service = service;
    }

    private int GetUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    [HttpPost("run")]
    public async Task<IActionResult> Run(CancellationToken cancellationToken)
    {
        await _service.RunAnalysisAsync(cancellationToken);
        return Accepted(new { message = "ADM analysis started" });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var data = await _service.GetAllAsync(cancellationToken);
        return Ok(data);
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        var d = await _service.GetDashboardAsync(GetUserId(), cancellationToken);
        return Ok(d);
    }

    [HttpGet("adm-dashboard")]
    public async Task<IActionResult> AdmDashboard(CancellationToken cancellationToken)
    {
        var d = await _service.GetAdmDashboardAsync(cancellationToken);
        return Ok(d);
    }

    [HttpGet("{pnr}")]
    public async Task<IActionResult> GetByPnr(string pnr, CancellationToken cancellationToken)
    {
        var item = await _service.GetByPnrAsync(pnr, cancellationToken);
        if (item == null) return NotFound();
        return Ok(item);
    }
}
