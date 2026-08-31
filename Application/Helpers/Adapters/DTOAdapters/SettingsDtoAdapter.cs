using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.DTO.Request;

namespace SkyOpsQueueIntelligence.Application.Helpers.Adapters.DTOAdapters;

public sealed class SettingsDtoAdapter
{
    public AppConfiguration ToAppConfiguration(AppConfigurationRequest request, long? id = null) => new()
    {
        Id = id ?? request.Id ?? 0,
        Category = request.Category,
        ConfigKey = request.ConfigKey,
        ConfigValue = request.ConfigValue,
        ProviderName = request.ProviderName,
        IsEnabled = request.IsEnabled,
        IsActive = request.IsActive,
        CreatedDate = request.CreatedDate ?? DateTime.UtcNow,
        ModifiedUser = request.ModifiedUser,
        ModifiedDate = request.ModifiedDate
    };

    public PccCredential ToPccCredential(PccCredentialRequest request, long? credId = null) => new()
    {
        Cred_ID = credId ?? request.Cred_ID ?? 0,
        SourceDb = request.SourceDb ?? string.Empty,
        PCCMasterCode = request.PCCMasterCode,
        Provider = request.Provider,
        ServiceType = request.ServiceType,
        SectorType = request.SectorType,
        TagName = request.TagName,
        TagValue = request.TagValue,
        RecordStatus = request.RecordStatus,
    
        CreatedUser = request.CreatedUser,
        CreatedDate = request.CreatedDate,
        ModifiedUser = request.ModifiedUser,
        ModifiedDate = request.ModifiedDate,
       
        AirlineCurrencyCode = request.AirlineCurrencyCode
    };

    public PccAgentEmailMaster ToPccAgentEmailMaster(PccAgentEmailMasterRequest request, long? id = null) => new()
    {
        Id = id ?? request.Id ?? 0,
        PccValue = request.PccValue,
        Pcc = request.Pcc,
        Company = request.Company,
        Market = request.Market,
        Emails = request.Emails,
        IsActive = request.IsActive,
        CreatedBy = request.CreatedBy,
        CreatedDate = request.CreatedDate,
        ModifiedBy = request.ModifiedBy,
        ModifiedDate = request.ModifiedDate
    };
}
