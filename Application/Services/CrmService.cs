using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public sealed class CrmService : ICrmService
{
    private readonly ICrmRepository repository;

    public CrmService(ICrmRepository repository) => this.repository = repository;

    public Task<IReadOnlyList<CrmEmailDto>> GetEmailsAsync(CrmSearchRequest request, CancellationToken ct = default) => repository.GetEmailsAsync(request, ct);

    public Task<CrmEmailDto?> GetEmailAsync(long id, CancellationToken ct = default) => repository.GetEmailAsync(id, ct);

    public Task<CrmCaseDto?> GetCaseAsync(string caseNumber, CancellationToken ct = default) => repository.GetCaseAsync(caseNumber, ct);

    public Task<IReadOnlyList<CrmTimelineItemDto>> GetTimelineAsync(string caseNumber, CancellationToken ct = default) => repository.GetTimelineAsync(caseNumber, ct);

    public Task<CrmTimelineItemDto> AddMessageAsync(string caseNumber, string senderType, string? senderId, string message, CancellationToken ct = default) => repository.AddMessageAsync(caseNumber, senderType, senderId, message, ct);

    public Task<CrmTimelineItemDto> AddActionAsync(string caseNumber, long? messageId, string actorType, string? actorId, CrmActionRequest request, CancellationToken ct = default) => repository.AddActionAsync(caseNumber, messageId, actorType, actorId, request, ct);

    public Task<Customer360Dto?> GetCustomerAsync(long customerId, CancellationToken ct = default) => repository.GetCustomerAsync(customerId, ct);

    public Task<SmtpConfigurationRecord?> GetSmtpConfigurationAsync(CancellationToken ct = default) => repository.GetSmtpConfigurationAsync(ct);

    public Task SaveSmtpConfigurationAsync(SmtpConfigurationRecord configuration, CancellationToken ct = default) => repository.SaveSmtpConfigurationAsync(configuration, ct);
}