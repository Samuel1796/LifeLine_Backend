namespace BloodDonorFinder.Api.Models;

public class User
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = Roles.Donor;
    public string? Phone { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DonorProfile? DonorProfile { get; set; }
}

public static class Roles
{
    public const string Donor = "Donor";
    public const string Requester = "Requester";
}
