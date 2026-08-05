using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Application.Services;

public class AuthService : IAuthService
{
    private readonly IConfiguration _configuration;
    private readonly string _secretKey;
    private readonly int _tokenExpiryMinutes;
    private readonly bool _useDatabase;
    private readonly IUserRepository? _userRepository;

    public AuthService(IConfiguration configuration, IUserRepository? userRepository = null)
    {
        _configuration = configuration;
        _secretKey = configuration["Jwt:SecretKey"] ?? throw new InvalidOperationException("JWT:SecretKey not configured");
        _tokenExpiryMinutes = int.TryParse(configuration["Jwt:ExpiryMinutes"], out var minutes) ? minutes : 60;
        _useDatabase = bool.TryParse(configuration["Auth:UseDatabase"], out var useDb) && useDb;
        _userRepository = userRepository;
    }

    public async Task<AuthResponse?> AuthenticateAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return null;

        if (_useDatabase)
        {
            if (_userRepository == null)
                throw new InvalidOperationException("IUserRepository not registered but Auth is configured to use database.");

            var user = await _userRepository.GetByUsernameAsync(username);
            if (user == null || !user.IsActive) return null;

            var valid = !string.IsNullOrEmpty(user.PasswordHash) && VerifySha256Hash(password, user.PasswordHash);
            if (!valid)
            {
                await _userRepository.IncrementFailedAttemptsAsync(username);
                return null;
            }

            await _userRepository.UpdateLastLoginAsync(username);
            await _userRepository.ResetFailedAttemptsAsync(username);

            var token = GenerateToken(user);
            return new AuthResponse { Token = token, Username = user.Username, ExpiresAt = DateTime.UtcNow.AddMinutes(_tokenExpiryMinutes), UserId = user.Id, IsAdmin = user.Role , Mobile = (long)user.mobile};
        }

        if (!ValidateCredentials(username, password)) return null;

        var fallbackToken = GenerateToken(username);
        return new AuthResponse { Token = fallbackToken, Username = username, ExpiresAt = DateTime.UtcNow.AddMinutes(_tokenExpiryMinutes), UserId = 0, IsAdmin = 0 };
    }

    public string GenerateToken(string username)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.NameIdentifier, username),
            new Claim("iss", "SkyOpsQueueIntelligence"),
            new Claim("aud", "SkyOpsQueueIntelligence")
        };
        var token = new JwtSecurityToken("SkyOpsQueueIntelligence", "SkyOpsQueueIntelligence", claims,
            expires: DateTime.UtcNow.AddMinutes(_tokenExpiryMinutes), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString(), ClaimValueTypes.Integer),
            new Claim("isAdmin", user.Role.ToString(), ClaimValueTypes.Integer),
            new Claim("iss", "SkyOpsQueueIntelligence"),
            new Claim("aud", "SkyOpsQueueIntelligence")
        };
        var token = new JwtSecurityToken("SkyOpsQueueIntelligence", "SkyOpsQueueIntelligence", claims,
            expires: DateTime.UtcNow.AddMinutes(_tokenExpiryMinutes), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static bool ValidateCredentials(string username, string password)
        => !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);

    private static bool VerifySha256Hash(string input, string storedHash)
    {
        try
        {
            byte[] storedBytes = IsHexString(storedHash) ? HexStringToBytes(storedHash) : Convert.FromBase64String(storedHash);
            using var sha = SHA256.Create();
            var computed = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return CryptographicOperations.FixedTimeEquals(computed, storedBytes);
        }
        catch { return false; }
    }

    private static bool IsHexString(string s)
    {
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        return true;
    }

    private static byte[] HexStringToBytes(string hex)
    {
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < hex.Length; i += 2)
            bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
        return bytes;
    }
}
