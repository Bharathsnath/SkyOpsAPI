using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyOpsQueueIntelligence.Application.DTO;
using SkyOpsQueueIntelligence.Application.Interfaces;
using SkyOpsQueueIntelligence.Infrastructure.Interfaces;

namespace SkyOpsQueueIntelligence.Controllers;

[Authorize]
[ApiController]
[Route("api/User")]

public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IUserDirectoryCache _userDirectoryCache;

    public UserController(IUserService userService, IUserDirectoryCache userDirectoryCache)
    {
        _userService = userService;
        _userDirectoryCache = userDirectoryCache;
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers(CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirst("isAdmin")?.Value, out var currentRole))
            return Forbid();

        // Role 1 can see every user. Role 2 can see operators only (Role = 2).
        var visibleRole = currentRole == 1 ? (int?)null : currentRole == 2 ? 3 : -1;
        return Ok(await _userService.GetAllAsync(visibleRole, ct));
    }

    [HttpGet("{username}")]
    public async Task<IActionResult> GetUser(string username, CancellationToken ct)
    {
        var user = await _userService.GetByUsernameAsync(username, ct);
        if (user == null) return NotFound(new { message = "User not found" });
        return Ok(user);
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return BadRequest(new { message = "Username and password are required" });

        var existing = await _userService.GetByUsernameAsync(request.Username, ct);
        if (existing != null)
            return Conflict(new { message = "Username already exists" });

        var newId = await _userService.CreateUserAsync(request, ct);
        return CreatedAtAction(nameof(GetUser), new { username = request.Username }, new { id = newId, username = request.Username });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> UpdateUser(long id, [FromBody] UpdateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Username))
            return BadRequest(new { message = "Username is required" });

        var updated = await _userService.UpdateUserAsync(id, request, ct);
        if (!updated) return NotFound(new { message = "User not found" });

        return NoContent();
    }

    [HttpGet("directory")]
    public IActionResult GetDirectory()
    {
        var users = _userDirectoryCache.Users.Values
            .Select(user => new
            {
                user.Id,
                user.Username,
                user.Email,
                user.Mobile
            });

        return Ok(users);
    }

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles(CancellationToken ct)
    {
        var roles = await _userService.GetRolesAsync(ct);
        return Ok(roles);
    }

    [HttpGet("{id:long}/market-permissions")]
    public async Task<IActionResult> GetMarketPermissions(long id, CancellationToken ct)
    {
        var permissions = await _userService.GetMarketPermissionsAsync(id, ct);
        var response = new UserMarketPermissionResponse
        {
            Markets = permissions.Where(p => p.PermissionType == "M").ToList(),
            Companies = permissions.Where(p => p.PermissionType == "C").ToList(),
            Branches = permissions.Where(p => p.PermissionType == "B").ToList()
        };
        return Ok(response);
    }

    [HttpPost("{id:long}/market-permissions")]
    public async Task<IActionResult> SaveMarketPermissions(long id, [FromBody] List<SaveMarketPermissionRequest> permissions, CancellationToken ct)
    {
        await _userService.SaveMarketPermissionsAsync(id, permissions, ct);
        return NoContent();
    }
}
