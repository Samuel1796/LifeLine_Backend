using System.ComponentModel.DataAnnotations;

namespace BloodDonorFinder.Api.Dtos;

public record RegisterDto(
    [Required, MinLength(2)] string FullName,
    [Required, EmailAddress] string Email,
    [Required, MinLength(6)] string Password,
    [Required] string Role,
    string? Phone
);

public record LoginDto(
    [Required, EmailAddress] string Email,
    [Required] string Password
);

public record AuthResponseDto(string Token, UserDto User);

public record UserDto(int Id, string FullName, string Email, string Role, string? Phone);
