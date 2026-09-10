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
                .Where(s => s.Status == "TK")
                .Select(s =>
                {
                    var oldDep = s.OldDepartureTime;
                    var newDep = s.NewDepartureTime ?? s.DepartureTime;
                    var oldArr = s.OldArrivalTime;
                    var newArr = s.NewArrivalTime ?? s.ArrivalTime;

                    var depChanged = !string.Equals(oldDep, newDep, StringComparison.OrdinalIgnoreCase);
                    var arrChanged = !string.Equals(oldArr, newArr, StringComparison.OrdinalIgnoreCase);
                    var hasOldTimes = oldDep is not null || oldArr is not null;

                    int? delayMinutes = null;
                    if (oldDep is not null && newDep is not null && depChanged)
                        delayMinutes = ComputeDelayMinutes(oldDep, newDep);

                    var changeType = ClassifyChange(depChanged, arrChanged, hasOldTimes, delayMinutes);

                    return new DelayFlight(pnr.Pnr, s.Flight, s.Date, s.Origin, s.Destination,
                        oldDep, oldArr, newDep, newArr, changeType, delayMinutes);
                }))
            .ToArray();

        return new DelaySummaryResult(queueNumber, delayedFlights.Length, delayedFlights);
    }

    private static int? ComputeDelayMinutes(string oldTime, string newTime)
    {
        static bool TryParse(string t, out int minutes)
        {
            var norm = Queue7Parser.NormalizeTime(t);
            if (norm.Length == 4 && int.TryParse(norm[..2], out var h) && int.TryParse(norm[2..], out var m)
                && h is >= 0 and <= 23 && m is >= 0 and <= 59)
            {
                minutes = h * 60 + m;
                return true;
            }
            minutes = 0;
            return false;
        }

        return TryParse(oldTime, out var o) && TryParse(newTime, out var n) ? n - o : null;
    }

    private static ScheduleChangeType ClassifyChange(bool depChanged, bool arrChanged, bool hasOldTimes, int? delayMinutes)
    {
        if (!hasOldTimes || (!depChanged && !arrChanged))
            return ScheduleChangeType.FlightChanged;
        if (delayMinutes is null)
            return depChanged || arrChanged ? ScheduleChangeType.Postponed : ScheduleChangeType.OnTime;
        return delayMinutes > 0 ? ScheduleChangeType.Postponed
            : delayMinutes < 0 ? ScheduleChangeType.Preponed
            : ScheduleChangeType.OnTime;
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

