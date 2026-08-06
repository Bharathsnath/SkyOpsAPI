using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SkyOpsQueueIntelligence.Application.DTO.Request;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.Proxy;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public sealed class SabreCommandService : ISabreCommandService
{
    private readonly ISabreSessionService _sessionService;
    private readonly ICredentialStore _credentialStore;
    private readonly ISabreXmlLogService _xmlLogService;
    private readonly Queue7PollingOptions _options;
    private readonly IErrorLogService _errorLogService;
    private readonly HttpClient _httpClient;

    public SabreCommandService(
        ISabreSessionService sessionService,
        ICredentialStore credentialStore,
        ISabreXmlLogService xmlLogService,
        IOptions<Queue7PollingOptions> options,
        IErrorLogService errorLogService,
        HttpClient httpClient)
    {
        _sessionService = sessionService;
        _credentialStore = credentialStore;
        _xmlLogService = xmlLogService;
        _options = options.Value;
        _errorLogService = errorLogService;
        _httpClient = httpClient;
    }

    public Task<SabreCommandResponse> ExecuteEwrAsync(SabreCommandRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, "EWR", cancellationToken);

    public Task<SabreCommandResponse> ExecuteQrAsync(SabreCommandRequest request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, "QR", cancellationToken);

    public async Task<IEnumerable<SabreCommandResponse>> ExecuteQueueAsync(SabreQueueProcessRequest request, CancellationToken cancellationToken = default)
    {
        var (username, password, pccCode) = ResolveCredentials(request.OfficeId);
        var targetPnr = request.Pnr.Trim().ToUpperInvariant();
        var queueCommand = $"Q/{request.Queue}";

        SabreSession? session = null;
        try
        {
            session = await _sessionService.CreateSessionAsync(username, password, request.OfficeId, cancellationToken)
                ?? throw new InvalidOperationException($"Session creation failed for OfficeId: {request.OfficeId}");

            var responses = new List<SabreCommandResponse>();

            // Step 1: Enter the queue — response already contains first PNR display
            var currentResponse = await SendCommandAsync(session, queueCommand, pccCode, cancellationToken);
            responses.Add(new SabreCommandResponse(request.OfficeId, string.Empty, queueCommand, currentResponse));

            if (IsQueueEmpty(currentResponse))
                return responses;

            // Step 2: Loop with I until the 6-char PNR locator line is found
            while (!ContainsPnrLocator(currentResponse, targetPnr))
            {
                if (IsQueueEmpty(currentResponse))
                    return responses;

                currentResponse = await SendCommandAsync(session, "I", pccCode, cancellationToken);
                responses.Add(new SabreCommandResponse(request.OfficeId, string.Empty, "I", currentResponse));
            }

            // Step 3: Target PNR found — EWR loop until Sabre confirms end of transaction
            string ewrResponse;
            do
            {
                ewrResponse = await SendCommandAsync(session, "EWR", pccCode, cancellationToken);
                responses.Add(new SabreCommandResponse(request.OfficeId, targetPnr, "EWR", ewrResponse));
            }
            while (!IsEwrComplete(ewrResponse));

            // Step 4: QR — remove from queue (also exits queue, no QXI needed)
            var qrResponse = await SendCommandAsync(session, "QR", pccCode, cancellationToken);
            responses.Add(new SabreCommandResponse(request.OfficeId, targetPnr, "QR", qrResponse));

            return responses;
        }
        finally
        {
            if (session is not null)
                await _sessionService.CloseSessionAsync(session, cancellationToken);
        }
    }

    public async Task<IEnumerable<SabreCommandResponse>> ExecuteBothAsync(SabreCommandRequest request, CancellationToken cancellationToken = default)
    {
        var (username, password, pccCode) = ResolveCredentials(request.OfficeId);

        SabreSession? session = null;
        try
        {
            session = await _sessionService.CreateSessionAsync(username, password, request.OfficeId, cancellationToken)
                ?? throw new InvalidOperationException($"Session creation failed for OfficeId: {request.OfficeId}");

            var pnr = request.Pnr.ToUpperInvariant();
            var ewrCommand = $"EWR {pnr}";
            var qrCommand = $"QR {pnr}";

            var ewrResponse = await SendCommandAsync(session, ewrCommand, pccCode, cancellationToken);
            var qrResponse = await SendCommandAsync(session, qrCommand, pccCode, cancellationToken);

            return new[] {
                new SabreCommandResponse(request.OfficeId, request.Pnr, "EWR", ewrResponse),
                new SabreCommandResponse(request.OfficeId, request.Pnr, "QR", qrResponse)
            };
        }
        finally
        {
            if (session is not null)
                await _sessionService.CloseSessionAsync(session, cancellationToken);
        }
    }

    private async Task<SabreCommandResponse> ExecuteAsync(SabreCommandRequest request, string command, CancellationToken cancellationToken)
    {
        var (username, password, pccCode) = ResolveCredentials(request.OfficeId);

        SabreSession? session = null;
        try
        {
            session = await _sessionService.CreateSessionAsync(username, password, request.OfficeId, cancellationToken)
                ?? throw new InvalidOperationException($"Session creation failed for OfficeId: {request.OfficeId}");

            var hostCommand = $"{command} {request.Pnr.ToUpperInvariant()}";
            var responseText = await SendCommandAsync(session, hostCommand, pccCode, cancellationToken);

            return new SabreCommandResponse(request.OfficeId, request.Pnr, command, responseText);
        }
        finally
        {
            if (session is not null)
                await _sessionService.CloseSessionAsync(session, cancellationToken);
        }
    }

    private (string Username, string Password, string PccCode) ResolveCredentials(string officeId)
    {
        var pccGroup = _credentialStore.GetAll()
            .GroupBy(c => c.PCCMasterCode, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Any(c =>
                c.TagName.Equals("SourceOffice", StringComparison.OrdinalIgnoreCase) &&
                c.TagValue.Equals(officeId, StringComparison.OrdinalIgnoreCase)));

        if (pccGroup is null)
            throw new KeyNotFoundException($"No credentials found for OfficeId: {officeId}");

        var username = pccGroup.FirstOrDefault(c => c.TagName.Equals("UserName", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty;
        var password = pccGroup.FirstOrDefault(c => c.TagName.Equals("Password", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty;

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException($"Incomplete credentials for OfficeId: {officeId}");

        return (username, password, pccGroup.Key);
    }

    private async Task<string> SendCommandAsync(SabreSession session, string hostCommand, string pccCode, CancellationToken cancellationToken)
    {
        var apiOptions = _options.SabreApi;
        var messageId = $"{Guid.NewGuid()}@{apiOptions.FromPartyId}";
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss");

        var envelope = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <soap:Header>
                <MessageHeader xmlns="http://www.ebxml.org/namespaces/messageHeader">
                  <From>
                    <PartyId d5p1:type="urn:x12.org.IO5:01" xmlns:d5p1="http://www.ebxml.org/namespaces/messageHeader">{Escape(apiOptions.FromPartyId)}</PartyId>
                  </From>
                  <To>
                    <PartyId d5p1:type="urn:x12.org.IO5:01" xmlns:d5p1="http://www.ebxml.org/namespaces/messageHeader">{Escape(apiOptions.ToPartyId)}</PartyId>
                  </To>
                  <ConversationId>{Escape(session.ConversationId)}</ConversationId>
                  <Service d4p1:type="Sabre Trip Management" xmlns:d4p1="http://www.ebxml.org/namespaces/messageHeader">SabreCommandLLSRQ</Service>
                  <Action>SabreCommandLLSRQ</Action>
                  <MessageData>
                    <MessageId>{Escape(messageId)}</MessageId>
                    <Timestamp>{timestamp}</Timestamp>
                  </MessageData>
                </MessageHeader>
                <Security xmlns="http://schemas.xmlsoap.org/ws/2002/12/secext">
                  <BinarySecurityToken>{Escape(session.BinarySecurityToken)}</BinarySecurityToken>
                </Security>
              </soap:Header>
              <soap:Body>
                <SabreCommandLLSRQ Version="2.0.0" ReturnHostCommand="true" xmlns="http://webservices.sabre.com/sabreXML/2011/10">
                  <Request Output="SCREEN">
                    <HostCommand>{Escape(hostCommand)}</HostCommand>
                  </Request>
                </SabreCommandLLSRQ>
              </soap:Body>
            </soap:Envelope>
            """;

        var responseText = string.Empty;
        var statusCode = 0;

        try
        {
            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, apiOptions.Endpoint);
            httpRequest.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
            httpRequest.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/xml"));
            httpRequest.Headers.TryAddWithoutValidation("SOAPAction", "SabreCommandLLSRQ");

            using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            statusCode = (int)response.StatusCode;

            await _xmlLogService.LogSabreRequestResponseAsync(
                hostCommand: hostCommand, soapRequest: envelope, soapResponse: responseText,
                httpStatusCode: statusCode, pccCode: pccCode,
                status: response.IsSuccessStatusCode ? "SUCCESS" : "FAILED",
                uplId: session.UplId,
                cancellationToken: cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Sabre command '{hostCommand}' failed: HTTP {statusCode}");

            return ExtractResponseText(responseText);
        }
        catch (Exception ex)
        {
            await _xmlLogService.LogSabreRequestResponseAsync(
                hostCommand: hostCommand, soapRequest: envelope,
                soapResponse: string.IsNullOrWhiteSpace(responseText) ? ex.Message : responseText,
                httpStatusCode: statusCode == 0 ? 500 : statusCode,
                pccCode: pccCode, status: "FAILED", uplId: session.UplId, cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<string> ExecuteHostCommandAsync(string officeId, string hostCommand, CancellationToken cancellationToken = default)
    {
        var (username, password, pccCode) = ResolveCredentials(officeId);

        SabreSession? session = null;
        try
        {
            session = await _sessionService.CreateSessionAsync(username, password, officeId, cancellationToken)
                ?? throw new InvalidOperationException($"Session creation failed for OfficeId: {officeId}");

            var response = await SendCommandAsync(session, hostCommand, pccCode, cancellationToken);
            return response;
        }
        finally
        {
            if (session is not null)
                await _sessionService.CloseSessionAsync(session, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<string>> ExecutePagedHostCommandAsync(
        string officeId, string firstCommand, string nextPageCommand, string endMarker, int maxPages,
        CancellationToken cancellationToken = default)
    {
        var (username, password, pccCode) = ResolveCredentials(officeId);

        SabreSession? session = null;
        try
        {
            session = await _sessionService.CreateSessionAsync(username, password, officeId, cancellationToken)
                ?? throw new InvalidOperationException($"Session creation failed for OfficeId: {officeId}");

            var pages = new List<string>();
            var page = await SendCommandAsync(session, firstCommand, pccCode, cancellationToken);
            pages.Add(page);

            for (var i = 0; i < maxPages && !page.Contains(endMarker, StringComparison.OrdinalIgnoreCase); i++)
            {
                page = await SendCommandAsync(session, nextPageCommand, pccCode, cancellationToken);
                pages.Add(page);
            }

            return pages;
        }
        finally
        {
            if (session is not null)
                await _sessionService.CloseSessionAsync(session, cancellationToken);
        }
    }

    private static string ExtractResponseText(string soapResponse)
    {
        try
        {
            var doc = XDocument.Parse(soapResponse);
            var element = doc.Descendants().FirstOrDefault(e => e.Name.LocalName is "Response" or "Text" or "Screen");
            return element?.Value.Trim() ?? soapResponse;
        }
        catch { return soapResponse; }
    }

    // 6-char PNR locator appears on its own line (e.g. "KBKJNW")
    private static bool ContainsPnrLocator(string text, string pnr)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Any(line => line.Trim().Equals(pnr, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsQueueEmpty(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        var upper = text.ToUpperInvariant();
        return upper.Contains("QUEUE EMPTY")
            || upper.Contains("NO ITEMS")
            || upper.Contains("END OF QUEUE")
            || upper.Contains("QUE EMPTY")
            || upper.Contains("0 ITEMS")
            || upper.Contains("END OF DISPLAY FOR REQUESTED DATA")
            || upper.Contains("QUEUE SELECTED WAS EMPTY");
    }

    // EWR is complete when Sabre has fully ended the PNR transaction
    private static bool IsEwrComplete(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var upper = text.ToUpperInvariant();
        return upper.Contains("NO ITIN")
            || upper.Contains("TKT/TIME LIMIT")
            || upper.Contains("ITINERARY REQUIRED")
            || upper.Contains("VERIFY ORDER OF ITINERARY SEGMENTS")
            || System.Text.RegularExpressions.Regex.IsMatch(upper, @"\d+\.\d+[A-Z]+/[A-Z]+");
    }

    private static string Escape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
