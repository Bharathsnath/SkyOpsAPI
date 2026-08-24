namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface ISabreXmlLogService
{
    Task LogSabreRequestResponseAsync(
        string hostCommand,
        string soapRequest,
        string soapResponse,
        int httpStatusCode,
        string pccCode = "",
        string status = "SUCCESS",
        string uplId = "",
        string moduleName = "SabreQueueMCP",
        string moduleCode = "QUEUE",
        CancellationToken cancellationToken = default);
}
