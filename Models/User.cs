using System.ComponentModel.DataAnnotations;

namespace BloodDonorFinder.Api.Models;

public class User
{
    public int Id { get; set; }

    [MaxLength(100)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(200)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(60)]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Role { get; set; } = Roles.Donor;

    [MaxLength(30)]
    public string? Phone { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DonorProfile? DonorProfile { get; set; }
}

public static class Roles
{
    public const string Donor = "Donor";
    public const string Requester = "Requester";
}
