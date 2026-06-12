using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using BloodDonorFinder.Api.Data;
using BloodDonorFinder.Api.Dtos;
using BloodDonorFinder.Api.Models;
using BloodDonorFinder.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace BloodDonorFinder.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AppDbContext db, TokenService tokenService, IConfiguration config) : ControllerBase
{
    private readonly PasswordHasher<User> _hasher = new();

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        if (dto.Role != Roles.Donor && dto.Role != Roles.Requester)
            return BadRequest(new { message = "Role must be 'Donor' or 'Requester'." });

        var email = dto.Email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "An account with this email already exists." });

        var user = new User
        {
            FullName = dto.FullName.Trim(),
            Email = email,
            Role = dto.Role,
            Phone = dto.Phone,
        };
        user.PasswordHash = _hasher.HashPassword(user, dto.Password);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return Ok(new AuthResponseDto(tokenService.CreateToken(user), ToDto(user)));
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var email = dto.Email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user is null ||
            _hasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password) == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(new AuthResponseDto(tokenService.CreateToken(user), ToDto(user)));
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var user = await db.Users.FindAsync(userId);
        return user is null ? NotFound() : Ok(ToDto(user));
    }

    // Signs a short-lived identity token for the Chatbase widget so the chatbot
    // knows which logged-in user it is talking to. Returns null when no secret
    // is configured — the widget then runs unidentified, which is fine.
    [Authorize]
    [HttpGet("chatbase-token")]
    public ActionResult ChatbaseToken()
    {
        var secret = config["CHATBASE_IDENTITY_SECRET"];
        if (string.IsNullOrWhiteSpace(secret) || secret.StartsWith("paste-"))
            return Ok(new { token = (string?)null });

        var claims = new List<Claim>
        {
            new("user_id", User.FindFirstValue(ClaimTypes.NameIdentifier)!),
            new("email", User.FindFirstValue(ClaimTypes.Email) ?? string.Empty),
            new("name", User.FindFirstValue(ClaimTypes.Name) ?? string.Empty),
            new("role", User.FindFirstValue(ClaimTypes.Role) ?? string.Empty),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token) });
    }

    private static UserDto ToDto(User u) => new(u.Id, u.FullName, u.Email, u.Role, u.Phone);
}
