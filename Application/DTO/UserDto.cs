namespace SkyOpsQueueIntelligence.Application.DTO;

public class User
{
    // Users.Id is BIGINT in the production database.
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    public bool IsActive { get; set; }
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLogin { get; set; }
    public int Role { get; set; }
    public int? UpdatedBy { get; set; }
    public DateTime? CreatedAt { get; set; }
    public long mobile { get; set; }
}

public class RoleMaster
{
    public int Id { get; set; }
    public string RoleName { get; set; } = string.Empty;
}

public class UserMarketPermission
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string PermissionType { get; set; } = string.Empty; // M, C, B
    public int ReferenceId { get; set; }
    public bool IsActive { get; set; }
}

public class UserMarketPermissionResponse
{
    public IReadOnlyList<UserMarketPermission> Markets { get; set; } = [];
    public IReadOnlyList<UserMarketPermission> Companies { get; set; } = [];
    public IReadOnlyList<UserMarketPermission> Branches { get; set; } = [];
}

public class SaveMarketPermissionRequest
{
    public string PermissionType { get; set; } = string.Empty; // M, C, B
    public int ReferenceId { get; set; }
    public bool IsActive { get; set; } = true;
    public int ModifiedBy { get; set; }
}

public class MarketMaster
{
    public int Id { get; set; }
    public string MarketCode { get; set; } = string.Empty;
    public string MarketName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

public class CompanyMaster
{
    public int Id { get; set; }
    public int MarketId { get; set; }
    public string CompanyCode { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? TransactionPrefix { get; set; }
    public bool IsActive { get; set; }
}

public class BranchMaster
{
    public int Id { get; set; }
    public int CompanyId { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
