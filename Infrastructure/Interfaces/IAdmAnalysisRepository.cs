using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IAdmAnalysisRepository
{
    Task<long> SaveSalesAuditAsync(SalesAuditEntry entry, CancellationToken cancellationToken = default);
    Task SaveAdmAnalysisAsync(AdmAnalysisDto analysis, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdmAnalysisDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AdmAnalysisDto?> GetByPnrAsync(string pnr, CancellationToken cancellationToken = default);
    Task<DashboardDto> GetDashboardAsync(int userId, CancellationToken cancellationToken = default);
    Task<AdmDashboardDto> GetAdmDashboardAsync(CancellationToken cancellationToken = default);
    
    Task SaveCommandHistoryAsync(
        string pccCode,
        string hostCommand,
        string responseText,
        string uplId,
        string? pnr = null,
        CancellationToken cancellationToken = default);
}

public record SalesAuditEntry(string Pnr, string TicketNumber, decimal Amount, DateTime TicketDate, string AgencyPcc, string Agent, string Time);
