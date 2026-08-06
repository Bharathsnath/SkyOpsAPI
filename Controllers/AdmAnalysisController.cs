using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;

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
        var d = await _service.GetDashboardAsync(cancellationToken);
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
