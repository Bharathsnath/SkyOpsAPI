using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[ApiController]
[Authorize]
[Route("api")]
public sealed class EmailController : ControllerBase
{
    private readonly ISmtpService smtp;
    private readonly ICrmService crmService;
    private readonly IDataProtector protector;
    private readonly IConfiguration configuration;

    public EmailController(ISmtpService smtp, ICrmService crmService, IDataProtectionProvider provider, IConfiguration configuration)
    { this.smtp = smtp; this.crmService = crmService; protector = provider.CreateProtector("SkyOps.Crm.SmtpPassword.v1"); this.configuration = configuration; }

    [HttpPost("email/send")]
    public async Task<IActionResult> Send(SmtpSendRequest request, CancellationToken ct) { await smtp.SendAsync(request, ct); return Accepted(); }

    [HttpPost("settings/smtp/test")]
    public async Task<IActionResult> Test(SmtpConfigurationRequest request, CancellationToken ct) { await smtp.TestConnectionAsync(request, ct); return Ok(new { success = true }); }

    [HttpGet("settings/smtp")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var value = await crmService.GetSmtpConfigurationAsync(ct);
        if (value is null)
        {
            var section = configuration.GetSection("EmailNotification");
            if (string.IsNullOrWhiteSpace(section["SmtpHost"])) return NotFound();
            return Ok(new { Provider = "Custom SMTP", Host = section["SmtpHost"], Port = section.GetValue<int>("SmtpPort"), Username = section["Username"], FromEmail = section["FromAddress"], FromName = section["FromName"], UseSsl = section.GetValue<bool>("UseSsl"), hasPassword = !string.IsNullOrWhiteSpace(section["Password"]) });
        }
        return Ok(new { value.Provider, value.Host, value.Port, value.Username, value.FromEmail, value.FromName, value.UseSsl, hasPassword = true });
    }

    [HttpPut("settings/smtp")]
    public async Task<IActionResult> Save(SmtpConfigurationRequest request, CancellationToken ct)
    {
        var current = await crmService.GetSmtpConfigurationAsync(ct);
        var password = request.Password ?? (current is null ? configuration.GetSection("EmailNotification")["Password"] : protector.Unprotect(current.EncryptedPassword));
        if (string.IsNullOrWhiteSpace(password)) return BadRequest(new { message = "Password is required when SMTP has not been configured." });
        await crmService.SaveSmtpConfigurationAsync(new SmtpConfigurationRecord(0, request.Provider, request.Host, request.Port, request.Username, protector.Protect(password), request.FromEmail, request.FromName, request.UseSsl), ct);
        return NoContent();
    }
}
