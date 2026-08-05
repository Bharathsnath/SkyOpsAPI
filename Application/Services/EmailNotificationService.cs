using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Hubs;

namespace SkyOpsQueueIntelligence.Application.Services;

public sealed class EmailNotificationService : IEmailNotificationService
{
    private readonly IConfiguration _config;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmailNotificationService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IHubContext<QueueNotificationsHub> _hubContext;
    private readonly IErrorLogService _errorLogService;

    public EmailNotificationService(
        IConfiguration config,
        IServiceScopeFactory scopeFactory,
        ILogger<EmailNotificationService> logger,
        IHttpClientFactory httpClientFactory,
        IHubContext<QueueNotificationsHub> hubContext,
        IErrorLogService errorLogService)
    {
        _config = config;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _hubContext = hubContext;
        _errorLogService = errorLogService;
    }

    public async Task SendAlertAsync(string PCC, IReadOnlyList<QueueAnalysisResult> results, CancellationToken ct = default)
    {
        try
        {
        var section = _config.GetSection("EmailNotification");
        if (!section.GetValue<bool>("Enabled")) return;

        var sendOnCritical = section.GetValue<bool>("SendOnCritical");
        var sendOnTimeChange = section.GetValue<bool>("SendOnTimeChange");
        var baseUrl = section["BaseUrl"] ?? "https://skyopsapibeta.akbartravelsonline.com";
        var toAddresses = await ResolveRecipientsAsync(section, PCC, results, ct);

        var criticalActions = results
            .SelectMany(r => r.Actions.Select(a => (r.Pnr, r.PCC, a)))
            .Where(x => x.a.Status is "HX" or "UN" or "UC")
            .ToList();

        var timeChangeActions = results
            .SelectMany(r => r.Actions.Select(a => (r.Pnr, r.PCC, a)))
            .Where(x => x.a.Status == "TK" && x.a.DelayMinutes is not null && x.a.DelayMinutes != 0)
            .ToList();

        if ((!sendOnCritical || criticalActions.Count == 0) && (!sendOnTimeChange || timeChangeActions.Count == 0))
            return;

        var allActions = new List<(string Pnr, string? PCC, ActionFinding Action)>();
        if (sendOnCritical) allActions.AddRange(criticalActions);
        if (sendOnTimeChange) allActions.AddRange(timeChangeActions);
        var totalRecords = allActions.Select(x => x.Pnr).Distinct().Count();
        var alertTypes = new List<string>();
        if (sendOnCritical && criticalActions.Count > 0) alertTypes.Add("HX · UN · UC");
        if (sendOnTimeChange && timeChangeActions.Count > 0) alertTypes.Add("Schedule / Time Changes (TK Status)");
        var body = BuildEmailBody(PCC, criticalActions, timeChangeActions, sendOnCritical, sendOnTimeChange, baseUrl, totalRecords, alertTypes);
        var subject = $"[SKY OPS] Queue notification of PCC: {PCC}";

        await SendEmailAsync(section, subject, body, toAddresses, ct, throwOnError: false);
        }
        catch (Exception ex) { await _errorLogService.LogAsync(ex, "EmailNotificationService", "SkyOpsQueueIntelligence", "SERVICE", nameof(SendAlertAsync), nameof(EmailNotificationService)); }
    }

    public async Task SendQueueProcessingSummaryAsync(
        string pccCode,
        string displayPcc,
        IReadOnlyList<(string HostCommand, int QueueNumber, int AnalyzedCount, int SavedCount)> queueSummaries,
        CancellationToken ct = default)
    {
        try
        {
        var section = _config.GetSection("WebhookNotification");
        if (!section.GetValue<bool>("Enabled"))
        {
            return;
        }

        var webhookUrl = section["Url"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
        {
            _logger.LogWarning("Webhook notification skipped: no URL configured.");
            return;
        }

        var payload = new
        {
            eventType = "queue_processed_summary",
            pccCode,
            displayPcc,
            processedAtUtc = DateTime.UtcNow,
            totalQueues = queueSummaries.Count,
            totalPnrsAnalyzed = queueSummaries.Sum(x => x.AnalyzedCount),
            totalActionsSaved = queueSummaries.Sum(x => x.SavedCount),
            queues = queueSummaries.Select(x => new
            {
                hostCommand = x.HostCommand,
                queueNumber = x.QueueNumber,
                analyzedCount = x.AnalyzedCount,
                savedCount = x.SavedCount
            })
        };

        try
        {
            var client = _httpClientFactory.CreateClient("WebhookNotification");
            client.Timeout = TimeSpan.FromSeconds(section.GetValue<int>("TimeoutSeconds"));
            var response = await client.PostAsJsonAsync(webhookUrl, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Webhook notification returned {StatusCode} for PCC {Pcc}", response.StatusCode, displayPcc);
            }
            else
            {
                _logger.LogInformation("Webhook notification sent for PCC {Pcc}", displayPcc);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send webhook notification for PCC {Pcc}", displayPcc);
        }

        try
        {
            await _hubContext.Clients.All.SendAsync("QueueProcessingSummary", payload, ct);
            _logger.LogInformation("SignalR notification sent for PCC {Pcc}", displayPcc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send SignalR notification for PCC {Pcc}", displayPcc);
        }
        }
        catch (Exception ex) { await _errorLogService.LogAsync(ex, "EmailNotificationService", "SkyOpsQueueIntelligence", "SERVICE", nameof(SendQueueProcessingSummaryAsync), nameof(EmailNotificationService)); }
    }

    public async Task SendPriorityPnrRegistrationAsync(string pnr, IReadOnlyList<string> toEmails, CancellationToken ct = default)
    {
        try
        {
            var section = _config.GetSection("EmailNotification");
            if (!section.GetValue<bool>("Enabled")) return;
            if (toEmails.Count == 0) return;

            var baseUrl = section["BaseUrl"] ?? "https://skyopsapibeta.akbartravelsonline.com";
            var pnrUrl = $"{baseUrl}/pnr-details/{pnr}";
            var registeredAt = DateTime.UtcNow.ToString("yyyy-MM-dd · HH:mm") + " UTC";

            var body = $"""
                <!DOCTYPE html><html><head><meta charset='utf-8'/></head>
                <body style='margin:0;padding:0;background:#f0f4f8;font-family:Arial,sans-serif;'>
                <table width='100%' cellpadding='0' cellspacing='0' style='background:#f0f4f8;padding:32px 0;'>
                <tr><td align='center'>
                <table width='560' cellpadding='0' cellspacing='0' style='background:#fff;border-radius:14px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.10);'>
                  <tr><td style='background:#1a2744;padding:20px 24px;'>
                    <span style='color:#fff;font-size:18px;font-weight:800;'>&#9992; SkyOps &mdash; Priority PNR Registered</span>
                  </td></tr>
                  <tr><td style='padding:24px;'>
                    <div style='font-size:11px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;margin-bottom:6px;'>PNR Added to Priority Watch</div>
                    <a href='{pnrUrl}' style='font-size:28px;font-weight:900;color:#1a2744;text-decoration:none;letter-spacing:3px;'>{pnr}</a>
                    <div style='margin-top:16px;font-size:13px;color:#555;'>This PNR has been registered for priority monitoring at <strong>{registeredAt}</strong>.</div>
                    <div style='margin-top:8px;font-size:13px;color:#555;'>You will receive an alert whenever this PNR is detected with queue changes during polling.</div>
                    <div style='margin-top:20px;'>
                      <a href='{pnrUrl}' style='background:#1a2744;color:#fff;padding:10px 22px;border-radius:8px;text-decoration:none;font-size:13px;font-weight:700;'>View PNR Details</a>
                    </div>
                  </td></tr>
                  <tr><td style='padding:0 24px 20px 24px;'>
                    <table width='100%' cellpadding='0' cellspacing='0' style='background:#fffbea;border:1px solid #ffe082;border-radius:8px;'>
                      <tr><td style='padding:10px 14px;font-size:12px;color:#555;'>&#9651; Recommendation only. No Sabre commands were executed.</td></tr>
                    </table>
                  </td></tr>
                </table>
                </td></tr></table>
                </body></html>
                """;

            var subject = $"[SKY OPS] Priority PNR Registered: {pnr}";
            await SendEmailAsync(section, subject, body, toEmails, ct, throwOnError: false);
        }
        catch (Exception ex) { await _errorLogService.LogAsync(ex, "EmailNotificationService", "SkyOpsQueueIntelligence", "SERVICE", nameof(SendPriorityPnrRegistrationAsync), nameof(EmailNotificationService)); }
    }

    public async Task SendPriorityPnrAlertAsync(string pnr, IReadOnlyList<string> toEmails, IReadOnlyList<QueueAnalysisResult> results, CancellationToken ct = default)
    {
        try
        {
            var section = _config.GetSection("EmailNotification");
            if (!section.GetValue<bool>("Enabled")) return;
            if (toEmails.Count == 0) return;

            var baseUrl = section["BaseUrl"] ?? "https://skyopsapibeta.akbartravelsonline.com";
            var pnrUrl = $"{baseUrl}/pnr-details/{pnr}";
            var processedAt = DateTime.UtcNow.ToString("yyyy-MM-dd · HH:mm") + " UTC";

            var body = $"""
                <!DOCTYPE html><html><head><meta charset='utf-8'/></head>
                <body style='margin:0;padding:0;background:#f0f4f8;font-family:Arial,sans-serif;'>
                <table width='100%' cellpadding='0' cellspacing='0' style='background:#f0f4f8;padding:32px 0;'>
                <tr><td align='center'>
                <table width='560' cellpadding='0' cellspacing='0' style='background:#fff;border-radius:14px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.10);'>
                  <tr><td style='background:#1a2744;padding:20px 24px;'>
                    <span style='color:#fff;font-size:18px;font-weight:800;'>&#9992; SkyOps &mdash; Priority PNR Alert</span>
                  </td></tr>
                  <tr><td style='padding:24px;'>
                    <div style='font-size:11px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;margin-bottom:6px;'>Priority PNR Detected</div>
                    <a href='{pnrUrl}' style='font-size:28px;font-weight:900;color:#1a2744;text-decoration:none;letter-spacing:3px;'>{pnr}</a>
                    <div style='margin-top:16px;font-size:13px;color:#555;'>This PNR was found in the Sabre queue during polling at <strong>{processedAt}</strong>.</div>
                    <div style='margin-top:8px;font-size:13px;color:#555;'>Total actions flagged: <strong>{results.SelectMany(r => r.Actions).Count()}</strong></div>
                    <div style='margin-top:20px;'>
                      <a href='{pnrUrl}' style='background:#1a2744;color:#fff;padding:10px 22px;border-radius:8px;text-decoration:none;font-size:13px;font-weight:700;'>View PNR Details</a>
                    </div>
                  </td></tr>
                  <tr><td style='padding:0 24px 20px 24px;'>
                    <table width='100%' cellpadding='0' cellspacing='0' style='background:#fffbea;border:1px solid #ffe082;border-radius:8px;'>
                      <tr><td style='padding:10px 14px;font-size:12px;color:#555;'>&#9651; Recommendation only. No Sabre commands were executed.</td></tr>
                    </table>
                  </td></tr>
                </table>
                </td></tr></table>
                </body></html>
                """;

            var subject = $"[SKY OPS] Priority PNR Alert: {pnr}";
            await SendEmailAsync(section, subject, body, toEmails, ct, throwOnError: false);
        }
        catch (Exception ex) { await _errorLogService.LogAsync(ex, "EmailNotificationService", "SkyOpsQueueIntelligence", "SERVICE", nameof(SendPriorityPnrAlertAsync), nameof(EmailNotificationService)); }
    }

    public async Task SendRemarkEmailNotificationAsync(string pnr, string remarkEmail, IReadOnlyList<QueueAnalysisResult> results, CancellationToken ct = default)
    {
        try
        {
            var section = _config.GetSection("EmailNotification");
            if (!section.GetValue<bool>("Enabled")) return;
            if (!section.GetValue<bool>("SendRemarkEmail")) return;  // disabled by default

            var toAddresses = SplitRecipients(remarkEmail).ToArray();
            if (toAddresses.Length == 0) return;

            var baseUrl = section["BaseUrl"] ?? "https://skyopsapibeta.akbartravelsonline.com";
            var pnrUrl = $"{baseUrl}/pnr-details/{pnr}";
            var processedAt = DateTime.UtcNow.ToString("yyyy-MM-dd · HH:mm") + " UTC";
            var actionCount = results.SelectMany(r => r.Actions).Count();

            var body = $"""
                <!DOCTYPE html><html><head><meta charset='utf-8'/></head>
                <body style='margin:0;padding:0;background:#f0f4f8;font-family:Arial,sans-serif;'>
                <table width='100%' cellpadding='0' cellspacing='0' style='background:#f0f4f8;padding:32px 0;'>
                <tr><td align='center'>
                <table width='560' cellpadding='0' cellspacing='0' style='background:#fff;border-radius:14px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.10);'>
                  <tr><td style='background:#1a2744;padding:20px 24px;'>
                    <span style='color:#fff;font-size:18px;font-weight:800;'>&#9992; SkyOps &mdash; Queue Alert</span>
                  </td></tr>
                  <tr><td style='padding:24px;'>
                    <div style='font-size:11px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;margin-bottom:6px;'>PNR</div>
                    <a href='{pnrUrl}' style='font-size:28px;font-weight:900;color:#1a2744;text-decoration:none;letter-spacing:3px;'>{pnr}</a>
                    <div style='margin-top:16px;font-size:13px;color:#555;'>Processed at <strong>{processedAt}</strong>. Actions flagged: <strong>{actionCount}</strong>.</div>
                    <div style='margin-top:20px;'>
                      <a href='{pnrUrl}' style='background:#1a2744;color:#fff;padding:10px 22px;border-radius:8px;text-decoration:none;font-size:13px;font-weight:700;'>View PNR Details</a>
                    </div>
                  </td></tr>
                  <tr><td style='padding:0 24px 20px 24px;'>
                    <table width='100%' cellpadding='0' cellspacing='0' style='background:#fffbea;border:1px solid #ffe082;border-radius:8px;'>
                      <tr><td style='padding:10px 14px;font-size:12px;color:#555;'>&#9651; Recommendation only. No Sabre commands were executed.</td></tr>
                    </table>
                  </td></tr>
                </table>
                </td></tr></table>
                </body></html>
                """;

            await SendEmailAsync(section, $"[SKY OPS] Queue Alert for PNR: {pnr}", body, toAddresses, ct, throwOnError: false);
        }
        catch (Exception ex) { await _errorLogService.LogAsync(ex, "EmailNotificationService", "SkyOpsQueueIntelligence", "SERVICE", nameof(SendRemarkEmailNotificationAsync), nameof(EmailNotificationService)); }
    }

    public async Task SendTestEmailAsync(CancellationToken ct = default)
    {
        var section = _config.GetSection("EmailNotification");
        if (!section.GetValue<bool>("Enabled"))
        {
            throw new InvalidOperationException("Email notifications are disabled in configuration.");
        }

        var toAddresses = section.GetSection("ToAddresses").Get<string[]>() ?? Array.Empty<string>();
        var subject = "[SkyOps Alert] Test Email";
        var body = BuildTestEmailBody(section);

        await SendEmailAsync(section, subject, body, toAddresses, ct, throwOnError: true);
    }

    private static string BuildEmailBody(
        string PCC,
        List<(string Pnr, string? PCC, ActionFinding Action)> critical,
        List<(string Pnr, string? PCC, ActionFinding Action)> timeChanges,
        bool sendOnCritical,
        bool sendOnTimeChange,
        string baseUrl,
        int totalRecords,
        List<string> alertTypes)
    {
        var processedAt = DateTime.UtcNow.ToString("yyyy-MM-dd · HH:mm") + " UTC";
        var alertTypesDisplay = alertTypes.Count > 0 ? string.Join(" · ", alertTypes) : "—";
        var hasCritical = sendOnCritical && critical.Count > 0;
        var hasTimeChange = sendOnTimeChange && timeChanges.Count > 0;

        var badgeHtml = hasCritical
            ? "<span style='background:#fff0f0;color:#d32f2f;border:1.5px solid #d32f2f;border-radius:20px;padding:5px 14px;font-size:12px;font-weight:700;letter-spacing:0.5px;'>&#9679;&nbsp;CRITICAL</span>"
            : "<span style='background:#fff8e1;color:#f57c00;border:1.5px solid #f57c00;border-radius:20px;padding:5px 14px;font-size:12px;font-weight:700;letter-spacing:0.5px;'>&#9679;&nbsp;TIME CHANGE</span>";

        var sb = new StringBuilder();
        sb.Append($@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/></head>
<body style='margin:0;padding:0;background:#f0f4f8;font-family:Arial,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background:#f0f4f8;padding:32px 0;'>
<tr><td align='center'>
<table width='560' cellpadding='0' cellspacing='0' style='background:#ffffff;border-radius:14px;overflow:hidden;box-shadow:0 4px 24px rgba(0,0,0,0.10);'>

  <!-- Header -->
  <tr>
    <td style='background:#ffffff;padding:22px 24px 0 24px;'>
      <table width='100%' cellpadding='0' cellspacing='0'>
        <tr>
          <td>
            <table cellpadding='0' cellspacing='0'>
              <tr>
                <td style='background:#1a2744;border-radius:8px;width:38px;height:38px;text-align:center;vertical-align:middle;'>
                  <span style='color:#ffffff;font-size:20px;'>&#9992;</span>
                </td>
                <td style='padding-left:10px;'>
                  <div style='font-size:15px;font-weight:700;color:#1a2744;'>SkyOps</div>
                  <div style='font-size:11px;color:#5b8dee;font-style:italic;'>Queue Intelligence</div>
                </td>
              </tr>
            </table>
          </td>
          <td align='right'>{badgeHtml}</td>
        </tr>
      </table>
      <h1 style='font-size:22px;font-weight:800;color:#1a2744;margin:18px 0 4px 0;'>Queue Alert &mdash; Action Required</h1>
      <p style='font-size:13px;color:#555;margin:0 0 18px 0;'>SKY OPS Queue notification of PCC (<strong style='color:#1a2744;'>{PCC}</strong>)</p>
      <hr style='border:none;border-top:1px solid #e8edf2;margin:0;'/>
    </td>
  </tr>

  <!-- Meta row -->
  <tr>
    <td style='padding:16px 24px;'>
      <table width='100%' cellpadding='0' cellspacing='0'>
        <tr>
          <td style='width:33%;'>
            <div style='font-size:9px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;margin-bottom:4px;'>Processed</div>
            <div style='font-size:13px;font-weight:700;color:#1a2744;'>{processedAt}</div>
          </td>
          <td style='width:33%;'>
            <div style='font-size:9px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;margin-bottom:4px;'>Alert Types</div>
            <div style='font-size:13px;font-weight:700;color:#1a2744;'>{alertTypesDisplay}</div>
          </td>
          <td style='width:33%;'>
            <div style='font-size:9px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;margin-bottom:4px;'>Records</div>
            <div style='font-size:13px;font-weight:700;color:#1a2744;'>{totalRecords} flagged</div>
          </td>
        </tr>
      </table>
    </td>
  </tr>

  <!-- Flagged Records label -->
  <tr>
    <td style='padding:0 24px 10px 24px;'>
      <div style='font-size:9px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;'>Flagged Records</div>
    </td>
  </tr>");

        void AppendCard(string pnr, ActionFinding action, string statusColor, string statusBg, string statusLabel, string? segLabel)
        {
            var pnrUrl = $"{baseUrl}/pnr-details/{pnr}";
            

            var leftHtml = $@"
      <table role='presentation' width='100%' cellpadding='0' cellspacing='0' border='0'>
        <tr>
          <td valign='top' width='45%'>
            <div style='font-size:9px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;margin-bottom:4px;'>PNR Locator</div>
            <a href='{pnrUrl}' target='_blank' style='font-size:19px;font-weight:900;color:#1a2744;text-decoration:none;letter-spacing:2px;'>{pnr}</a>
          </td>
          <td valign='top' width='55%'>
            <div style='background:#f5f7fa;border-radius:6px;padding:10px 12px;'>
              <div style='font-size:9px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;margin-bottom:4px;'>Required Action</div>
              <div style='font-size:13px;font-weight:600;color:#1a2744;'>{action.Action}</div>
              <div style='font-size:11px;color:#888;margin-top:3px;'>Recommendation Only</div>
            </div>
          </td>
        </tr>
      </table>";

            sb.Append($@"
  <tr>
    <td style='padding:0 20px 14px 20px;'>
      <table width='100%' cellpadding='0' cellspacing='0' style='border:1.5px solid {statusColor};border-radius:10px;overflow:hidden;background:#ffffff;'>
        <tr valign='top'>
          <!-- Left: PNR + Required Action (updated layout) -->
          <td style='padding:16px 16px 14px 16px;'>
            {leftHtml}
          </td>
          <!-- Right: Status -->
          <td style='width:88px;border-left:1.5px dashed {statusColor};background:{statusBg};padding:20px 12px;text-align:center;vertical-align:middle;'>
            <div style='font-size:9px;color:#aaa;text-transform:uppercase;letter-spacing:0.8px;margin-bottom:6px;'>Status</div>
            <div style='font-size:24px;font-weight:700;color:{statusColor};line-height:1;'>{action.Status}</div>
            <div style='font-size:11px;color:{statusColor};margin-top:5px;'>{statusLabel}</div>
          </td>
        </tr>
      </table>
    </td>
  </tr>");
        }

        if (hasCritical)
        {
            foreach (var (pnr, _, action) in critical)
            {
                var (color, bg, label) = action.Status switch
                {
                    "HX" => ("#d32f2f", "#fff5f5", "Cancelled"),
                    "UN" => ("#d32f2f", "#fff5f5", "Unable"),
                    "UC" => ("#d32f2f", "#fff5f5", "Unable Confirm"),
                    _ => ("#d32f2f", "#fff5f5", action.Status)
                };
                var segLabel = action.Segment > 0 ? action.Segment.ToString("D3") : null;
                AppendCard(pnr, action, color, bg, label, segLabel);
            }
        }

        if (hasTimeChange)
        {
            foreach (var (pnr, _, action) in timeChanges)
            {
                var delayLabel = action.DelayMinutes.HasValue ? $"+{action.DelayMinutes} min" : "Schedule / Time Changes (TK Status)";
                var segLabel = action.Segment > 0 ? action.Segment.ToString("D3") : null;
                AppendCard(pnr, action, "#f57c00", "#fff8e1", delayLabel, segLabel);
            }
        }

        sb.Append(@"
  <!-- Disclaimer -->
  <tr>
    <td style='padding:0 20px 24px 20px;'>
      <table width='100%' cellpadding='0' cellspacing='0' style='background:#fffbea;border:1px solid #ffe082;border-radius:8px;'>
        <tr>
          <td style='padding:12px 16px;'>
            <table cellpadding='0' cellspacing='0'>
              <tr>
                <td style='vertical-align:top;padding-right:10px;font-size:15px;color:#f59e0b;'>&#9651;</td>
                <td style='font-size:12px;color:#555;line-height:1.6;'>Recommendation only. No Sabre commands were executed. All GDS actions must be reviewed and confirmed by a qualified agent before processing.</td>
              </tr>
            </table>
          </td>
        </tr>
      </table>
    </td>
  </tr>

</table>
</td></tr></table>
</body>
</html>");

        return sb.ToString();
    }

    private async Task<IReadOnlyList<string>> ResolveRecipientsAsync(
        IConfigurationSection section,
        string PCC,
        IReadOnlyList<QueueAnalysisResult> results,
        CancellationToken ct)
    {
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (section.GetValue<bool>("SendRemarkEmail"))
        {
            foreach (var recipient in results.SelectMany(result => SplitRecipients(result.RemarkEmail)))
                recipients.Add(recipient);
        }

        foreach (var recipient in await ResolvePccRecipientsAsync(PCC, results, ct))
        {
            recipients.Add(recipient);
        }

        if (recipients.Count == 0)
        {
            foreach (var recipient in section.GetSection("ToAddresses").Get<string[]>() ?? Array.Empty<string>())
            {
                recipients.Add(recipient);
            }
        }

        return recipients.ToArray();
    }

    private static IEnumerable<string> SplitRecipients(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (var recipient in value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.IsNullOrWhiteSpace(recipient))
                yield return recipient;
        }
    }

    private async Task<IReadOnlyList<string>> ResolvePccRecipientsAsync(
        string PCC,
        IReadOnlyList<QueueAnalysisResult> results,
        CancellationToken ct)
    {
        var pccCandidates = results
            .Select(r => r.PCC)
            .Where(pcc => !string.IsNullOrWhiteSpace(pcc))
            .Select(pcc => pcc!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(PCC))
        {
            pccCandidates.Add(PCC.Trim());
        }

        if (pccCandidates.Count == 0)
        {
            return Array.Empty<string>();
        }

        using var scope = _scopeFactory.CreateScope();
        var settingsRepository = scope.ServiceProvider.GetRequiredService<ISettingsRepository>();
        var recipients = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var entries = await settingsRepository.GetPccAgentEmailMastersByPccsAsync(pccCandidates, ct);

        foreach (var entry in entries.Where(entry => entry.IsActive == 1))
        {
            foreach (var email in SplitRecipients(entry.Emails))
            {
                recipients.Add(email);
            }
        }

        return recipients.ToArray();
    }

    private static string BuildTestEmailBody(IConfigurationSection section)
    {
        var sentAt = DateTime.UtcNow.ToString("yyyy-MM-dd · HH:mm") + " UTC";
        return $@"
<!DOCTYPE html>
<html>
<head><meta charset='utf-8'/></head>
<body style='margin:0;padding:0;background:#f4f6f9;font-family:Arial,sans-serif;'>
<table width='100%' cellpadding='0' cellspacing='0' style='background:#f4f6f9;padding:32px 0;'>
<tr><td align='center'>
<table width='600' cellpadding='0' cellspacing='0' style='background:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 2px 12px rgba(0,0,0,0.08);'>
  <tr>
    <td style='padding:28px;'>
      <table cellpadding='0' cellspacing='0'>
        <tr>
          <td style='background:#1a1a2e;border-radius:8px;width:36px;height:36px;text-align:center;vertical-align:middle;'>
            <span style='color:#ffffff;font-size:18px;font-weight:bold;'>&#9650;</span>
          </td>
          <td style='padding-left:10px;'>
            <div style='font-size:15px;font-weight:700;color:#1a1a2e;'>SkyOps</div>
            <div style='font-size:11px;color:#888;'>Queue Intelligence</div>
          </td>
        </tr>
      </table>
      <h1 style='font-size:20px;font-weight:800;color:#1a1a2e;margin:20px 0 8px 0;'>Test Email &mdash; SMTP Verified</h1>
      <p style='font-size:13px;color:#555;margin:0 0 20px 0;'>This message confirms SMTP delivery is working correctly.</p>
      <hr style='border:none;border-top:1px solid #eee;margin:0 0 20px 0;'/>
      <table cellpadding='0' cellspacing='0'>
        <tr><td style='font-size:12px;color:#aaa;padding-bottom:4px;'>SMTP Host</td><td style='font-size:13px;font-weight:700;color:#1a1a2e;padding-left:16px;'>{section["SmtpHost"]}</td></tr>
        <tr><td style='font-size:12px;color:#aaa;padding-bottom:4px;'>SMTP Port</td><td style='font-size:13px;font-weight:700;color:#1a1a2e;padding-left:16px;'>{section["SmtpPort"]}</td></tr>
        <tr><td style='font-size:12px;color:#aaa;padding-bottom:4px;'>From</td><td style='font-size:13px;font-weight:700;color:#1a1a2e;padding-left:16px;'>{section["FromAddress"]}</td></tr>
        <tr><td style='font-size:12px;color:#aaa;'>Sent At</td><td style='font-size:13px;font-weight:700;color:#1a1a2e;padding-left:16px;'>{sentAt}</td></tr>
      </table>
    </td>
  </tr>
  <tr>
    <td style='padding:0 28px 28px 28px;'>
      <table width='100%' cellpadding='0' cellspacing='0' style='background:#fffbea;border:1px solid #ffe082;border-radius:8px;'>
        <tr>
          <td style='padding:12px 16px;font-size:12px;color:#555;'>&#9651; Recommendation only. No Sabre commands were executed.</td>
        </tr>
      </table>
    </td>
  </tr>
</table>
</td></tr></table>
</body>
</html>";
    }

    private async Task SendEmailAsync(
        IConfigurationSection section,
        string subject,
        string htmlBody,
        IReadOnlyList<string> toAddresses,
        CancellationToken ct,
        bool throwOnError)
    {
        var host = section["SmtpHost"]!;
        var port = section.GetValue<int>("SmtpPort");
        var useSsl = section.GetValue<bool>("UseSsl");
        var username = section["Username"]!;
        var password = section["Password"]!;
        var fromAddress = section["FromAddress"]!;
        var fromName = section["FromName"] ?? "SkyOps Queue Intelligence";

        if (toAddresses.Count == 0)
        {
            _logger.LogWarning("Email notification skipped: no ToAddresses configured.");
            return;
        }

        using var message = new MailMessage();
        message.From = new MailAddress(fromAddress, fromName);
        foreach (var to in toAddresses)
            message.To.Add(to);
        message.Subject = subject;
        message.Body = htmlBody;
        message.IsBodyHtml = true;

        using var client = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(username, password),
            EnableSsl = useSsl
        };

        try
        {
            await client.SendMailAsync(message, ct);
            _logger.LogInformation("Email alert sent to {Recipients}", string.Join(", ", toAddresses));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send email alert.");
            if (throwOnError)
            {
                throw;
            }
        }
    }
}
