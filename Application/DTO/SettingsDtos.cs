using System.Text.Json.Serialization;

namespace SkyOpsQueueIntelligence.Application.DTO;

public sealed class ConnectionCredential
{
    public long Cred_ID { get; init; }
    public string PCCMasterCode { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ServiceType { get; init; } = string.Empty;
    public string SectorType { get; init; } = string.Empty;
    public string TagName { get; init; } = string.Empty;
    public string TagValue { get; init; } = string.Empty;
    public int RecordStatus { get; init; }
   
    public int CreatedUser { get; init; }
    public DateTime? CreatedDate { get; init; }
    public int ModifiedUser { get; init; }
    public DateTime? ModifiedDate { get; init; }
    
    public string AirlineCurrencyCode { get; init; } = string.Empty;
}

public sealed class PccCredential
{
    public long Cred_ID { get; init; }
    public string SourceDb { get; init; } = string.Empty;
    public string PCCMasterCode { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string ServiceType { get; init; } = string.Empty;
    public string SectorType { get; init; } = string.Empty;
    public string TagName { get; init; } = string.Empty;
    public string TagValue { get; init; } = string.Empty;
    public int RecordStatus { get; init; }
    public string SystemStatus { get; init; } = string.Empty;
    public int CreatedUser { get; init; }
    public DateTime? CreatedDate { get; init; }
    public int ModifiedUser { get; init; }
    public DateTime? ModifiedDate { get; init; }
    public int LocationId { get; init; }
    public int PCCRegionID { get; init; }
    public int ProviderId { get; init; }
    public string AirlineCurrencyCode { get; init; } = string.Empty;
}

public sealed class PccListEntry
{
    public string Provider { get; init; } = string.Empty;
    public string TagValue { get; init; } = string.Empty;
}

public sealed class PccGroupEntry
{
    public string PccCode { get; init; } = string.Empty;
    public string Username { get; init; } = string.Empty;
    public string Provider { get; init; } = string.Empty;
    public string SourceOffice { get; init; } = string.Empty;
    public string Company { get; init; } = string.Empty;
    public string Branch { get; init; } = string.Empty;
    public string Market { get; init; } = string.Empty;
}

public sealed class PccAgentEmailMaster
{
    public long Id { get; init; }
    [JsonPropertyName("pccvalue")]
    public string PccValue { get; init; } = string.Empty;
    [JsonPropertyName("PCC")]
    public string? Pcc { get; init; }
    public string Emails { get; init; } = string.Empty;
    public int IsActive { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
    public DateTime? CreatedDate { get; init; }
    public string ModifiedBy { get; init; } = string.Empty;
    public DateTime? ModifiedDate { get; init; }

    [JsonIgnore]
    public string PCCCode => !string.IsNullOrWhiteSpace(PccValue) ? PccValue : (Pcc ?? string.Empty);
}

public class UserPccMapping
{
    public long Id { get; set; }
    public int UserId { get; set; }
    public string PccCode { get; set; } = string.Empty;
    public string AccessType { get; set; } = "PCC";
    public int IsActive { get; set; }
    public int CreatedBy { get; set; } 
    public DateTime? CreatedDate { get; set; }
    public int ModifiedBy { get; set; } 
    public DateTime? ModifiedDate { get; set; }
}

public class AppConfiguration
{
    public long Id { get; set; }
    public required string Category { get; set; }
    public required string ConfigKey { get; set; }
    public required string ConfigValue { get; set; }
    public string? ProviderName { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedDate { get; set; }
    public int? ModifiedUser { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
