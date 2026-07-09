using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Models;

public class User
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(200)]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Role { get; set; } = Roles.Employee;

    [MaxLength(80)]
    public string? Department { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class Roles
{
    public const string Employee = "Employee";
    public const string Manager = "Manager";
}
