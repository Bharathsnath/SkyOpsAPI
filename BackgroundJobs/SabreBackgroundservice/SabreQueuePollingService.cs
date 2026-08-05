using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyOpsQueueIntelligence.Application.Helpers;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.Proxy;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Hubs;

namespace SkyOpsQueueIntelligence.BackgroundJobs;

public sealed class SabreQueuePollingService : BackgroundService, IQueue7PollingTrigger
{
    private sealed record QueuePollResult(
        string HostCommand,
        int QueueNumber,
        int AnalyzedCount,
        int SavedCount,
        IReadOnlyCollection<string> CurrentPnrs);

    private readonly Queue7PollingOptions _options;
    private readonly IQueueActionRepository _repository;
    private readonly ICredentialStore _credentialStore;
    private readonly ISabreSessionService _sessionService;
    private readonly IQueue7TextSource _textSource;
    private readonly IEmailNotificationService _emailService;
    private readonly ISabreXmlLogService _xmlLogService;
    private readonly IHubContext<QueueNotificationsHub> _hub;
    private readonly ILogger<SabreQueuePollingService> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IErrorLogService _errorLogService;
    private readonly IPriorityPnrRepository _priorityPnrRepository;
    private readonly IServiceScopeFactory _scopeFactory;

    public SabreQueuePollingService(
        IOptions<Queue7PollingOptions> options,
        IQueueActionRepository repository,
        ICredentialStore credentialStore,
        ISabreSessionService sessionService,
        IQueue7TextSource textSource,
        IEmailNotificationService emailService,
        ISabreXmlLogService xmlLogService,
        IHubContext<QueueNotificationsHub> hub,
        ILogger<SabreQueuePollingService> logger,
        IHostEnvironment environment,
        IErrorLogService errorLogService,
        IPriorityPnrRepository priorityPnrRepository,
        IServiceScopeFactory scopeFactory)
    {
        _options = options.Value;
        _repository = repository;
        _credentialStore = credentialStore;
        _sessionService = sessionService;
        _textSource = textSource;
        _emailService = emailService;
        _xmlLogService = xmlLogService;
        _hub = hub;
        _logger = logger;
        _environment = environment;
        _errorLogService = errorLogService;
        _priorityPnrRepository = priorityPnrRepository;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Queue polling is disabled.");
            await WriteFileLogAsync("Queue polling is disabled.", stoppingToken, "WARN");
            return;
        }

        var queues = GetQueueEntries();
        var interval = TimeSpan.FromMinutes(Math.Max(1, _options.IntervalMinutes));
        await WriteFileLogAsync($"Queue polling started. Queues: {string.Join(", ", queues.Select(q => q.HostCommand))}. Interval: {interval.TotalMinutes} min.", stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAllPccsAsync(queues, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Queue polling cycle failed. Retrying in {Interval} min.", interval.TotalMinutes);
                await _errorLogService.LogAsync(ex, "QueuePolling", "SkyOpsQueueIntelligence", "BACKGROUND", "ExecuteAsync", nameof(SabreQueuePollingService), null, stoppingToken);
                await WriteFileLogAsync($"Polling cycle error: {ex.Message}", stoppingToken, "ERROR");
            }

            await Task.Delay(interval, stoppingToken);
        }
    }

    public async Task TriggerAsync(CancellationToken cancellationToken)
    {
        var queues = GetQueueEntries();
        await PollAllPccsAsync(queues, cancellationToken);
    }

    private async Task PollAllPccsAsync(IReadOnlyList<(string HostCommand, int QueueNumber)> queues, CancellationToken cancellationToken)
    {
        var currentPnrsByQueue = queues.ToDictionary(
            entry => entry.QueueNumber,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var reconciliationSkippedQueues = new HashSet<int>();

        // Get distinct PCC credentials by actual Sabre login identity.
        // Multiple PCCMasterCode labels can share the same SourceOffice + Username + Password,
        // so we must dedupe by that triplet instead of by the master label itself.
        var allCreds = _credentialStore.GetAll();
        var pccGroups = allCreds
            .GroupBy(c => c.PCCMasterCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => new
            {
                PccCode = g.Key,
                Username = g.FirstOrDefault(c => c.TagName.Equals("UserName", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty,
                Password = g.FirstOrDefault(c => c.TagName.Equals("Password", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty,
                SourceOffice = g.FirstOrDefault(c => c.TagName.Equals("SourceOffice", StringComparison.OrdinalIgnoreCase))?.TagValue ?? g.Key,
                Company = g.FirstOrDefault(c => c.TagName.Equals("Company", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty,
                Branch = g.FirstOrDefault(c => c.TagName.Equals("Branch", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty,
                Market = g.FirstOrDefault(c => c.TagName.Equals("Market", StringComparison.OrdinalIgnoreCase))?.TagValue ?? string.Empty
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Username) && !string.IsNullOrWhiteSpace(x.Password))
            .GroupBy(x => $"{x.SourceOffice}|{x.Username}|{x.Password}|{x.Company}|{x.Branch}|{x.Market}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (pccGroups.Count == 0)
        {
            await WriteFileLogAsync("No PCCs with UserName/Password found in credential store. Falling back to config token.", cancellationToken);
            // Fallback to existing config-based polling
            foreach (var entry in queues)
            {
                try
                {
                    var currentPnrs = await ProcessQueueWithConfigTokenAsync(entry.HostCommand, entry.QueueNumber, cancellationToken);
                    currentPnrsByQueue[entry.QueueNumber].UnionWith(currentPnrs);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    reconciliationSkippedQueues.Add(entry.QueueNumber);
                    await _errorLogService.LogAsync(ex, $"QueuePolling|Fallback|{entry.HostCommand}", "SkyOpsQueueIntelligence", "BACKGROUND", "PollAllPccsAsync", nameof(SabreQueuePollingService), null, cancellationToken);
                    await WriteFileLogAsync($"{entry.HostCommand} fallback polling failed: {ex.Message}", cancellationToken, "ERROR");
                }
            }

            await ReconcileMissingPnrsAsync(currentPnrsByQueue, reconciliationSkippedQueues, cancellationToken);
            return;
        }

        await WriteFileLogAsync($"Processing {pccGroups.Count} PCC(s)...", cancellationToken);

        foreach (var pccGroup in pccGroups)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var pccCode = pccGroup.PccCode;
            var username = pccGroup.Username;
            var password = pccGroup.Password;
            var sourceOffice = pccGroup.SourceOffice;
            var displayPcc = sourceOffice;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                reconciliationSkippedQueues.UnionWith(queues.Select(entry => entry.QueueNumber));
                _logger.LogWarning("PCC {Pcc}: missing UserName or Password, skipping.", pccCode);
                await WriteFileLogAsync($"PCC {pccCode}: missing UserName or Password, skipping.", cancellationToken, "WARN");
                continue;
            }

            SabreSession? session = null;
            try
            {
                // 1. Create session
                session = await _sessionService.CreateSessionAsync(username, password, sourceOffice, cancellationToken);
                if (session is null)
                {
                    reconciliationSkippedQueues.UnionWith(queues.Select(entry => entry.QueueNumber));
                    await WriteFileLogAsync($"PCC {pccCode}: Session creation failed. Skipping.", cancellationToken, "ERROR");
                    continue;
                }

                // 2. Process each queue with this session
                var queueSummaries = new List<(string HostCommand, int QueueNumber, int AnalyzedCount, int SavedCount)>();
                foreach (var entry in queues)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        var summary = await ProcessQueueForPccAsync(pccCode, displayPcc, session, entry.HostCommand, entry.QueueNumber, cancellationToken);
                        queueSummaries.Add((summary.HostCommand, summary.QueueNumber, summary.AnalyzedCount, summary.SavedCount));
                        currentPnrsByQueue[entry.QueueNumber].UnionWith(summary.CurrentPnrs);
                    }
                    catch (Exception ex)
                    {
                        reconciliationSkippedQueues.Add(entry.QueueNumber);
                        queueSummaries.Add((entry.HostCommand, entry.QueueNumber, 0, 0));
                        await _errorLogService.LogAsync(ex, $"QueuePolling|PCC:{pccCode}|{entry.HostCommand}", "SkyOpsQueueIntelligence", "BACKGROUND", "PollAllPccsAsync", nameof(SabreQueuePollingService), null, cancellationToken);
                        await WriteFileLogAsync($"PCC {pccCode} {entry.HostCommand} failed: {ex.Message}", cancellationToken, "ERROR");
                    }
                }

                await _emailService.SendQueueProcessingSummaryAsync(pccCode, displayPcc, queueSummaries, cancellationToken);

            // var totalAnalyzed = queueSummaries.Sum(s => s.AnalyzedCount);
            // var totalSaved = queueSummaries.Sum(s => s.SavedCount);
            // var summaryMsg = $"PCC {displayPcc}: {totalAnalyzed} PNRs analyzed, {totalSaved} actions saved.";
            // //await QueueNotificationsHub.SendQueueSummaryAsync(_hub, summaryMsg, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                reconciliationSkippedQueues.UnionWith(queues.Select(entry => entry.QueueNumber));
                await _errorLogService.LogAsync(ex, $"QueuePolling|PCC:{pccCode}", "SkyOpsQueueIntelligence", "BACKGROUND", "PollAllPccsAsync", nameof(SabreQueuePollingService), null, cancellationToken);
                await WriteFileLogAsync($"PCC {pccCode} error: {ex.Message}", cancellationToken, "ERROR");
            }
            finally
            {
                // 3. Close session
                if (session is not null)
                {
                    await _sessionService.CloseSessionAsync(session, cancellationToken);
                }
            }
        }

        await ReconcileMissingPnrsAsync(currentPnrsByQueue, reconciliationSkippedQueues, cancellationToken);
    }

    private async Task<QueuePollResult> ProcessQueueForPccAsync(string pccCode, string displayPcc, SabreSession session, string hostCommand, int queueNumber, CancellationToken cancellationToken)
    {
        var combinedText = await FetchQueueTextWithSessionAsync(session, hostCommand, pccCode, session.UplId, cancellationToken);
        var sourceId = $"{_options.SabreApi.Endpoint}|PCC:{pccCode}";
        var providerName = _credentialStore.GetByPcc(pccCode).FirstOrDefault()?.Provider ?? "";

        // Persist raw queue text to a timestamped file for auditing
        // try
        // {
        //     var logPath = Path.IsPathRooted(_options.LogFilePath)
        //         ? _options.LogFilePath
        //         : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _options.LogFilePath));

        //     var directory = Path.GetDirectoryName(logPath) ?? Path.GetFullPath(_environment.ContentRootPath);
        //     if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        //     var safeCmd = string.Concat(hostCommand.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Replace(' ', '_').Replace('/', '_');
        //     var fileName = $"queue_{pccCode}_{safeCmd}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.txt";
        //     var file = Path.Combine(directory, fileName);

        //     await File.WriteAllTextAsync(file, combinedText ?? string.Empty, cancellationToken);
        // }
        // catch (Exception ex)
        // {
        //     _logger.LogWarning(ex, "Failed to write queue text file for PCC {Pcc} command {Cmd}", pccCode, hostCommand);
        // }

        if (string.IsNullOrWhiteSpace(combinedText))
        {
            var emptyMsg = $"PCC {displayPcc} {hostCommand}: Queue empty or no parsable items.";
            await WriteFileLogAsync(emptyMsg, cancellationToken);
            await _repository.SaveProcessingLogAsync(sourceId, "", 0, 0, 0, "Empty", emptyMsg, session.UplId, cancellationToken);
            return new QueuePollResult(hostCommand, queueNumber, 0, 0, Array.Empty<string>());
        }

        var analysisResults = Queue7Processor.ProcessQueueText(combinedText, queueNumber);
        var (savedCount, changedResults) = await _repository.SaveRecommendedActionsAsync(analysisResults, session.UplId, providerName, cancellationToken);

        // Send general queue alerts for newly inserted/updated records only.
        if (changedResults.Count > 0)
        {
            await _emailService.SendAlertAsync(displayPcc, changedResults, cancellationToken);
            await QueueNotificationsHub.SendQueueNotificationAsync(_hub, $"PCC {displayPcc}: {changedResults.Count} PNR(s) need attention.", cancellationToken);
        }

        // Priority PNR alerts: only fire for PNRs that had actual DB changes this cycle (new or updated).
        // This prevents re-alerting on restart for PNRs already in the queue with no new changes.
        var changedPnrSet = new HashSet<string>(changedResults.Select(r => r.Pnr), StringComparer.OrdinalIgnoreCase);
        foreach (var result in analysisResults.Where(r => r.RequiresAction && r.Actions.Any(a => a.ShouldNotify)
            && changedPnrSet.Contains(r.Pnr)))
        {
            // Look up by actual PNR first, then fall back to remark email if the entry was registered by email.
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
                    .Where(id => id.HasValue)
                    .Select(id => id!.Value)
                    .ToList();

                if (userIds.Count > 0)
                {
                    using var scope = _scopeFactory.CreateScope();
                    var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();
                    foreach (var ue in await userRepository.GetEmailsByUserIdsAsync(userIds, cancellationToken))
                        emails.Add(ue);
                }
            }

            if (emails.Count > 0)
            {
                await _emailService.SendPriorityPnrAlertAsync(result.Pnr, emails.ToList(), new[] { result }, cancellationToken);
            }
        }

        // Send remark-email alerts only for PNRs with actual DB changes this cycle.
        foreach (var result in analysisResults.Where(r => r.RequiresAction && !string.IsNullOrWhiteSpace(r.RemarkEmail)
            && changedPnrSet.Contains(r.Pnr)))
        {
            await _emailService.SendRemarkEmailNotificationAsync(result.Pnr, result.RemarkEmail!, new[] { result }, cancellationToken);
        }

        var msg = $"PCC {displayPcc} {hostCommand}: analyzed {analysisResults.Count} PNRs, saved {savedCount} actions.";
        await WriteFileLogAsync(msg, cancellationToken);

        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combinedText)));
        await _repository.SaveProcessingLogAsync(sourceId, contentHash, analysisResults.Count, savedCount, 1, "Updated", msg, session.UplId, cancellationToken);
        return new QueuePollResult(
            hostCommand,
            queueNumber,
            analysisResults.Count,
            savedCount,
            analysisResults.Select(result => result.Pnr).ToArray());
    }

    private async Task<string> FetchQueueTextWithSessionAsync(SabreSession session, string hostCommand, string pccCode, string uplId, CancellationToken cancellationToken)
    {
        var pnrTexts = new List<string>();
        const int maxItems = 500;

        var text = await SendSabreCommandAsync(session.BinarySecurityToken, session.ConversationId, hostCommand, pccCode, uplId, cancellationToken);

        if (IsQueueEmpty(text))
        {
            try { await SendSabreCommandAsync(session.BinarySecurityToken, session.ConversationId, "QXI", pccCode, uplId, cancellationToken); }
            catch { /* ignore */ }
            return string.Empty;
        }

        // If the initial response is a queue count/header screen (no PNR content), advance to first PNR.
        // If it already contains a PNR (Sabre returns first item directly), capture it immediately.
        if (IsQueueCountScreen(text))
        {
            text = await SendSabreCommandAsync(session.BinarySecurityToken, session.ConversationId, "I", pccCode, uplId, cancellationToken);
        }

        while (!IsQueueEmpty(text) && pnrTexts.Count < maxItems)
        {
            if (!string.IsNullOrWhiteSpace(text))
                pnrTexts.Add(text);

            text = await SendSabreCommandAsync(session.BinarySecurityToken, session.ConversationId, "I", pccCode, uplId, cancellationToken);
        }

        // Exit queue
        try { await SendSabreCommandAsync(session.BinarySecurityToken, session.ConversationId, "QXI", pccCode, uplId, cancellationToken); }
        catch { /* ignore */ }

        return string.Join(Environment.NewLine, pnrTexts);
    }

    private async Task<string> SendSabreCommandAsync(string token, string conversationId, string hostCommand, string pccCode, string uplId, CancellationToken cancellationToken)
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
                  <ConversationId>{Escape(conversationId)}</ConversationId>
                  <Service d4p1:type="Sabre Trip Management" xmlns:d4p1="http://www.ebxml.org/namespaces/messageHeader">SabreCommandLLSRQ</Service>
                  <Action>SabreCommandLLSRQ</Action>
                  <MessageData>
                    <MessageId>{Escape(messageId)}</MessageId>
                    <Timestamp>{timestamp}</Timestamp>
                  </MessageData>
                </MessageHeader>
                <Security xmlns="http://schemas.xmlsoap.org/ws/2002/12/secext">
                  <BinarySecurityToken>{Escape(token)}</BinarySecurityToken>
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

        using var request = new HttpRequestMessage(HttpMethod.Post, apiOptions.Endpoint);
        request.Content = new StringContent(envelope, Encoding.UTF8, "text/xml");
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/xml"));
        request.Headers.TryAddWithoutValidation("SOAPAction", "SabreCommandLLSRQ");

        var responseText = string.Empty;
        var statusCode = 0;
        var status = "SUCCESS";

        try
        {
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = await httpClient.SendAsync(request, cancellationToken);
            responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            statusCode = (int)response.StatusCode;

            if (!response.IsSuccessStatusCode)
            {
                status = "FAILED";
                throw new HttpRequestException($"Sabre command '{hostCommand}' failed: HTTP {statusCode}");
            }

            await _xmlLogService.LogSabreRequestResponseAsync(
                hostCommand: hostCommand,
                soapRequest: envelope,
                soapResponse: responseText,
                httpStatusCode: statusCode,
                pccCode: pccCode,
                status: status,
                uplId: uplId,
                cancellationToken: cancellationToken);

            return ExtractResponseText(responseText);
        }
        catch (Exception ex)
        {
            if (statusCode == 0)
                statusCode = 500;

            await _xmlLogService.LogSabreRequestResponseAsync(
                hostCommand: hostCommand,
                soapRequest: envelope,
                soapResponse: string.IsNullOrWhiteSpace(responseText) ? ex.Message : responseText,
                httpStatusCode: statusCode,
                pccCode: pccCode,
                status: "FAILED",
                uplId: uplId,
                cancellationToken: cancellationToken);

            throw;
        }
    }

    private async Task<IReadOnlyCollection<string>> ProcessQueueWithConfigTokenAsync(string hostCommand, int queueNumber, CancellationToken cancellationToken)
    {
        var pnrTexts = await _textSource.GetAllQueuePnrsAsync(hostCommand, cancellationToken);
        var combinedText = string.Join(Environment.NewLine, pnrTexts);
        var sourceId = _options.SabreApi.Endpoint;

        if (string.IsNullOrWhiteSpace(combinedText))
        {
            await WriteFileLogAsync($"{hostCommand} returned no PNR text (config token).", cancellationToken);
            return Array.Empty<string>();
        }

        var analysisResults = Queue7Processor.ProcessQueueText(combinedText, queueNumber);
        var (savedCount, _) = await _repository.SaveRecommendedActionsAsync(analysisResults, "", "", cancellationToken);
        await WriteFileLogAsync($"{hostCommand} (config): analyzed {analysisResults.Count} PNRs, saved {savedCount} actions.", cancellationToken);

        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(combinedText)));
        await _repository.SaveProcessingLogAsync(sourceId, contentHash, analysisResults.Count, savedCount, 0, "Updated", "", "", cancellationToken);
        return analysisResults.Select(result => result.Pnr).ToArray();
    }

    private async Task ReconcileMissingPnrsAsync(
        IReadOnlyDictionary<int, HashSet<string>> currentPnrsByQueue,
        IReadOnlySet<int> reconciliationSkippedQueues,
        CancellationToken cancellationToken)
    {
        foreach (var (queueNumber, currentPnrs) in currentPnrsByQueue)
        {
            if (reconciliationSkippedQueues.Contains(queueNumber))
            {
                await WriteFileLogAsync($"Queue {queueNumber}: skipped missing-PNR reconciliation because at least one PCC request failed.", cancellationToken, "WARN");
                continue;
            }

            await _repository.MarkPnrsNotInQueueAsync(queueNumber, currentPnrs, cancellationToken);
        }
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

    // Returns true only when Sabre responds with a queue count/header screen (no PNR data).
    // Sabre sometimes returns the first PNR directly on Q/N — in that case we must NOT discard it.
    private static bool IsQueueCountScreen(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var upper = text.ToUpperInvariant();
        // A count screen contains queue statistics but no passenger name or segment lines
        var hasQueueStats = upper.Contains("ITEMS IN QUEUE") || upper.Contains("QUEUE COUNT") || upper.Contains("QC/");
        var hasPnrContent = upper.Contains("RECEIVED FROM") || System.Text.RegularExpressions.Regex.IsMatch(upper, @"\d+\.\d+[A-Z]+/[A-Z]+") || System.Text.RegularExpressions.Regex.IsMatch(upper, @"\b(HK|HX|TK|UN|UC|WL)\d*\b");
        return hasQueueStats && !hasPnrContent;
    }

    private static string ExtractResponseText(string soapResponse)
    {
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(soapResponse);
            var element = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName is "Response" or "Text" or "Screen");
            return element?.Value.Trim() ?? soapResponse;
        }
        catch
        {
            return soapResponse;
        }
    }

    private IReadOnlyList<(string HostCommand, int QueueNumber)> GetQueueEntries()
    {
        if (_options.Queues.Count > 0)
        {
            return _options.Queues
                .Where(q => Queue7PollingOptions.AllowedHostCommands.Contains(q.HostCommand))
                .Select(q => (q.HostCommand, q.QueueNumber))
                .ToArray();
        }

        var cmd = _options.SabreApi.HostCommand;
        if (!Queue7PollingOptions.AllowedHostCommands.Contains(cmd))
            throw new InvalidOperationException($"Configured HostCommand '{cmd}' is not permitted.");

        var number = int.TryParse(cmd.Split('/').Last(), out var n) ? n : 7;
        return [(cmd, number)];
    }

    private async Task WriteFileLogAsync(string message, CancellationToken cancellationToken, string level = "INFO")
    {
        var logPath = Path.IsPathRooted(_options.LogFilePath)
            ? _options.LogFilePath
            : Path.GetFullPath(Path.Combine(_environment.ContentRootPath, _options.LogFilePath));

        var directory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} | {message}{Environment.NewLine}";
        await File.AppendAllTextAsync(logPath, line, cancellationToken);

        try { await QueueNotificationsHub.SendWorkflowLogAsync(_hub, level, message, cancellationToken); }
        catch { /* non-critical */ }
    }

    private static string Escape(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;
}
