using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponse?> AuthenticateAsync(string username, string password);
    string GenerateToken(string username);
}
