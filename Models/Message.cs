namespace BloodDonorFinder.Api.Models;

// A chat message inside the conversation that opens between a requester and a
// donor once that donor has ACCEPTED the request. The conversation is keyed by
// (BloodRequestId, DonorId) so one request can hold a separate thread per donor.
public class Message
{
    public int Id { get; set; }

    public int BloodRequestId { get; set; }
    public BloodRequest BloodRequest { get; set; } = null!;

    // The donor side of this conversation (the other side is always the requester).
    public int DonorId { get; set; }

    public int SenderId { get; set; }
    public User Sender { get; set; } = null!;

    public string Text { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
