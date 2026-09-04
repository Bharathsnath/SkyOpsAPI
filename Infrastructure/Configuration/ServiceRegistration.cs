using SkyOpsQueueIntelligence.Application.Helpers.Adapters.DTOAdapters;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.Proxy;
using SkyOpsQueueIntelligence.Application.Services;
using SkyOpsQueueIntelligence.BackgroundJobs;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Repositories;
using SkyOpsQueueIntelligence.Infrastructure.Services;

namespace SkyOpsQueueIntelligence.Infrastructure.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Queue7PollingOptions>(configuration.GetSection(Queue7PollingOptions.SectionName));

        // Infrastructure repositories
        services.AddSingleton<IConnectionCredentialStore, ConnectionCredentialStore>();
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<IQueueActionRepository, QueueActionRepository>();
        // ADM analysis repository and service
        services.AddSingleton<IAdmAnalysisRepository, AdmAnalysisRepository>();
        services.AddScoped<IAdmAnalysisService, AdmAnalysisService>();
        services.AddSingleton<ISettingsRepository, SettingsRepository>();
        services.AddSingleton<IDashboardRepository, DashboardRepository>();
        services.AddSingleton<IPriorityPnrRepository, PriorityPnrRepository>();
        services.AddSingleton<IUserDirectoryCache, UserDirectoryCache>();
        services.AddSingleton<IMarketCompanyBranchRepository, MarketCompanyBranchRepository>();
        services.AddSingleton<IMarketCompanyBranchService, MarketCompanyBranchService>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserPccMappingRepository, UserPccMappingRepository>();

        // Application services
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserPccMappingService, UserPccMappingService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IQueueService, QueueService>();
        services.AddSingleton<DashboardDtoAdapter>();
        services.AddSingleton<QueueDtoAdapter>();
        services.AddSingleton<SettingsDtoAdapter>();
        services.AddScoped<IQueueAnalysisService, QueueAnalysisService>();

        services.AddSingleton<IErrorLogService, ErrorLogService>();
        services.AddSingleton<ISabreXmlLogService, SabreXmlLogService>();
        services.AddSingleton<IEmailNotificationService, EmailNotificationService>();

        // ADM background job
        services.AddSingleton<AdmAnalysisBackgroundService>();
        services.AddHostedService(sp => sp.GetRequiredService<AdmAnalysisBackgroundService>());

        // Proxy / Sabre API clients
        services.AddHttpClient<Queue7TextSource>();
        services.AddHttpClient<SabreSessionService>();
        services.AddSingleton<ISabreSessionService>(sp => sp.GetRequiredService<SabreSessionService>());
        services.AddSingleton<IQueue7TextSource>(sp => sp.GetRequiredService<Queue7TextSource>());
        services.AddHttpClient<SabreCommandService>();
        services.AddScoped<ISabreCommandService, SabreCommandService>();

        // Background jobs
        services.AddSingleton<SabreQueuePollingService>();
        services.AddHostedService(sp => sp.GetRequiredService<SabreQueuePollingService>());
        services.AddSingleton<IQueue7PollingTrigger>(sp => sp.GetRequiredService<SabreQueuePollingService>());
        services.AddHttpClient<GalileoSessionService>();
        services.AddSingleton<IGalileoSessionService>(sp => sp.GetRequiredService<GalileoSessionService>());
        services.AddHttpClient<GalileoQueuePollingService>();
        services.AddHostedService<GalileoQueuePollingService>();
        services.AddHttpClient<AmadeusSessionService>();
        services.AddSingleton<IAmadeusSessionService>(sp => sp.GetRequiredService<AmadeusSessionService>());
        services.AddHttpClient<AmadeusQueuePollingService>();
        services.AddHostedService<AmadeusQueuePollingService>();

        return services;
    }
}
