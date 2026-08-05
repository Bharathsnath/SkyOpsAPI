using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Response;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Tools;

[McpServerToolType]
public sealed class Queue7AnalysisTools
{
    private readonly IQueueAnalysisService _service;
    private readonly IErrorLogService _errorLogService;
    private readonly ILogger<Queue7AnalysisTools> _logger;

    public Queue7AnalysisTools(IQueueAnalysisService service, IErrorLogService errorLogService, ILogger<Queue7AnalysisTools> logger)
    {
        _service = service;
        _errorLogService = errorLogService;
        _logger = logger;
    }

    [McpServerTool(Name = "parse_queue_text")]
    [Description("Parses queue text for queue 7, 379, or 62 and extracts PNR information. Analysis only; no Sabre commands are executed.")]
    public ParsedQueueResult ParseQueueText(string queueText, int queueNumber = 7)
    {
        try
        {
            return _service.ParseQueueText(queueText, queueNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP parse_queue_text failed");
            _ = _errorLogService.LogAsync(ex, "MCP", "SkyOpsQueueIntelligence", "MCP", "ParseQueueText", nameof(Queue7AnalysisTools));
            throw;
        }
    }

    [McpServerTool(Name = "parse_segments")]
    [Description("Extracts flight number, carrier, date, origin, destination, status code, departure time, and arrival time from PNR text.")]
    public IReadOnlyList<FlightSegment> ParseSegments(string pnrText)
    {
        try
        {
            return _service.ParseSegments(pnrText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP parse_segments failed");
            _ = _errorLogService.LogAsync(ex, "MCP", "SkyOpsQueueIntelligence", "MCP", "ParseSegments", nameof(Queue7AnalysisTools));
            throw;
        }
    }

    [McpServerTool(Name = "queue_processor")]
    [Description("Analyzes queue PNR text for queue 7, 379, or 62 and returns recommendation-only servicing actions for TK, HX, UN, UC, US, and WL segments.")]
    public IReadOnlyList<QueueAnalysisResult> QueueProcessor(string queueText, int queueNumber = 7)
    {
        try
        {
            return _service.Analyze(queueText, queueNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP queue_processor failed");
            _ = _errorLogService.LogAsync(ex, "MCP", "SkyOpsQueueIntelligence", "MCP", "QueueProcessor", nameof(Queue7AnalysisTools));
            throw;
        }
    }

    [McpServerTool(Name = "store_recommended_actions")]
    [Description("Analyzes supplied queue text (queue 7, 379, or 62) and stores recommendation-only action findings in MySQL. No Sabre commands are executed.")]
    public async Task<object> StoreRecommendedActions(string queueText, int queueNumber = 7)
    {
        try
        {
            var result = await _service.AnalyzeAndStoreAsync(queueText, queueNumber);
            return new { queue = result.Queue, pnrCount = result.PnrCount, savedActionCount = result.SavedActionCount, databaseConfigured = result.DatabaseConfigured, message = result.Message };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MCP store_recommended_actions failed");
            await _errorLogService.LogAsync(ex, "MCP", "SkyOpsQueueIntelligence", "MCP", "StoreRecommendedActions", nameof(Queue7AnalysisTools));
            throw;
        }
    }
}
