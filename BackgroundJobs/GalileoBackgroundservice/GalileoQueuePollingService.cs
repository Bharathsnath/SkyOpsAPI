using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Helpers;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.Proxy;
using SkyOpsQueueIntelligence.Hubs;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.BackgroundJobs;

public sealed class GalileoQueuePollingService : BackgroundService
{
    private readonly Queue7PollingOptions _options;
    private readonly ICredentialStore _credentialStore;
    private readonly IGalileoSessionService _sessionService;
    private readonly IQueueActionRepository _repository;
    private readonly IEmailNotificationService _emailService;
    private readonly IHubContext<QueueNotificationsHub> _hub;
    private readonly IErrorLogService _errorLogService;
    private readonly IPriorityPnrRepository _priorityPnrRepository;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GalileoQueuePollingService> _logger;

    public GalileoQueuePollingService(
        IOptions<Queue7PollingOptions> options,
        ICredentialStore credentialStore,
        IGalileoSessionService sessionService,
        IQueueActionRepository repository,
        IEmailNotificationService emailService,
        IHubContext<QueueNotificationsHub> hub,
        IErrorLogService errorLogService,
        IPriorityPnrRepository priorityPnrRepository,
        IServiceScopeFactory scopeFactory,
        HttpClient httpClient,
        ILogger<GalileoQueuePollingService> logger)
    {
        _options = options.Value;
        _credentialStore = credentialStore;
        _sessionService = sessionService;
        _repository = repository;
        _emailService = emailService;
        _hub = hub;
        _errorLogService = errorLogService;
        _priorityPnrRepository = priorityPnrRepository;
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.GalileoPolling.Enabled)
        {
            _logger.LogInformation("Galileo queue polling is disabled.");
            await WriteFileLogAsync("Galileo queue polling is disabled.", stoppingToken, "WARN");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.GalileoPolling.IntervalMinutes));
        await WriteFileLogAsync($"Galileo queue polling started. Queues: {string.Join(", ", _options.GalileoPolling.Queues.Select(q => $"Q/{q}"))}. Interval: {interval.TotalMinutes} min.", stoppingToken);

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
                await _errorLogService.LogAsync(ex, "GalileoQueuePolling", "SkyOpsQueueIntelligence", "BACKGROUND", "ExecuteAsync", nameof(GalileoQueuePollingService), null, stoppingToken);
                await WriteFileLogAsync($"Galileo polling cycle error: {ex.Message}", stoppingToken, "ERROR");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task PollAllPccsAsync(CancellationToken cancellationToken)
    {
        var currentPnrsByQueue = _options.GalileoPolling.Queues.ToDictionary(
            q => q,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var reconciliationSkippedQueues = new HashSet<int>();

        var pccCodes = _credentialStore.GetAll()
            .Where(c => c.Provider.Equals("1G", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.PCCMasterCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (pccCodes.Count == 0)
        {
            const string message = "Galileo polling skipped: no active 1G PCC credentials were loaded.";
            _logger.LogWarning(message);
            await WriteFileLogAsync(message, cancellationToken, "WARN");
        }

        foreach (var pccCode in pccCodes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var credentials = _credentialStore.GetByPcc(pccCode)
                .Where(c => c.Provider.Equals("1G", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var username = GetTag(credentials, "SessionUserName");
            var password = GetTag(credentials, "SessionPassword");
            var sessionProfile = GetTag(credentials, "SessionProfileValue");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                _logger.LogWarning("Galileo PCC {PccCode} has no SessionUserName or SessionPassword.", pccCode);
                await WriteFileLogAsync($"Galileo PCC {pccCode}: missing SessionUserName or SessionPassword, skipping.", cancellationToken, "WARN");
                reconciliationSkippedQueues.UnionWith(_options.GalileoPolling.Queues);
                continue;
            }

            if (string.IsNullOrWhiteSpace(sessionProfile))
            {
                _logger.LogError("Galileo PCC {PccCode} has no SessionProfileValue.", pccCode);
                await WriteFileLogAsync($"Galileo PCC {pccCode}: missing SessionProfileValue, skipping.", cancellationToken, "ERROR");
                reconciliationSkippedQueues.UnionWith(_options.GalileoPolling.Queues);
                continue;
            }

            GalileoSession? session = null;
            try
            {
                session = await _sessionService.CreateSessionAsync(pccCode, sessionProfile, cancellationToken);
                if (session is null)
                {
                    reconciliationSkippedQueues.UnionWith(_options.GalileoPolling.Queues);
                    continue;
                }

                var queueSummaries = new List<(string HostCommand, int QueueNumber, int AnalyzedCount, int SavedCount)>();

                foreach (var queueNumber in _options.GalileoPolling.Queues)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var (analyzedCount, savedCount, currentPnrs) = await ProcessQueueAsync(
                            pccCode, username, password, session, queueNumber, cancellationToken);

                        queueSummaries.Add(($"Q/{queueNumber}", queueNumber, analyzedCount, savedCount));
                        currentPnrsByQueue[queueNumber].UnionWith(currentPnrs);
                    }
                    catch (Exception ex)
                    {
                        reconciliationSkippedQueues.Add(queueNumber);
                        queueSummaries.Add(($"Q/{queueNumber}", queueNumber, 0, 0));
                        await _errorLogService.LogAsync(ex, $"GalileoQueuePolling|PCC:{pccCode}|Q/{queueNumber}", "SkyOpsQueueIntelligence", "BACKGROUND", "PollAllPccsAsync", nameof(GalileoQueuePollingService), null, cancellationToken);
                        await WriteFileLogAsync($"Galileo PCC {pccCode} Q/{queueNumber} failed: {ex.Message}", cancellationToken, "ERROR");
                        _logger.LogError(ex, "Galileo PCC {PccCode} Q/{QueueNumber} failed.", pccCode, queueNumber);
                    }
                }

                await _emailService.SendQueueProcessingSummaryAsync(pccCode, pccCode, queueSummaries, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                reconciliationSkippedQueues.UnionWith(_options.GalileoPolling.Queues);
                await _errorLogService.LogAsync(ex, $"GalileoQueuePolling|PCC:{pccCode}", "SkyOpsQueueIntelligence", "BACKGROUND", "PollAllPccsAsync", nameof(GalileoQueuePollingService), null, cancellationToken);
                await WriteFileLogAsync($"Galileo PCC {pccCode} error: {ex.Message}", cancellationToken, "ERROR");
                _logger.LogError(ex, "Galileo PCC {PccCode} polling failed.", pccCode);
            }
        }

        foreach (var (queueNumber, currentPnrs) in currentPnrsByQueue)
        {
            if (reconciliationSkippedQueues.Contains(queueNumber))
                continue;

            await _repository.MarkPnrsNotInQueueAsync(queueNumber, currentPnrs, cancellationToken);
        }
    }

    private async Task<(int AnalyzedCount, int SavedCount, IReadOnlyCollection<string> CurrentPnrs)> ProcessQueueAsync(
        string pccCode,
        string username,
        string password,
        GalileoSession session,
        int queueNumber,
        CancellationToken cancellationToken)
    {
        var pnrTexts = new List<string>();
        try
        {
            var response = await SendCommandAsync(pccCode, username, password, session, $"Q/{queueNumber}", cancellationToken);
            const int maxItems = 500;
            string? firstPnr = null;

            _logger.LogInformation("Galileo PCC {PccCode} Q/{QueueNumber} returned {ResponseLength} characters after session creation.",
                pccCode, queueNumber, response.Length);

            while (!IsQueueEmpty(response) && pnrTexts.Count < maxItems)
            {
                var currentPnr = ExtractQueuePnr(response);
                if (firstPnr is null && currentPnr is not null)
                {
                    firstPnr = currentPnr;
                }
                else if (currentPnr is not null && currentPnr.Equals(firstPnr, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Galileo PCC {PccCode} Q/{QueueNumber}: first PNR {Pnr} returned again; stopping queue scan.",
                        pccCode, queueNumber, firstPnr);
                    break;
                }

                if (!string.IsNullOrWhiteSpace(response))
                {
                    var enrichedText = await EnrichWithVendorDataAsync(
                        pccCode, username, password, session, currentPnr, response, cancellationToken);
                    pnrTexts.Add(enrichedText);
                }

                _logger.LogInformation("Galileo PCC {PccCode} Q/{QueueNumber}: advancing queue with I command; item {ItemNumber}.",
                    pccCode, queueNumber, pnrTexts.Count);
                response = await SendCommandAsync(pccCode, username, password, session, "I", cancellationToken);

                if (response.ToUpperInvariant().Contains("IGNORED"))
                {
                    _logger.LogInformation("Galileo PCC {PccCode} Q/{QueueNumber}: I command returned IGNORED; exiting queue loop.", pccCode, queueNumber);
                    break;
                }
            }
        }
        finally
        {
            try
            {
                await SendCommandAsync(pccCode, username, password, session, "QXI", cancellationToken);
                _logger.LogInformation("Galileo PCC {PccCode} Q/{QueueNumber}: exited queue with QXI.", pccCode, queueNumber);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Galileo PCC {PccCode} Q/{QueueNumber}: QXI queue exit failed.", pccCode, queueNumber);
            }
        }

        if (pnrTexts.Count == 0)
        {
            var emptyMsg = $"Galileo PCC {pccCode} Q/{queueNumber}: Queue empty.";
            _logger.LogInformation("{Message}", emptyMsg);
            await WriteFileLogAsync(emptyMsg, cancellationToken);
            await _repository.SaveProcessingLogAsync($"Galileo|{pccCode}", "", 0, 0, 0, "Empty", emptyMsg, session.UplId, cancellationToken);
            return (0, 0, Array.Empty<string>());
        }

        var combinedText = string.Join(Environment.NewLine, pnrTexts);
        var analysisResults = Queue7Processor.ProcessQueueText(combinedText, queueNumber);
        var actionCount = analysisResults.Sum(result => result.Actions.Count);
        _logger.LogInformation("Galileo PCC {PccCode} Q/{QueueNumber}: parsed {PnrCount} PNRs and {ActionCount} actions before database save. TransDB configured: {IsConfigured}.",
            pccCode, queueNumber, analysisResults.Count, actionCount, _repository.IsConfigured);

        if (analysisResults.Count > 0 && actionCount == 0)
        {
            await WriteFileLogAsync($"Galileo PCC {pccCode} Q/{queueNumber}: received {analysisResults.Count} PNRs, but no flight segments were parsed; nothing to save.", cancellationToken, "WARN");
        }

        var (savedCount, changedResults) = await _repository.SaveRecommendedActionsAsync(analysisResults, session.UplId, "1G", cancellationToken);

        if (changedResults.Count > 0)
        {
            await _emailService.SendAlertAsync(pccCode, changedResults, cancellationToken);
            await QueueNotificationsHub.SendQueueNotificationAsync(_hub, $"Galileo PCC {pccCode}: {changedResults.Count} PNR(s) need attention.", cancellationToken);
        }

        var changedPnrSet = new HashSet<string>(changedResults.Select(r => r.Pnr), StringComparer.OrdinalIgnoreCase);

        foreach (var result in analysisResults.Where(r => r.RequiresAction && r.Actions.Any(a => a.ShouldNotify)
            && changedPnrSet.Contains(r.Pnr)))
        {
            var priorityEntry = await _priorityPnrRepository.GetByPnrAsync(result.Pnr, cancellationToken)
                ?? (!string.IsNullOrWhiteSpace(result.RemarkEmail)
                    ? await _priorityPnrRepository.GetByRemarkEmailAsync(
                        result.RemarkEmail.Split(';', ',')[0].Trim(), cancellationToken)
                    : null);
            if (priorityEntry is null) continue;

            var emails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in (priorityEntry.NotifyEmail ?? string.Empty)
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(e => !string.IsNullOrWhiteSpace(e)))
                emails.Add(e);

            if (!string.IsNullOrWhiteSpace(priorityEntry.Users))
            {
                var userIds = priorityEntry.Users
                    .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(s => int.TryParse(s, out var id) ? id : (int?)null)
                    .Where(id => id.HasValue).Select(id => id!.Value).ToList();

                if (userIds.Count > 0)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                    foreach (var ue in await userRepository.GetEmailsByUserIdsAsync(userIds, cancellationToken))
                        emails.Add(ue);
                }
            }

            if (emails.Count > 0)
                await _emailService.SendPriorityPnrAlertAsync(result.Pnr, emails.ToList(), new[] { result }, cancellationToken);
        }

        foreach (var result in analysisResults.Where(r => r.RequiresAction && !string.IsNullOrWhiteSpace(r.RemarkEmail)
            && changedPnrSet.Contains(r.Pnr)))
        {
            await _emailService.SendRemarkEmailNotificationAsync(result.Pnr, result.RemarkEmail!, new[] { result }, cancellationToken);
        }

        var msg = $"Galileo PCC {pccCode} Q/{queueNumber}: analyzed {analysisResults.Count} PNRs, saved {savedCount} actions.";
        _logger.LogInformation("{Message}", msg);
        await WriteFileLogAsync(msg, cancellationToken);

        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combinedText)));
        await _repository.SaveProcessingLogAsync($"Galileo|{pccCode}", contentHash, analysisResults.Count, savedCount, 1, "Updated", msg, session.UplId, cancellationToken);

        return (analysisResults.Count, savedCount, analysisResults.Select(r => r.Pnr).ToArray());
    }

    private async Task<string> EnrichWithVendorDataAsync(
        string pccCode,
        string username,
        string password,
        GalileoSession session,
        string? pnr,
        string pnrText,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pnr))
            return pnrText;

        var sb = new StringBuilder(pnrText);
        try
        {
            var vlResponse = await SendCommandAsync(pccCode, username, password, session, $"*VL {pnr}", cancellationToken);
            if (!string.IsNullOrWhiteSpace(vlResponse))
                sb.AppendLine().AppendLine("*VL").AppendLine(vlResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Galileo PCC {PccCode}: *VL failed for PNR {Pnr}.", pccCode, pnr);
        }

        try
        {
            var vrResponse = await SendCommandAsync(pccCode, username, password, session, $"*VR {pnr}", cancellationToken);
            if (!string.IsNullOrWhiteSpace(vrResponse))
                sb.AppendLine().AppendLine("*VR").AppendLine(vrResponse);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Galileo PCC {PccCode}: *VR failed for PNR {Pnr}.", pccCode, pnr);
        }

        return sb.ToString();
    }

    private async Task<string> SendCommandAsync(
        string pccCode,
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
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.TryAddWithoutValidation("SOAPAction", "http://webservices.galileo.com/SubmitTerminalTransaction");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        await _repository.SaveApiLogAsync(
            pccCode: pccCode,
            serviceName: "GalileoQueuePolling",
            hostCommand: command,
            requestXml: envelope,
            responseXml: responseText,
            httpStatusCode: (int)response.StatusCode,
            status: response.IsSuccessStatusCode ? "Success" : "Failed",
            uplId: session.UplId,
            workFlow: "GalileoQueuePolling",
            moduleName: "SkyOpsQueueIntelligence",
            moduleCode: "GALILEO",
            cancellationToken: cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Galileo command '{command}' failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {Truncate(responseText)}");

        var transactionText = ExtractTransactionText(responseText);
        _logger.LogInformation("Galileo command {Command} returned HTTP {StatusCode} and {ResponseLength} transaction characters.",
            command, (int)response.StatusCode, transactionText.Length);
        return transactionText;
    }

    private static string ExtractTransactionText(string responseText)
    {
        try
        {
            var document = XDocument.Parse(responseText);
            var fault = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault");
            if (fault is not null)
                throw new InvalidOperationException($"Galileo SOAP fault: {Truncate(fault.Value)}");

            var result = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "SubmitTerminalTransactionResult");
            if (result is not null)
                return result.Value.Trim();

            return responseText.Trim();
        }
        catch (XmlException)
        {
            return responseText;
        }
    }

    private static string Truncate(string value)
        => value.Length <= 500 ? value : value[..500];

    private static bool IsQueueEmpty(string text)
    {
        var upper = text.ToUpperInvariant();
        return string.IsNullOrWhiteSpace(text)
            || upper.Contains("QUEUE EMPTY")
            || upper.Contains("NO ITEMS")
            || upper.Contains("END OF QUEUE")
            || upper.Contains("0 ITEMS")
            || upper.Contains("IGNORED");
    }

    private static string? ExtractQueuePnr(string response)
    {
        foreach (var rawLine in response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            var slashIndex = line.IndexOf('/');
            if (slashIndex <= 0)
                continue;

            var candidate = line[..slashIndex].Trim();
            if (candidate.Length is >= 5 and <= 8 && candidate.All(char.IsLetterOrDigit))
                return candidate.ToUpperInvariant();
        }

        return null;
    }

    private async Task WriteFileLogAsync(string message, CancellationToken cancellationToken, string level = "INFO")
    {
        var logPath = Path.IsPathRooted(_options.LogFilePath)
            ? _options.LogFilePath
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _options.LogFilePath));

        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} | {message}{Environment.NewLine}";
        await File.AppendAllTextAsync(logPath, line, cancellationToken);

        try { await QueueNotificationsHub.SendWorkflowLogAsync(_hub, level, message, cancellationToken); }
        catch { /* non-critical */ }
    }

    private static string? GetTag(IEnumerable<StorePccCredential> credentials, string tagName)
        => credentials.FirstOrDefault(c =>
            c.TagName.Trim().Equals(tagName, StringComparison.OrdinalIgnoreCase))?.TagValue.Trim();

    private static string Escape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
