using Microsoft.Extensions.Options;
using SkyOpsQueueIntelligence.Application.Helpers;
using SkyOpsQueueIntelligence.Application.Proxy;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.BackgroundJobs;

public sealed class AmadeusQueuePollingService : BackgroundService
{
    private readonly Queue7PollingOptions _options;
    private readonly ICredentialStore _credentialStore;
    private readonly IAmadeusSessionService _sessionService;
    private readonly IQueueActionRepository _repository;
    private readonly ILogger<AmadeusQueuePollingService> _logger;

    public AmadeusQueuePollingService(IOptions<Queue7PollingOptions> options, ICredentialStore credentialStore,
        IAmadeusSessionService sessionService, IQueueActionRepository repository,
        ILogger<AmadeusQueuePollingService> logger)
    {
        _options = options.Value;
        _credentialStore = credentialStore;
        _sessionService = sessionService;
        _repository = repository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var polling = _options.AmadeusPolling;
        if (!polling.Enabled)
        {
            _logger.LogInformation("Amadeus queue polling is disabled.");
            return;
        }

        var interval = TimeSpan.FromMinutes(Math.Max(1, polling.IntervalMinutes));
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PollAsync(polling, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Amadeus queue polling cycle failed."); }
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task PollAsync(AmadeusPollingOptions polling, CancellationToken cancellationToken)
    {
        var credentials = _credentialStore.GetByPcc(polling.PccCode)
            .Where(c => c.RecordStatus == 0 && c.Provider.Equals("AM", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (credentials.Count == 0)
        {
            _logger.LogWarning("No active Amadeus credentials found for PCC {PccCode}.", polling.PccCode);
            return;
        }

        var session = await _sessionService.CreateSessionAsync(polling.PccCode, cancellationToken);
        if (session is null) return;
        try
        {
            foreach (var queueNumber in polling.Queues)
            {
                var texts = new List<string>();
                if (!polling.QueueCommands.TryGetValue(queueNumber, out var queueCommand))
                {
                    _logger.LogWarning("No Amadeus queue command configured for queue {QueueNumber}.", queueNumber);
                    continue;
                }

                var response = await _sessionService.SendCommandAsync(session, queueCommand, cancellationToken);
                string? firstPnr = null;
                try
                {
                    for (var item = 0; item < 500 && !IsQueueEmpty(response); item++)
                    {
                        var currentPnr = ExtractPnr(response, queueNumber);
                        if (currentPnr is not null)
                        {
                            if (firstPnr is null)
                            {
                                firstPnr = currentPnr;
                            }
                            else if (currentPnr.Equals(firstPnr, StringComparison.OrdinalIgnoreCase))
                            {
                                _logger.LogInformation("Amadeus PCC {PccCode} Q/{QueueNumber}: first PNR {Pnr} returned again; exiting queue scan.",
                                    polling.PccCode, queueNumber, firstPnr);
                                break;
                            }
                        }

                        texts.Add(response);
                        response = await _sessionService.SendCommandAsync(session, "IG", cancellationToken);
                    }
                }
                finally
                {
                    await _sessionService.SendCommandAsync(session, "QI", cancellationToken);
                }

                if (texts.Count == 0) continue;
                var results = Queue7Processor.ProcessQueueText(string.Join(Environment.NewLine, texts), queueNumber);
                var saved = await _repository.SaveRecommendedActionsAsync(results, session.UplId, "AM", cancellationToken);
                _logger.LogInformation("Amadeus PCC {PccCode} Q/{QueueNumber}: analyzed {Analyzed} PNRs, saved {Saved} actions.",
                    polling.PccCode, queueNumber, results.Count, saved.Saved);
                _logger.LogInformation("Amadeus PCC {PccCode} Q/{QueueNumber}: extracted PNRs {Pnrs}.",
                    polling.PccCode, queueNumber, string.Join(", ", results.Select(result => result.Pnr)));
            }
        }
        finally
        {
            await _sessionService.CloseSessionAsync(session, cancellationToken);
        }
    }

    private static bool IsQueueEmpty(string response)
    {
        var text = response.ToUpperInvariant();
        return string.IsNullOrWhiteSpace(response) || text.Contains("QUEUE EMPTY") ||
            text.Contains("NO ITEMS") || text.Contains("END OF QUEUE") || text.Contains("0 ITEMS") ||
            text.Contains("QUEUE/DATE RANGE EMPTY");
    }

    private static string? ExtractPnr(string response, int queueNumber)
    {
        var result = Queue7Processor.ProcessQueueText(response, queueNumber).FirstOrDefault();
        return result is null || string.IsNullOrWhiteSpace(result.Pnr) ||
            result.Pnr.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
            ? null
            : result.Pnr.Trim();
    }
}