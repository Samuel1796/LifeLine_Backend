using System.ComponentModel.DataAnnotations;

namespace Nook.Api.Dtos;

public record CurrentBookingDto(
    DateTime StartsAt, DateTime EndsAt, string BookedBy, string? Department, bool IsMine);

public record NextBookingDto(DateTime StartsAt, string BookedBy);

public record SpaceListItemDto(
    int Id,
    int OfficeId,
    string OfficeName,
    string Name,
    string Type,
    int Capacity,
    string[] Amenities,
    bool IsActive,
    bool Available,
    CurrentBookingDto? CurrentBooking,
    NextBookingDto? NextBooking
);

public record SpaceBookingDto(int Id, DateTime StartsAt, DateTime EndsAt, string BookedBy, bool IsMine);

public record SpaceDetailDto(
    int Id,
    int OfficeId,
    string OfficeName,
    string Name,
    string Type,
    int Capacity,
    string[] Amenities,
    bool IsActive,
    string Date,
    List<SpaceBookingDto> Bookings
);

public record SpaceDto(
    int Id,
    int OfficeId,
    string OfficeName,
    string Name,
    string Type,
    int Capacity,
    string[] Amenities,
    bool IsActive
);

public record CreateSpaceDto(
    [Required] int? OfficeId,
    [Required, MinLength(1), MaxLength(80)] string Name,
    [Required] string Type,
    [Required, Range(1, 1000)] int? Capacity,
    string[]? Amenities
);

public record UpdateSpaceDto(
    [Required] int? OfficeId,
    [Required, MinLength(1), MaxLength(80)] string Name,
    [Required] string Type,
    [Required, Range(1, 1000)] int? Capacity,
    string[]? Amenities,
    [Required] bool? IsActive
);

public record OccupancyOfficeDto(int OfficeId, string OfficeName, int Total, int BookedNow);

public record OccupancyDto(string Date, List<OccupancyOfficeDto> Offices, int TotalBookingsToday);
