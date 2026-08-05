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
        CancellationToken cancellationToken = default);
}
