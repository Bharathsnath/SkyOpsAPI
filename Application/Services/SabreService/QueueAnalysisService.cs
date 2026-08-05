using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Response;
using SkyOpsQueueIntelligence.Application.Helpers;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.Proxy;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Application.Services;

public sealed class QueueAnalysisService : IQueueAnalysisService
{
    private readonly IQueue7TextSource _textSource;
    private readonly IQueueActionRepository _repository;

    public QueueAnalysisService(IQueue7TextSource textSource, IQueueActionRepository repository)
    {
        _textSource = textSource;
        _repository = repository;
    }

    public bool IsDatabaseConfigured => _repository.IsConfigured;

    public ParsedQueueResult ParseQueueText(string queueText, int queueNumber = 7)
        => Queue7Parser.ParseQueueText(queueText, queueNumber);

    public IReadOnlyList<FlightSegment> ParseSegments(string pnrText)
        => Queue7Parser.ParseSegments(pnrText);

    public IReadOnlyList<QueueAnalysisResult> Analyze(string queueText, int queueNumber = 7)
        => Queue7Processor.ProcessQueueText(queueText, queueNumber);

    public async Task<QueueStoreResult> AnalyzeAndStoreAsync(string queueText, int queueNumber = 7, CancellationToken cancellationToken = default)
    {
        var results = Queue7Processor.ProcessQueueText(queueText, queueNumber);
        var (savedCount, _) = await _repository.SaveRecommendedActionsAsync(results, "", "", cancellationToken);
        return new QueueStoreResult(queueNumber, results.Count, savedCount, _repository.IsConfigured);
    }

    public async Task<QueueStoreResult> FetchAnalyzeAndStoreAsync(int queueNumber, CancellationToken cancellationToken = default)
    {
        var sourceResult = await _textSource.GetQueueAnalysisTextForCommandAsync($"Q/{queueNumber}", cancellationToken);
        return await AnalyzeAndStoreAsync(sourceResult.QueueText, queueNumber, cancellationToken);
    }

    public async Task<DelaySummaryResult> GetDelaySummaryAsync(int queueNumber, CancellationToken cancellationToken = default)
    {
        var sourceResult = await _textSource.GetQueueAnalysisTextForCommandAsync($"Q/{queueNumber}", cancellationToken);
        var parsed = Queue7Parser.ParseQueueText(sourceResult.QueueText, queueNumber);

        var delayedFlights = parsed.Pnrs
            .SelectMany(pnr => pnr.Segments
                .Where(s => s.OldDepartureTime is not null || s.OldArrivalTime is not null)
                .Select(s => new DelayFlight(pnr.Pnr, s.Flight, s.Date, s.Origin, s.Destination,
                    s.OldDepartureTime, s.OldArrivalTime,
                    s.NewDepartureTime ?? s.DepartureTime, s.NewArrivalTime ?? s.ArrivalTime)))
            .ToArray();

        return new DelaySummaryResult(queueNumber, delayedFlights.Length, delayedFlights);
    }

    public async Task<QueueSummaryResult> GetSummaryAsync(int queueNumber, CancellationToken cancellationToken = default)
    {
        var sourceResult = await _textSource.GetQueueAnalysisTextForCommandAsync($"Q/{queueNumber}", cancellationToken);
        var results = Queue7Processor.ProcessQueueText(sourceResult.QueueText, queueNumber);

        var pnrSummaries = results.Select(r => new PnrActionSummary(r.Pnr, r.RequiresAction, r.Summary, r.Actions)).ToArray();
        var totalActions = results.Sum(r => r.Actions.Count);
        var actionablePnrs = results.Count(r => r.RequiresAction);
        var overallSummary = totalActions == 0
            ? "No actions required across all PNRs."
            : $"{actionablePnrs} PNR(s) require action — {totalActions} total action(s) found.";

        return new QueueSummaryResult(queueNumber, results.Count, actionablePnrs, totalActions, overallSummary, pnrSummaries);
    }
}

