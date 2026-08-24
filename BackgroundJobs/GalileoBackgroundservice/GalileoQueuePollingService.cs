using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Helpers;
using SkyOpsQueueIntelligence.Application.Proxy;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.BackgroundJobs;

public sealed class GalileoQueuePollingService : BackgroundService
{
    private readonly Queue7PollingOptions _options;
    private readonly ICredentialStore _credentialStore;
    private readonly IGalileoSessionService _sessionService;
    private readonly IQueueActionRepository _repository;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GalileoQueuePollingService> _logger;

    public GalileoQueuePollingService(
        IOptions<Queue7PollingOptions> options,
        ICredentialStore credentialStore,
        IGalileoSessionService sessionService,
        IQueueActionRepository repository,
        HttpClient httpClient,
        ILogger<GalileoQueuePollingService> logger)
    {
        _options = options.Value;
        _credentialStore = credentialStore;
        _sessionService = sessionService;
        _repository = repository;
        _httpClient = httpClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.GalileoPolling.Enabled)
        {
            _logger.LogInformation("Galileo queue polling is disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.GalileoPolling.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllPccsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Galileo queue polling cycle failed.");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task PollAllPccsAsync(CancellationToken cancellationToken)
    {
        var pccCodes = _credentialStore.GetAll()
            .Where(credential => credential.Provider.Equals("1G", StringComparison.OrdinalIgnoreCase))
            .Select(credential => credential.PCCMasterCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var pccCode in pccCodes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var credentials = _credentialStore.GetByPcc(pccCode)
                .Where(credential => credential.Provider.Equals("1G", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var username = GetTag(credentials, "UserName");
            var password = GetTag(credentials, "Password");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("Galileo PCC {PccCode} has no UserName or Password.", pccCode);
                continue;
            }

            var sessionProfile = GetTag(credentials, "SessionProfileValue");
            if (string.IsNullOrWhiteSpace(sessionProfile))
            {
                _logger.LogError(
                    "Galileo PCC {PccCode} has no SessionProfileValue credential; configure the Travelport AccessProfile before polling.",
                    pccCode);
                continue;
            }

            _logger.LogInformation("Galileo PCC {PccCode} using session profile {Profile}.", pccCode, sessionProfile);
            var session = await _sessionService.CreateSessionAsync(
                pccCode, sessionProfile, cancellationToken);
            if (session is null) continue;

            foreach (var queueNumber in _options.GalileoPolling.Queues)
            {
                await ProcessQueueAsync(pccCode, username, password, session, queueNumber, cancellationToken);
            }
        }
    }

    private async Task ProcessQueueAsync(
        string pccCode,
        string username,
        string password,
        GalileoSession session,
        int queueNumber,
        CancellationToken cancellationToken)
    {
        var pnrTexts = new List<string>();
        var response = await SendCommandAsync(username, password, session, $"Q/{queueNumber}", cancellationToken);
        const int maxItems = 500;

        while (!IsQueueEmpty(response) && pnrTexts.Count < maxItems)
        {
            if (!string.IsNullOrWhiteSpace(response))
                pnrTexts.Add(response);

            response = await SendCommandAsync(username, password, session, "I", cancellationToken);
        }

        foreach (var pnrText in pnrTexts)
        {
            var results = Queue7Processor.ProcessQueueText(pnrText, queueNumber);
            if (results.Count > 0)
                await _repository.SaveRecommendedActionsAsync(results, session.UplId, "1G", cancellationToken);

            await SendCommandAsync(username, password, session, "@ALL", cancellationToken);
        }

        await SendCommandAsync(username, password, session, "QR", cancellationToken);
        _logger.LogInformation("Galileo PCC {PccCode} queue {QueueNumber}: processed {Count} item(s).", pccCode, queueNumber, pnrTexts.Count);
    }

    private async Task<string> SendCommandAsync(
        string username,
        string password,
        GalileoSession session,
        string command,
        CancellationToken cancellationToken)
    {
        var envelope = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
              <soap:Body>
                <SubmitTerminalTransaction xmlns="http://webservices.galileo.com">
                  <Token>{Escape(session.SessionToken)}</Token>
                  <Request>{Escape(command)}</Request>
                  <IntermediateResponse />
                </SubmitTerminalTransaction>
              </soap:Body>
            </soap:Envelope>
            """;

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.GalileoApi.Endpoint);
        request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Galileo command '{command}' failed with HTTP {(int)response.StatusCode}.");

        return ExtractTransactionText(responseText);
    }

    private static string ExtractTransactionText(string responseText)
    {
        try
        {
            var document = XDocument.Parse(responseText);
            var result = document.Descendants().FirstOrDefault(element =>
                element.Name.LocalName is "SubmitTerminalTransactionResult" or "Response" or "Text" or "Screen");
            return result?.Value.Trim() ?? responseText;
        }
        catch (XmlException)
        {
            return responseText;
        }
    }

    private static bool IsQueueEmpty(string text)
    {
        var upper = text.ToUpperInvariant();
        return string.IsNullOrWhiteSpace(text)
            || upper.Contains("QUEUE EMPTY")
            || upper.Contains("NO ITEMS")
            || upper.Contains("END OF QUEUE")
            || upper.Contains("0 ITEMS");
    }

    private static string? GetTag(IEnumerable<StorePccCredential> credentials, string tagName)
        => credentials.FirstOrDefault(credential =>
            credential.TagName.Trim().Equals(tagName, StringComparison.OrdinalIgnoreCase))?.TagValue.Trim();

    private static string Escape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}