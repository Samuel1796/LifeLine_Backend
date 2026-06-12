using System.ComponentModel.DataAnnotations;

namespace BloodDonorFinder.Api.Dtos;

public record CreateRequestDto(
    [Required] string BloodType,
    [Required, MinLength(2)] string PatientName,
    string? HospitalName,
    [Required] string Urgency,
    [Range(-90, 90)] double Latitude,
    [Range(-180, 180)] double Longitude,
    string? City,
    [Range(1, 20)] int UnitsNeeded,
    string? Notes,
    int? TargetDonorUserId
);

public record RequestSummaryDto(
    int Id,
    string BloodType,
    string PatientName,
    string? HospitalName,
    string Urgency,
    string Status,
    string? City,
    int UnitsNeeded,
    string? Notes,
    DateTime CreatedAt,
    double Latitude,
    double Longitude,
    string RequesterName,
    string? RequesterPhone,
    double? DistanceKm,
    int AcceptedCount,
    string? MyResponse,
    bool IsDirect
);

public record CreateRequestResponseDto(RequestSummaryDto Request, int NotifiedDonors);

public record RespondDto(bool Accept);

public record UpdateStatusDto([Required] string Status);

public record ResponderDto(
    int DonorId,
    string FullName,
    string? Phone,
    string BloodType,
    string Status,
    DateTime RespondedAt
);

public record RequestDetailDto(
    RequestSummaryDto Request,
    List<ResponderDto> Responders
);
