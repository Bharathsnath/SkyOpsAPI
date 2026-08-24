using Microsoft.Extensions.Hosting;
using SkyOpsQueueIntelligence.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace SkyOpsQueueIntelligence.BackgroundJobs;

public class AdmAnalysisBackgroundService : BackgroundService
{
    private readonly ILogger<AdmAnalysisBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public AdmAnalysisBackgroundService(IServiceScopeFactory scopeFactory, ILogger<AdmAnalysisBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ADM background service starting.");

        // Run once on startup
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<IAdmAnalysisService>();
            await svc.RunAnalysisAsync(stoppingToken);
            // await svc.RunQueue379ChurnScanAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initial ADM analysis run.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(4));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IAdmAnalysisService>();
                    await svc.RunAnalysisAsync(stoppingToken);
                    // await svc.RunQueue379ChurnScanAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during scheduled ADM analysis run.");
                }
            }
        }
        catch (OperationCanceledException) { }

        _logger.LogInformation("ADM background service stopping.");
    }
}
