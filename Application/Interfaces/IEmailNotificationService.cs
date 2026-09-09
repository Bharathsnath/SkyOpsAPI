using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IEmailNotificationService
{
    Task SendAlertAsync(string pccCode, string company, string market, IReadOnlyList<QueueAnalysisResult> results, CancellationToken ct = default);
    Task SendQueueProcessingSummaryAsync(
        string pccCode,
        string displayPcc,
        IReadOnlyList<(string HostCommand, int QueueNumber, int AnalyzedCount, int SavedCount)> queueSummaries,
        CancellationToken ct = default);
    Task SendTestEmailAsync(CancellationToken ct = default);
    Task SendPriorityPnrAlertAsync(string pnr, IReadOnlyList<string> toEmails, IReadOnlyList<QueueAnalysisResult> results, CancellationToken ct = default);
    Task SendPriorityPnrRegistrationAsync(string pnr, IReadOnlyList<string> toEmails, CancellationToken ct = default);
    Task SendRemarkEmailNotificationAsync(string pnr, string remarkEmail, IReadOnlyList<QueueAnalysisResult> results, CancellationToken ct = default);
    Task SendTestRemarkEmailAsync(string toEmail, CancellationToken ct = default);
}
