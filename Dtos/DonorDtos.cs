using System.ComponentModel.DataAnnotations;

namespace BloodDonorFinder.Api.Dtos;

public record DonorProfileDto(
    [Required] string BloodType,
    [Range(-90, 90)] double Latitude,
    [Range(-180, 180)] double Longitude,
    string? City,
    bool IsAvailable,
    bool IsAnonymous,
    DateTime? LastDonationDate
);

public record DonorProfileResponseDto(
    int UserId,
    string FullName,
    string BloodType,
    double Latitude,
    double Longitude,
    string? City,
    bool IsAvailable,
    bool IsAnonymous,
    DateTime? LastDonationDate
);

public record AvailabilityDto(bool IsAvailable);

// What requesters see on the donor map. Anonymous donors get a masked name.
public record MapDonorDto(
    int Id,
    string DisplayName,
    string? Phone,
    string BloodType,
    double Latitude,
    double Longitude,
    string? City,
    bool IsAvailable,
    bool IsAnonymous
);
