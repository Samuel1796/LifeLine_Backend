using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Models;

public class Workspace
{
    public int Id { get; set; }

    public int OfficeId { get; set; }
    public Office Office { get; set; } = null!;

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(10)]
    public string Type { get; set; } = WorkspaceTypes.Desk;

    public int Capacity { get; set; } = 1;

    // Comma-separated, e.g. "Monitor,Window,Standing".
    [MaxLength(300)]
    public string Amenities { get; set; } = string.Empty;

    // Soft delete: inactive spaces are hidden from listings and cannot be booked.
    public bool IsActive { get; set; } = true;
}

public static class WorkspaceTypes
{
    public const string Desk = "Desk";
    public const string Room = "Room";
    public const string Office = "Office";

    public static readonly string[] All = [Desk, Room, Office];
}
