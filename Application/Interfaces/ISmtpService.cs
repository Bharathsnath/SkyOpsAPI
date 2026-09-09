namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface ISmtpService
{
    Task TestConnectionAsync(SmtpConfigurationRequest request, CancellationToken ct = default);
    Task SendAsync(SmtpSendRequest request, CancellationToken ct = default);
}

public sealed record SmtpConfigurationRequest(string Provider, string Host, int Port, string Username, string? Password, string FromEmail, string FromName, bool UseSsl);
public sealed record SmtpSendRequest(string To, string? Cc, string Subject, string HtmlBody, IReadOnlyList<SmtpAttachmentRequest>? Attachments = null);
public sealed record SmtpAttachmentRequest(string FileName, string ContentType, string Base64Content);
