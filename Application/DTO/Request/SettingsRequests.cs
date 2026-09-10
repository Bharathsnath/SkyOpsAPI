namespace SkyOpsQueueIntelligence.Application.DTO.Request;

public sealed record UpdateRemarksRequest(
    string Remarks);

public sealed record UpdateRemarksByKeyRequest(
    string Pnr,
    string Remarks);

public class ConnectionCredentialUpdateRequest
{
    public bool IsEnabled { get; set; }
    public int ModifiedUser { get; set; }
    public List<CredentialTagUpdate> Credentials { get; set; } = new();
}

public class CredentialTagUpdate
{
    public long CredId { get; set; }
    public string TagName { get; set; } = string.Empty;
    public string TagValue { get; set; } = string.Empty;
}

public class PccCredentialStatusRequest
{
    public int RecordStatus { get; set; }
    public int ModifiedUser { get; set; }
}

public class LoggingConfigRequest
{
    public bool Enabled { get; set; }
    public int ModifiedUser { get; set; }
}

public class AppConfigurationRequest
{
    public long? Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string ConfigKey { get; set; } = string.Empty;
    public string ConfigValue { get; set; } = string.Empty;
    public string? ProviderName { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsActive { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int? ModifiedUser { get; set; }
    public DateTime? ModifiedDate { get; set; }
}

public class PccCredentialRequest
{
    public long? Cred_ID { get; set; }
    public string? SourceDb { get; set; }
    public string PCCMasterCode { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string SectorType { get; set; } = string.Empty;
    public string TagName { get; set; } = string.Empty;
    public string TagValue { get; set; } = string.Empty;
    public int RecordStatus { get; set; }
    
    public int CreatedUser { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int ModifiedUser { get; set; }
    public DateTime? ModifiedDate { get; set; }
    
    public string AirlineCurrencyCode { get; set; } = string.Empty;
}

public class UserPccMappingRequest
{
    public long? Id { get; set; }
    public int UserId { get; set; }
    public string PccCode { get; set; } = string.Empty;
    public string AccessType { get; set; } = "PCC";
    public int IsActive { get; set; }
    public int ModifiedBy { get; set; }
}

public class PccAgentEmailMasterRequest
{
    public long? Id { get; set; }
    public string PccValue { get; set; } = string.Empty;
    public string? Pcc { get; set; }
    public string Company { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string Emails { get; set; } = string.Empty;
    public int IsActive { get; set; }
    public int CreatedBy { get; set; }
    public DateTime? CreatedDate { get; set; }
    public int ModifiedBy { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
