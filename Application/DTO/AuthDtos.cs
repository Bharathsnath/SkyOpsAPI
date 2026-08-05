namespace SkyOpsQueueIntelligence.Application.DTO;

public class AuthRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}

public class AuthResponse
{
    public required string Token { get; set; }
    public required string Username { get; set; }
    public DateTime ExpiresAt { get; set; }
    public long UserId { get; set; }
    public int IsAdmin { get; set; }
    public long Mobile { get; set; }
}

public class CreateUserRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;
    public int Role { get; set; } = 0;
    public int UpdatedBy { get; set; }
    public long Mobile { get; set; }
}

public class UpdateUserRequest
{
    public required string Username { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
    public int Role { get; set; }
    public int UpdatedBy { get; set; }
}
