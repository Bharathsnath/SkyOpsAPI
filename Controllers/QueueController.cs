using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Controllers;

[Authorize]
[ApiController]
[Route("api/")]
public class QueueController : ControllerBase
{
    private readonly IQueueAnalysisService _service;
   
    private static readonly int[] AllowedQueues = new[] { 7, 379, 62 };

    public QueueController(IQueueAnalysisService service, IQueueActionRepository repository)
    {
        _service = service;
      
    }

    [HttpGet("/queue-summary")]
    public async Task<IActionResult> GetQueueSummary(int? queue, CancellationToken cancellationToken)
    {
        var queueNumber = queue ?? 7;
        if (!AllowedQueues.Contains(queueNumber))
            return BadRequest(new { message = $"Queue {queueNumber} is not supported. Allowed: {string.Join(", ", AllowedQueues)}." });

        try
        {
            var summary = await _service.GetSummaryAsync(queueNumber, cancellationToken);
            return Ok(summary);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { queue = queueNumber, message = $"Queue {queueNumber} data file was not found." });
        }
    }

    [HttpGet("/delay-summary")]
    public async Task<IActionResult> GetDelaySummary(int? queue, CancellationToken cancellationToken)
    {
        var queueNumber = queue ?? 7;
        if (!AllowedQueues.Contains(queueNumber))
            return BadRequest(new { message = $"Queue {queueNumber} is not supported. Allowed: {string.Join(", ", AllowedQueues)}." });

        try
        {
            var result = await _service.GetDelaySummaryAsync(queueNumber, cancellationToken);
            return Ok(result);
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { queue = queueNumber, message = $"Queue {queueNumber} data file was not found." });
        }
    }

    

    [HttpPost("/store-recommended-actions")]
    public async Task<IActionResult> StoreRecommendedActions(int? queue, CancellationToken cancellationToken)
    {
        var queueNumber = queue ?? 7;
        if (!AllowedQueues.Contains(queueNumber))
            return BadRequest(new { message = $"Queue {queueNumber} is not supported. Allowed: {string.Join(", ", AllowedQueues)}." });

        try
        {
            var result = await _service.FetchAnalyzeAndStoreAsync(queueNumber, cancellationToken);
            return Ok(new { queue = result.Queue, pnrCount = result.PnrCount, savedActionCount = result.SavedActionCount, databaseConfigured = result.DatabaseConfigured, message = result.Message });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new { queue = queueNumber, message = $"Queue {queueNumber} data file was not found." });
        }
    }

    
}
