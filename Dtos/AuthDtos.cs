using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Dtos;

public record RegisterDto(
    [Required, MinLength(2), MaxLength(100)] string FullName,
    [Required, EmailAddress, MaxLength(200)] string Email,
    [Required, MinLength(6), MaxLength(100)] string Password,
    [Required] string Role,
    [MaxLength(80)] string? Department
);

public record LoginDto(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record AuthResponseDto(string Token, UserDto User);

public record UserDto(int Id, string FullName, string Email, string Role, string? Department);
