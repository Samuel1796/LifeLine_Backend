namespace BloodDonorFinder.Api.Models;

public class DonorProfile
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string BloodType { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? City { get; set; }
    public bool IsAvailable { get; set; } = true;
    public bool IsAnonymous { get; set; }
    public DateTime? LastDonationDate { get; set; }
}

public static class BloodTypes
{
    public static readonly string[] All = ["O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+"];

    // Recipient blood type -> blood types that can safely donate to them
    public static readonly Dictionary<string, string[]> CompatibleDonors = new()
    {
        ["O-"] = ["O-"],
        ["O+"] = ["O-", "O+"],
        ["A-"] = ["O-", "A-"],
        ["A+"] = ["O-", "O+", "A-", "A+"],
        ["B-"] = ["O-", "B-"],
        ["B+"] = ["O-", "O+", "B-", "B+"],
        ["AB-"] = ["O-", "A-", "B-", "AB-"],
        ["AB+"] = ["O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+"],
    };

    public static bool IsValid(string bloodType) => All.Contains(bloodType);
}
