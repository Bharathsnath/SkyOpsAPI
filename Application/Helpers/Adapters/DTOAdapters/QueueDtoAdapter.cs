using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Helpers.Adapters.DTOAdapters;

public sealed class QueueDtoAdapter
{
    public PnrDelayAnalysisDto? ToDelayAnalysis(PnrDelayAnalysisDto? dto) => dto;
}
