using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.Proxy;

namespace SkyOpsQueueIntelligence.Application.Proxy;

public sealed class Queue7TextSource : IQueue7TextSource
{
    private readonly Queue7PollingOptions _options;
    private readonly HttpClient _httpClient;
    private readonly IHostEnvironment _environment;
    private readonly ISabreXmlLogService _xmlLogService;
    private readonly ILogger<Queue7TextSource> _logger;
    private readonly string _activeConversationId;

    public Queue7TextSource(
        IOptions<Queue7PollingOptions> options,
        HttpClient httpClient,
        IHostEnvironment environment,
        ISabreXmlLogService xmlLogService,
        ILogger<Queue7TextSource> logger)
    {
        _options = options.Value;
        _httpClient = httpClient;
        _environment = environment;
        _xmlLogService = xmlLogService;
        _logger = logger;
        _activeConversationId = string.IsNullOrWhiteSpace(_options.SabreApi.ConversationId)
            ? $"{Guid.NewGuid()}@{_options.SabreApi.FromPartyId}"
            : _options.SabreApi.ConversationId;
    }

    public Task<Queue7TextSourceResult> GetQueueTextAsync(CancellationToken cancellationToken)
        => GetQueueTextForCommandAsync(_options.SabreApi.HostCommand, cancellationToken);

    public async Task<Queue7TextSourceResult> GetQueueAnalysisTextForCommandAsync(string hostCommand, CancellationToken cancellationToken)
    {
        if (_options.Source.Equals("File", StringComparison.OrdinalIgnoreCase))
        {
            return await GetQueueTextForCommandAsync(hostCommand, cancellationToken);
        }

        var pnrTexts = await GetAllQueuePnrsAsync(hostCommand, cancellationToken);
        return new Queue7TextSourceResult(
            string.Join(Environment.NewLine, pnrTexts),
            _options.SabreApi.Endpoint,
            _activeConversationId);
    }

    public async Task<Queue7TextSourceResult> GetQueueTextForCommandAsync(string hostCommand, CancellationToken cancellationToken)
    {
        if (!Queue7PollingOptions.AllowedHostCommands.Contains(hostCommand))
            throw new InvalidOperationException($"Host command '{hostCommand}' is not permitted. Allowed: {string.Join(", ", Queue7PollingOptions.AllowedHostCommands)}.");

        return _options.Source.Equals("File", StringComparison.OrdinalIgnoreCase)
            ? GetQueueTextFromFile(hostCommand)
            : await GetQueueTextFromSabreApiAsync(hostCommand, cancellationToken);
    }

    // Fetches all PNRs from a queue by looping Q/{n} → I → I → ... until Sabre signals empty queue.
    public async Task<IReadOnlyList<string>> GetAllQueuePnrsAsync(string hostCommand, CancellationToken cancellationToken)
    {
        if (!Queue7PollingOptions.AllowedHostCommands.Contains(hostCommand))
            throw new InvalidOperationException($"Host command '{hostCommand}' is not permitted.");

        var pnrTexts = new List<string>();
        const int maxItems = 500; // safety cap to avoid a runaway queue session

        // Enter the queue
        var result = await GetQueueTextFromSabreApiAsync(hostCommand, cancellationToken);

        while (!IsQueueEmpty(result.QueueText) && pnrTexts.Count < maxItems)
        {
            // Extract the current PNR text from the response
            var currentPnr = ExtractCurrentPnr(result.QueueText);
            if (string.IsNullOrWhiteSpace(currentPnr))
            {
                currentPnr = result.QueueText.Trim();
            }

            if (!string.IsNullOrWhiteSpace(currentPnr))
            {
                pnrTexts.Add(currentPnr);
            }

            // Advance to next queue item using I (ignore/next) — does not remove PNR
            result = await GetQueueTextFromSabreApiAsync("I", cancellationToken);
        }

        // Exit the queue (only if we successfully retrieved at least one PNR)
        if (pnrTexts.Count > 0)
        {
            try
            {
                await GetQueueTextFromSabreApiAsync("QXI", cancellationToken);
            }
            catch
            {
                // Ignore errors on exit command
            }
        }

        return pnrTexts;
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
            || upper.Contains("QUEUE SELECTED WAS EMPTY"); // some Sabre versions return the last PNR with "NO PIC CODE" when queue is empty
    }

    private Queue7TextSourceResult GetQueueTextFromFile(string hostCommand)
    {
        var queueNumber = hostCommand.Split('/').Last();
        var filePath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, $"data/QUEUE {queueNumber}.txt"));

        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Queue data file not found: {filePath}", filePath);

        return new Queue7TextSourceResult(File.ReadAllText(filePath), filePath, _activeConversationId);
    }

    private async Task<Queue7TextSourceResult> GetQueueTextFromSabreApiAsync(string hostCommand, CancellationToken cancellationToken)
    {
        var apiOptions = _options.SabreApi;
        var token = GetSecurityToken(apiOptions);

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Sabre BinarySecurityToken is not configured. Set Queue7Polling:SabreApi:BinarySecurityToken or SABRE_SECURITY_TOKEN.");

        var soapEnvelope = BuildSabreCommandEnvelope(apiOptions, token, hostCommand, _activeConversationId);
        using var request = new HttpRequestMessage(HttpMethod.Post, apiOptions.Endpoint);
        request.Content = new StringContent(soapEnvelope, Encoding.UTF8, "text/xml");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.TryAddWithoutValidation("SOAPAction", "SabreCommandLLSRQ");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        // Log the Sabre request and response to wp_xmllog database
        var httpStatusCode = (int)response.StatusCode;
        var status = response.IsSuccessStatusCode ? "SUCCESS" : "FAILED";
        
        await _xmlLogService.LogSabreRequestResponseAsync(
            hostCommand: hostCommand,
            soapRequest: soapEnvelope,
            soapResponse: responseText,
            httpStatusCode: httpStatusCode,
            pccCode: apiOptions.FromPartyId ?? "UNKNOWN",
            status: status,
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Sabre API request failed with HTTP {httpStatusCode}: {response.ReasonPhrase} — {responseText}");

        return new Queue7TextSourceResult(ExtractQueueText(responseText), apiOptions.Endpoint, _activeConversationId);
    }

    private static string GetSecurityToken(SabreApiOptions options)
        => !string.IsNullOrWhiteSpace(options.BinarySecurityToken)
            ? options.BinarySecurityToken
            : Environment.GetEnvironmentVariable("SABRE_SECURITY_TOKEN") ?? string.Empty;

    private static string BuildSabreCommandEnvelope(SabreApiOptions options, string token, string hostCommand, string conversationId)
    {
        var messageId = $"{Guid.NewGuid()}@{options.FromPartyId}";
        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        return $$"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <soap:Header>
                <MessageHeader xmlns="http://www.ebxml.org/namespaces/messageHeader">
                  <From>
                    <PartyId d5p1:type="urn:x12.org.IO5:01" xmlns:d5p1="http://www.ebxml.org/namespaces/messageHeader">{{Escape(options.FromPartyId)}}</PartyId>
                  </From>
                  <To>
                    <PartyId d5p1:type="urn:x12.org.IO5:01" xmlns:d5p1="http://www.ebxml.org/namespaces/messageHeader">{{Escape(options.ToPartyId)}}</PartyId>
                  </To>
                  <ConversationId>{{Escape(conversationId)}}</ConversationId>
                  <Service d4p1:type="Sabre Trip Management" xmlns:d4p1="http://www.ebxml.org/namespaces/messageHeader">SabreCommandLLSRQ</Service>
                  <Action>SabreCommandLLSRQ</Action>
                  <MessageData>
                    <MessageId>{{Escape(messageId)}}</MessageId>
                    <Timestamp>{{timestamp}}</Timestamp>
                  </MessageData>
                </MessageHeader>
                <Security xmlns="http://schemas.xmlsoap.org/ws/2002/12/secext">
                  <BinarySecurityToken>{{Escape(token)}}</BinarySecurityToken>
                </Security>
              </soap:Header>
              <soap:Body>
                <SabreCommandLLSRQ Version="2.0.0" ReturnHostCommand="true" xmlns="http://webservices.sabre.com/sabreXML/2011/10">
                  <Request Output="SCREEN">
                    <HostCommand>{{Escape(hostCommand)}}</HostCommand>
                  </Request>
                </SabreCommandLLSRQ>
              </soap:Body>
            </soap:Envelope>
            """;
    }

    private static string ExtractQueueText(string soapResponse)
    {
        try
        {
            var document = XDocument.Parse(soapResponse);
            var responseElement = document
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName is "Response" or "Text" or "Screen");

            return responseElement?.Value.Trim() ?? soapResponse;
        }
        catch
        {
            return soapResponse;
        }
    }

    private static string ExtractCurrentPnr(string queueText)
    {
        if (string.IsNullOrWhiteSpace(queueText))
            return string.Empty;

        var lines = queueText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        var pnrLines = new List<string>();
        var captureStarted = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (!captureStarted && IsQueueBlockHeader(line))
            {
                captureStarted = true;
                pnrLines.Add(line);
                continue;
            }

            if (captureStarted)
            {
                if (IsQueueBlockHeader(line))
                    break;
                pnrLines.Add(line);
            }
        }

        return string.Join("\n", pnrLines).Trim();
    }

    private static bool IsQueueBlockHeader(string line)
    {
        return line.Contains("NO PIC CODE", StringComparison.OrdinalIgnoreCase)
            || System.Text.RegularExpressions.Regex.IsMatch(line, @"^\s*\d{1,3}\s{2,}.+(?:CANCELLED|SCHEDULE CHANGE|TIME LIMIT|WAITLIST|REQUESTED)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static string Escape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
