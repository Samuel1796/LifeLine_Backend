using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Models;

public class Booking
{
    public int Id { get; set; }

    public int WorkspaceId { get; set; }
    public Workspace Workspace { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    // Stored in UTC. The booked window is [StartsAt, EndsAt).
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }

    [MaxLength(10)]
    public string Status { get; set; } = BookingStatus.Confirmed;

    [MaxLength(200)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public static class BookingStatus
{
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
}
