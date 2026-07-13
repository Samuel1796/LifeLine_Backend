using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Dtos;

public record CreateBookingDto(
    [Required] int? WorkspaceId,
    [Required] DateTimeOffset? StartsAt,
    [Required] DateTimeOffset? EndsAt,
    [MaxLength(200)] string? Note,
    int? ReplaceBookingId
);

public record BookingWorkspaceDto(int Id, string Name, string Type, string OfficeName);

public record BookingDto(
    int Id,
    BookingWorkspaceDto Workspace,
    DateTime StartsAt,
    DateTime EndsAt,
    string Status,
    string? Note
);

public record ManagerBookingDto(
    int Id,
    BookingWorkspaceDto Workspace,
    DateTime StartsAt,
    DateTime EndsAt,
    string Status,
    string? Note,
    string BookedBy
);

public record MyBookingsDto(List<BookingDto> Upcoming, List<BookingDto> Past);
