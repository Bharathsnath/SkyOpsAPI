using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Infrastructure.Interfaces;

public interface IAdmAnalysisRepository
{
    Task SaveSalesAuditAsync(SalesAuditEntry entry, CancellationToken cancellationToken = default);
    Task SaveAdmAnalysisAsync(AdmAnalysisDto analysis, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdmAnalysisDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AdmAnalysisDto?> GetByPnrAsync(string pnr, CancellationToken cancellationToken = default);
    Task<DashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetPccMarketMapAsync(CancellationToken cancellationToken = default);
}

public record SalesAuditEntry(string Pnr, string TicketNumber, decimal Amount, DateTime TicketDate, string AgencyPcc, string Agent, string Time);
