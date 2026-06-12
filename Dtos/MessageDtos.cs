using System.ComponentModel.DataAnnotations;

namespace BloodDonorFinder.Api.Dtos;

public record SendMessageDto(
    // Required when the requester sends; ignored for donors (their own id is used).
    int? DonorId,
    [Required, MinLength(1), MaxLength(1000)] string Text
);

public record MessageDto(
    int Id,
    int SenderId,
    string SenderName,
    string Text,
    DateTime SentAt
);
