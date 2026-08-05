using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[Authorize]
[ApiController]
[Route("api/settings")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _service;
    private readonly IConnectionCredentialStore _connectionCredentialStore;
    private readonly ICredentialStore _credentialStore;

    public SettingsController(ISettingsService service, IConnectionCredentialStore connectionCredentialStore, ICredentialStore credentialStore)
    {
        _service = service;
        _connectionCredentialStore = connectionCredentialStore;
        _credentialStore = credentialStore;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
        => Ok(await _service.GetAllAsync(ct));

    [HttpGet("category/{category}")]
    public async Task<IActionResult> GetByCategory(string category, CancellationToken ct)
        => Ok(await _service.GetByCategoryAsync(category, ct));

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id, CancellationToken ct)
    {
        var config = await _service.GetByIdAsync(id, ct);
        return config is null ? NotFound() : Ok(config);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] PccCredentialRequest request, CancellationToken ct)
    {
        var id = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id, message = "Created in SkyOps DB" });
    }

    [HttpPost("pcc-agent-email-master")]
    public async Task<IActionResult> CreatePccAgentEmailMaster([FromBody] PccAgentEmailMasterRequest request, CancellationToken ct)
    {
        var id = await _service.CreatePccAgentEmailMasterAsync(request, ct);
        return Ok(new { id, message = "Upserted" });
    }

    [HttpGet("pcc-agent-email-master")]
    public async Task<IActionResult> GetPccAgentEmailMaster([FromQuery] string? pcc, CancellationToken ct)
    {
        var data = string.IsNullOrWhiteSpace(pcc)
            ? await _service.GetPccAgentEmailMastersAsync(ct)
            : await _service.GetPccAgentEmailMastersByPccAsync(pcc, ct);

        return Ok(data);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] AppConfigurationRequest request, CancellationToken ct)
    {
        request.Id = id;
        return await _service.UpdateAsync(request, ct) ? Ok(new { message = "Updated" }) : NotFound();
    }

    [HttpGet("logging")]
    public async Task<IActionResult> GetLoggingConfigurations(CancellationToken ct)
        => Ok(await _service.GetLoggingConfigurationsAsync(ct));

    [HttpPut("logging/{configKey}")]
    public async Task<IActionResult> UpdateLoggingConfiguration(string configKey, [FromBody] LoggingConfigRequest request, CancellationToken ct)
    {
        await _service.UpdateConfigurationAsync(configKey, request.Enabled, request.ModifiedUser, ct);
        return Ok(new { configKey, enabled = request.Enabled, message = request.Enabled ? "Enabled" : "Disabled" });
    }

    [HttpPost("app-configuration")]
    public async Task<IActionResult> CreateAppConfiguration([FromBody] AppConfigurationRequest request, CancellationToken ct)
    {
        var id = await _service.CreateAppConfigurationAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
        => await _service.DeleteAsync(id, ct) ? Ok(new { message = "Deleted" }) : NotFound();

    [HttpGet("pcc-credentials")]
    public async Task<IActionResult> GetPccCredentials([FromQuery] string? pcc, CancellationToken ct)
    {
        var data = string.IsNullOrWhiteSpace(pcc)
            ? await _service.GetPccCredentialsAsync(ct)
            : await _service.GetPccCredentialsByPccAsync(pcc, ct);

        return Ok(data.Select(c => new
        {
            c.Cred_ID,
            sourceDb = c.SourceDb,
            c.PCCMasterCode,
            c.Provider,
            c.ServiceType,
            c.SectorType,
            c.TagName,
            c.TagValue,
            c.RecordStatus
        }));
    }

    [HttpGet("pcc-list")]
    public async Task<IActionResult> GetPccList(CancellationToken ct)
    {
        var data = await _service.GetPccListAsync(ct);
        return Ok(data);
    }

    [HttpPost("pcc-credentials")]
    public async Task<IActionResult> CreatePccCredential([FromBody] PccCredentialRequest request, CancellationToken ct)
    {
        var id = await _service.CreatePccCredentialAsync(request, ct);
        return Ok(new { credId = id, message = "Created in SkyOps DB" });
    }

    [HttpPost("pcc-credentials/skyops")]
    public async Task<IActionResult> CreatePccCredentialSkyops([FromBody] PccCredentialRequest request, CancellationToken ct)
    {
        var id = await _service.CreatePccCredentialSkyopsAsync(request, ct);
        return Ok(new { credId = id, message = "Created in SkyOps DB" });
    }

    [HttpPut("pcc-credentials/{credId:long}")]
    public async Task<IActionResult> UpdatePccCredential(long credId, [FromBody] PccCredentialRequest request, CancellationToken ct)
    {
        return await _service.UpdatePccCredentialAsync(credId, request, ct)
            ? Ok(new { message = "Updated" })
            : NotFound();
    }

    [HttpPatch("pcc-credentials/{credId:long}/status")]
    public async Task<IActionResult> SetPccCredentialStatus(long credId, [FromBody] PccCredentialStatusRequest request, CancellationToken ct)
    {
        return await _service.SetPccCredentialStatusAsync(credId, request.RecordStatus, request.ModifiedUser, ct)
            ? Ok(new { message = request.RecordStatus == 0 ? "Enabled" : "Disabled" })
            : NotFound();
    }

    [HttpGet("connection-credentials")]
    public IActionResult GetConnectionCredentials()
    {
        var all = _connectionCredentialStore.GetAll();
        var providers = all
            .GroupBy(c => c.PCCMasterCode.ToUpperInvariant())
            .Select(g => new
            {
                name = g.Key switch
                {
                    "MASTER" => "Master DB",
                    "TRANSACTION" => "Transaction DB",
                    "LOG" => "Log DB",
                    _ => g.Key
                },
                category = g.Key,
                isEnabled = g.All(c => c.RecordStatus == 0),
                credentials = g.Select(c => new
                {
                    credId = c.Cred_ID,
                    tagName = c.TagName,
                    tagValue = c.TagValue
                })
            });
        return Ok(providers);
    }

    [HttpPut("connection-credentials/{category}")]
    public async Task<IActionResult> UpdateConnectionCredential(
        string category,
        [FromBody] ConnectionCredentialUpdateRequest request,
        CancellationToken ct)
    {
        var credentials = _connectionCredentialStore.GetByPcc(category);
        if (credentials.Count == 0) return NotFound();

        foreach (var update in request.Credentials)
        {
            await _service.UpdateConnectionTagAsync(update.CredId, update.TagName, update.TagValue, request.ModifiedUser, request.IsEnabled, ct);
        }

        await _connectionCredentialStore.LoadAsync(ct);
        return Ok(new { message = "Updated" });
    }
}
