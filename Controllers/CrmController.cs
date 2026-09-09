using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Hubs;

namespace SkyOpsQueueIntelligence.Controllers;

[ApiController]
[Route("api/crm")]
[Authorize]
public sealed class CrmController : ControllerBase
{
    private readonly ICrmService crmService;
    private readonly ICrmActionTokenService tokens;
    private readonly IHubContext<CrmHub> hub;

    public CrmController(ICrmService crmService, ICrmActionTokenService tokens, IHubContext<CrmHub> hub) { this.crmService = crmService; this.tokens = tokens; this.hub = hub; }

    [HttpGet("emails")]
    public Task<IReadOnlyList<Application.DTO.CrmEmailDto>> GetEmails([FromQuery] CrmSearchRequest request, CancellationToken ct) => crmService.GetEmailsAsync(request, ct);

    [HttpGet("emails/{id:long}")]
    public async Task<IActionResult> GetEmail(long id, CancellationToken ct) => (await crmService.GetEmailAsync(id, ct)) is { } email ? Ok(email) : NotFound();

    [HttpGet("cases/{caseId}")]
    public async Task<IActionResult> GetCase(string caseId, CancellationToken ct) => (await crmService.GetCaseAsync(caseId, ct)) is { } value ? Ok(value) : NotFound();

    [HttpGet("cases/{caseId}/timeline")]
    public Task<IReadOnlyList<CrmTimelineItemDto>> GetTimeline(string caseId, CancellationToken ct) => crmService.GetTimelineAsync(caseId, ct);

    [HttpPost("cases/{caseId}/actions")]
    public async Task<IActionResult> RecordAction(string caseId, [FromBody] CrmActionRequest request, CancellationToken ct)
    {
        var actorType = ActorType();
        if (!IsAllowed(actorType, request.ActionCode)) return Forbid();
        var item = await crmService.AddActionAsync(caseId, null, actorType, ActorId(), request, ct);
        await hub.Clients.Group($"crm:{caseId}").SendAsync("ReceiveAction", new { caseId, item }, ct);
        await hub.Clients.Group($"crm:{caseId}").SendAsync("CaseUpdated", new { caseId, actionCode = request.ActionCode }, ct);
        return Ok(item);
    }

    [HttpPost("cases/{caseId}/messages")]
    public async Task<IActionResult> AddMessage(string caseId, [FromBody] CrmMessageRequest request, CancellationToken ct)
    {
        var item = await crmService.AddMessageAsync(caseId, "AGENT", ActorId(), request.Message, ct);
        await hub.Clients.Group($"crm:{caseId}").SendAsync("ReceiveMessage", new { caseId, item }, ct);
        return Ok(item);
    }

    [HttpGet("customers/{customerId:long}")]
    public async Task<IActionResult> GetCustomer(long customerId, CancellationToken ct) => (await crmService.GetCustomerAsync(customerId, ct)) is { } value ? Ok(value) : NotFound();

    [HttpPost("cases/{caseId}/action-links")]
    public async Task<IActionResult> CreateActionLink(string caseId, ActionLinkRequest request, CancellationToken ct)
    {
        var token = await tokens.CreateAsync(caseId, request.Recipient, request.RecipientType, request.AllowedActions, TimeSpan.FromMinutes(Math.Clamp(request.ExpiresMinutes, 1, 1440)), ct);
        return Ok(new { token, expiresMinutes = request.ExpiresMinutes });
    }

    private string? ActorId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
    private string ActorType() => User.IsInRole("Operations") ? "OPERATIONS" : User.IsInRole("Customer") ? "CUSTOMER" : "AGENT";
    private static bool IsAllowed(string actor, string action) => actor switch
    {
        "CUSTOMER" => new[] { "ACCEPT", "ALTERNATE_FLIGHT", "CANCEL_REQUEST", "CONTACT_AGENT", "CONFIRM", "REJECT" }.Contains(action, StringComparer.OrdinalIgnoreCase),
        "OPERATIONS" => new[] { "REBOOK", "TICKET", "CANCEL", "QUEUE_ACTION", "COMPLETE_OPERATION" }.Contains(action, StringComparer.OrdinalIgnoreCase),
        _ => new[] { "ACCEPT", "RESCHEDULE", "CANCEL", "REQUEST_MORE_DETAILS", "SEND_TO_OPERATIONS", "CONTACT_CUSTOMER", "ADD_FOLLOW_UP", "CLOSE_CASE", "ALTERNATE_FLIGHT" }.Contains(action, StringComparer.OrdinalIgnoreCase)
    };

    public sealed record ActionLinkRequest(string Recipient, string RecipientType, IReadOnlyCollection<string> AllowedActions, int ExpiresMinutes = 60);
}

[ApiController]
[Route("api/customer-action")]
[AllowAnonymous]
public sealed class CustomerActionController : ControllerBase
{
    private readonly ICrmActionTokenService tokens;
    private readonly ICrmService crmService;
    private readonly IHubContext<CrmHub> hub;
    public CustomerActionController(ICrmActionTokenService tokens, ICrmService crmService, IHubContext<CrmHub> hub) { this.tokens = tokens; this.crmService = crmService; this.hub = hub; }

    [HttpPost("{token}")]
    public async Task<IActionResult> Apply(string token, CustomerActionRequest request, CancellationToken ct)
    {
        var tokenData = await tokens.ConsumeAsync(token, request.ActionCode, ct);
        if (tokenData is null) return BadRequest(new { message = "The action link is invalid, expired, already used, or not permitted." });
        var item = await crmService.AddActionAsync(tokenData.CaseNumber, null, tokenData.RecipientType, tokenData.Recipient, new CrmActionRequest(request.ActionCode, "SECURE_EMAIL_LINK", request.ActionData), ct);
        await hub.Clients.Group($"crm:{tokenData.CaseNumber}").SendAsync("ReceiveAction", new { caseId = tokenData.CaseNumber, item }, ct);
        return Ok(item);
    }

    public sealed record CustomerActionRequest(string ActionCode, object? ActionData = null);
}