using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Application.DTO;

namespace SkyOpsQueueIntelligence.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AuthRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Username and password are required" });

        var response = await _authService.AuthenticateAsync(request.Username, request.Password);
        if (response == null)
            return Unauthorized(new { message = "Invalid credentials" });

        return Ok(response);
    }

    [HttpPost("refresh")]
    public IActionResult Refresh()
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Unauthorized(new { message = "User not authenticated" });

        var token = _authService.GenerateToken(username);
        return Ok(new { token, username, expiresAt = DateTime.UtcNow.AddMinutes(60) });
    }

    [HttpGet("test-error-log")]
    public IActionResult TestErrorLog()
    {
        throw new InvalidOperationException("Test error for bd_log_errorlog persistence.");
    }
}
