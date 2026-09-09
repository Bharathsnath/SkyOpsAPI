using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface ICrmService
{
    Task<IReadOnlyList<CrmEmailDto>> GetEmailsAsync(CrmSearchRequest request, CancellationToken ct = default);
    Task<CrmEmailDto?> GetEmailAsync(long id, CancellationToken ct = default);
    Task<CrmCaseDto?> GetCaseAsync(string caseNumber, CancellationToken ct = default);
    Task<IReadOnlyList<CrmTimelineItemDto>> GetTimelineAsync(string caseNumber, CancellationToken ct = default);
    Task<CrmTimelineItemDto> AddMessageAsync(string caseNumber, string senderType, string? senderId, string message, CancellationToken ct = default);
    Task<CrmTimelineItemDto> AddActionAsync(string caseNumber, long? messageId, string actorType, string? actorId, CrmActionRequest request, CancellationToken ct = default);
    Task<Customer360Dto?> GetCustomerAsync(long customerId, CancellationToken ct = default);
    Task<SmtpConfigurationRecord?> GetSmtpConfigurationAsync(CancellationToken ct = default);
    Task SaveSmtpConfigurationAsync(SmtpConfigurationRecord configuration, CancellationToken ct = default);
}