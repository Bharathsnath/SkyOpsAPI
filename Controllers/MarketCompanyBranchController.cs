using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[Authorize]
[ApiController]
[Route("api/market-company-branch")]
public class MarketCompanyBranchController : ControllerBase
{
    private readonly IMarketCompanyBranchService _service;

    public MarketCompanyBranchController(IMarketCompanyBranchService service)
    {
        _service = service;
    }

    [HttpGet("markets")]
    public async Task<IActionResult> GetMarkets(CancellationToken ct)
        => Ok(await _service.GetMarketsAsync(ct));

    [HttpGet("companies")]
    public async Task<IActionResult> GetCompanies(CancellationToken ct)
        => Ok(await _service.GetCompaniesAsync(ct));

    [HttpGet("branches")]
    public async Task<IActionResult> GetBranches(CancellationToken ct)
        => Ok(await _service.GetBranchesAsync(ct));
}
