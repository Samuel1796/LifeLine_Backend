namespace BloodDonorFinder.Api.Models;

public class BloodRequest
{
    public int Id { get; set; }
    public int RequesterId { get; set; }
    public User Requester { get; set; } = null!;

    public string BloodType { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? HospitalName { get; set; }
    public string Urgency { get; set; } = UrgencyLevels.Medium;
    public string Status { get; set; } = RequestStatuses.Open;
    public string? Notes { get; set; }

    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string? City { get; set; }

    public int UnitsNeeded { get; set; } = 1;

    // When set, this request was sent directly to one specific donor
    // instead of being broadcast to everyone matched nearby.
    public int? DirectedDonorId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<DonorResponse> Responses { get; set; } = [];
}

public static class UrgencyLevels
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Critical = "Critical";

    public static readonly string[] All = [Low, Medium, High, Critical];
}

public static class RequestStatuses
{
    public const string Open = "Open";
    public const string Fulfilled = "Fulfilled";
    public const string Cancelled = "Cancelled";
}
