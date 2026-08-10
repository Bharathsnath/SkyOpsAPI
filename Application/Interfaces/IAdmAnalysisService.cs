using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IAdmAnalysisService
{
    Task RunAnalysisAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AdmAnalysisDto>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AdmAnalysisDto?> GetByPnrAsync(string pnr, CancellationToken cancellationToken = default);
    Task<DashboardDto> GetDashboardAsync(int userId, CancellationToken cancellationToken = default);
    Task<AdmDashboardDto> GetAdmDashboardAsync(CancellationToken cancellationToken = default);
}
