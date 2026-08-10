using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public sealed class SabreXmlLogService : ISabreXmlLogService
{
    private readonly IQueueActionRepository _queueActionRepository;
    private readonly ISettingsRepository _settingsRepository;
    private readonly ILogger<SabreXmlLogService> _logger;

    public SabreXmlLogService(
        IQueueActionRepository queueActionRepository,
        ISettingsRepository settingsRepository,
        ILogger<SabreXmlLogService> logger)
    {
        _queueActionRepository = queueActionRepository;
        _settingsRepository = settingsRepository;
        _logger = logger;
    }

    public async Task LogSabreRequestResponseAsync(
        string hostCommand,
        string soapRequest,
        string soapResponse,
        int httpStatusCode,
        string pccCode = "",
        string status = "SUCCESS",
        string uplId = "",
        string moduleName = "SabreQueueMCP",
        string moduleCode = "QUEUE",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var configs = await _settingsRepository.GetLoggingConfigurationsAsync(cancellationToken);
            var isEnabled = configs.Find(c => c.ConfigKey == "XMLLog" && c.ProviderName == "Sabre")?.IsEnabled ?? true;
            if (!isEnabled) return;

            await _queueActionRepository.SaveApiLogAsync(
                pccCode: pccCode,
                serviceName: moduleName,
                hostCommand: hostCommand,
                requestXml: soapRequest,
                responseXml: soapResponse,
                httpStatusCode: httpStatusCode,
                status: status,
                uplId: uplId,
                moduleName: moduleName,
                moduleCode: moduleCode,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log Sabre request/response. Command: {HostCommand}", hostCommand);
        }
    }
}
