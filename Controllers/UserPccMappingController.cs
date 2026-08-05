using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[Authorize]
[ApiController]
[Route("api/user-pcc-mapping")]
public class UserPccMappingController : ControllerBase
{
    private readonly IUserPccMappingService _service;

    public UserPccMappingController(IUserPccMappingService service) => _service = service;

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("user/{userId:int}")]
    public async Task<IActionResult> GetByUserId(int userId, CancellationToken ct)
        => Ok(await _service.GetByUserIdAsync(userId, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] UserPccMappingRequest request, CancellationToken ct)
    {
        var id = await _service.CreateAsync(request, ct);
        return Ok(new { id, message = "Created" });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UserPccMappingRequest request, CancellationToken ct)
        => await _service.UpdateAsync(id, request, ct) ? Ok(new { message = "Updated" }) : NotFound();
}
