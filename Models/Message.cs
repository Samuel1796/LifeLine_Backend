using System.ComponentModel.DataAnnotations;

namespace BloodDonorFinder.Api.Models;

public class Message
{
    public int Id { get; set; }

    public int BloodRequestId { get; set; }
    public BloodRequest BloodRequest { get; set; } = null!;

    public int DonorId { get; set; }

    public int SenderId { get; set; }
    public User Sender { get; set; } = null!;

    [MaxLength(1000)]
    public string Text { get; set; } = string.Empty;

    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
