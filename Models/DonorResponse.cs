namespace BloodDonorFinder.Api.Models;

public class DonorResponse
{
    public int Id { get; set; }
    public int BloodRequestId { get; set; }
    public BloodRequest BloodRequest { get; set; } = null!;

    public int DonorId { get; set; }
    public User Donor { get; set; } = null!;

    public string Status { get; set; } = ResponseStatuses.Accepted;
    public DateTime RespondedAt { get; set; } = DateTime.UtcNow;
}

public static class ResponseStatuses
{
    public const string Accepted = "Accepted";
    public const string Declined = "Declined";
}
