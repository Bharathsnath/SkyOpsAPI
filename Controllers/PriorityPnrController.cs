using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[Authorize]
[ApiController]
[Route("api/priority-pnr")]
public class PriorityPnrController : ControllerBase
{
    private readonly IPriorityPnrRepository _repo;
    private readonly IEmailNotificationService _emailService;
    private readonly IUserService _userService;

    public PriorityPnrController(
        IPriorityPnrRepository repo,
        IEmailNotificationService emailService,
        IUserService userService)
    {
        _repo = repo;
        _emailService = emailService;
        _userService = userService;
    }

    /// <summary>Add a priority PNR.</summary>
    [HttpPost]
    public async Task<IActionResult> Add([FromBody] PriorityPnrEntry entry, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entry.Pnr))
            return BadRequest(new { message = "Pnr is required." });
        if (entry.CreatedBy == 0)
            return BadRequest(new { message = "CreatedBy (user ID) is required." });

        var id = await _repo.AddAsync(entry, ct);

        var emails = (entry.NotifyEmail ?? string.Empty)
            .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .ToList();
        if (emails.Count > 0)
            _ = Task.Run(() => _emailService.SendPriorityPnrRegistrationAsync(entry.Pnr.ToUpperInvariant(), emails), CancellationToken.None);

        return Ok(new { id, pnr = entry.Pnr.ToUpperInvariant(), message = "Priority PNR saved." });
    }

    /// <summary>Update an existing priority PNR.</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] PriorityPnrEntry entry, CancellationToken ct)
    {
        var updated = await _repo.UpdateAsync(id, entry, ct);
        return updated ? Ok(new { message = "Updated." }) : NotFound(new { message = "Priority PNR not found." });
    }

    /// <summary>List all active priority PNRs.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var list = await _repo.GetAllAsync(ct);
        var users = await _userService.GetAllAsync(ct: ct);

        return Ok(new
        {
            priorityPnrs = list,
            users = users.Select(user => new
            {
                user.Id,
                user.Username,
                user.IsActive
            })
        });
    }

    /// <summary>Get a single priority PNR by PNR code.</summary>
    [HttpGet("{pnr}")]
    public async Task<IActionResult> GetByPnr(string pnr, CancellationToken ct)
    {
        var entry = await _repo.GetByPnrAsync(pnr, ct);
        return entry is null ? NotFound(new { message = $"Priority PNR '{pnr}' not found." }) : Ok(entry);
    }

    /// <summary>Soft-delete a priority PNR by ID.</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, [FromQuery] int modifiedBy, CancellationToken ct)
    {
        if (modifiedBy == 0)
            return BadRequest(new { message = "modifiedBy (user ID) is required." });

        var deleted = await _repo.DeleteAsync(id, modifiedBy, ct);
        return deleted ? Ok(new { message = "Deleted." }) : NotFound(new { message = "Not found." });
    }
}
