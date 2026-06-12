using System.Security.Claims;
using BloodDonorFinder.Api.Data;
using BloodDonorFinder.Api.Dtos;
using BloodDonorFinder.Api.Hubs;
using BloodDonorFinder.Api.Models;
using BloodDonorFinder.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BloodDonorFinder.Api.Controllers;

[ApiController]
[Route("api/requests")]
[Authorize]
public class RequestsController(AppDbContext db, IHubContext<NotificationHub> hub) : ControllerBase
{
    private const double MatchRadiusKm = 50;

    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpPost]
    [Authorize(Roles = Roles.Requester)]
    public async Task<ActionResult<CreateRequestResponseDto>> Create(CreateRequestDto dto)
    {
        if (!BloodTypes.IsValid(dto.BloodType))
            return BadRequest(new { message = "Invalid blood type." });
        if (!UrgencyLevels.All.Contains(dto.Urgency))
            return BadRequest(new { message = "Invalid urgency level." });

        if (dto.TargetDonorUserId is int targetId &&
            !await db.DonorProfiles.AnyAsync(p => p.UserId == targetId))
        {
            return BadRequest(new { message = "That donor no longer exists." });
        }

        var request = new BloodRequest
        {
            RequesterId = CurrentUserId,
            BloodType = dto.BloodType,
            PatientName = dto.PatientName.Trim(),
            HospitalName = dto.HospitalName,
            Urgency = dto.Urgency,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,
            City = dto.City,
            UnitsNeeded = dto.UnitsNeeded,
            Notes = dto.Notes,
            DirectedDonorId = dto.TargetDonorUserId,
        };

        db.BloodRequests.Add(request);
        await db.SaveChangesAsync();
        await db.Entry(request).Reference(r => r.Requester).LoadAsync();

        // A directed request goes to exactly one donor, regardless of distance.
        // A broadcast request reaches compatible, available donors within the radius.
        List<int> matchedDonorIds;
        if (dto.TargetDonorUserId is int donorUserId)
        {
            matchedDonorIds = [donorUserId];
        }
        else
        {
            var compatibleTypes = BloodTypes.CompatibleDonors[dto.BloodType];
            var candidates = await db.DonorProfiles
                .Where(p => p.IsAvailable && compatibleTypes.Contains(p.BloodType))
                .ToListAsync();

            matchedDonorIds = candidates
                .Where(p => GeoService.DistanceKm(p.Latitude, p.Longitude, dto.Latitude, dto.Longitude) <= MatchRadiusKm)
                .Select(p => p.UserId)
                .ToList();
        }

        var payload = ToSummary(request, null, 0, null);
        foreach (var donorId in matchedDonorIds)
        {
            await hub.Clients.Group($"user-{donorId}").SendAsync("NewRequest", payload);
        }

        return Ok(new CreateRequestResponseDto(payload, matchedDonorIds.Count));
    }

    [HttpGet("mine")]
    [Authorize(Roles = Roles.Requester)]
    public async Task<ActionResult<List<RequestSummaryDto>>> Mine()
    {
        var requests = await db.BloodRequests
            .Include(r => r.Requester)
            .Include(r => r.Responses)
            .Where(r => r.RequesterId == CurrentUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(requests
            .Select(r => ToSummary(r, null, r.Responses.Count(x => x.Status == ResponseStatuses.Accepted), null))
            .ToList());
    }

    [HttpGet("matched")]
    [Authorize(Roles = Roles.Donor)]
    public async Task<ActionResult<List<RequestSummaryDto>>> Matched()
    {
        var profile = await db.DonorProfiles.FirstOrDefaultAsync(p => p.UserId == CurrentUserId);
        if (profile is null)
            return Ok(new List<RequestSummaryDto>());

        // Requests this donor's blood type can serve
        var servableRequestTypes = BloodTypes.CompatibleDonors
            .Where(kv => kv.Value.Contains(profile.BloodType))
            .Select(kv => kv.Key)
            .ToArray();

        // Broadcast requests must be compatible; requests directed at this donor
        // always show, no matter the distance or blood type.
        var openRequests = await db.BloodRequests
            .Include(r => r.Requester)
            .Include(r => r.Responses)
            .Where(r => r.Status == RequestStatuses.Open &&
                (r.DirectedDonorId == CurrentUserId ||
                 (r.DirectedDonorId == null && servableRequestTypes.Contains(r.BloodType))))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        var result = openRequests
            .Select(r => new
            {
                Request = r,
                DistanceKm = GeoService.DistanceKm(profile.Latitude, profile.Longitude, r.Latitude, r.Longitude),
            })
            .Where(x => x.Request.DirectedDonorId == CurrentUserId || x.DistanceKm <= MatchRadiusKm)
            .OrderBy(x => x.DistanceKm)
            .Select(x => ToSummary(
                x.Request,
                Math.Round(x.DistanceKm, 1),
                x.Request.Responses.Count(resp => resp.Status == ResponseStatuses.Accepted),
                x.Request.Responses.FirstOrDefault(resp => resp.DonorId == CurrentUserId)?.Status))
            .ToList();

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RequestDetailDto>> Detail(int id)
    {
        var request = await db.BloodRequests
            .Include(r => r.Requester)
            .Include(r => r.Responses).ThenInclude(resp => resp.Donor).ThenInclude(d => d.DonorProfile)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request is null) return NotFound();

        var isOwner = request.RequesterId == CurrentUserId;
        var myResponse = request.Responses.FirstOrDefault(r => r.DonorId == CurrentUserId)?.Status;

        // Only the requester sees the full responder list with contact details.
        var responders = isOwner
            ? request.Responses
                .OrderByDescending(r => r.RespondedAt)
                .Select(r => new ResponderDto(
                    r.DonorId,
                    r.Donor.FullName,
                    r.Donor.Phone,
                    r.Donor.DonorProfile?.BloodType ?? "?",
                    r.Status,
                    r.RespondedAt))
                .ToList()
            : [];

        var summary = ToSummary(request, null,
            request.Responses.Count(r => r.Status == ResponseStatuses.Accepted), myResponse);

        return Ok(new RequestDetailDto(summary, responders));
    }

    [HttpPost("{id:int}/respond")]
    [Authorize(Roles = Roles.Donor)]
    public async Task<IActionResult> Respond(int id, RespondDto dto)
    {
        var request = await db.BloodRequests
            .Include(r => r.Responses)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request is null) return NotFound();
        if (request.Status != RequestStatuses.Open)
            return BadRequest(new { message = "This request is no longer open." });

        var donor = await db.Users.Include(u => u.DonorProfile).FirstAsync(u => u.Id == CurrentUserId);
        var newStatus = dto.Accept ? ResponseStatuses.Accepted : ResponseStatuses.Declined;

        var existing = request.Responses.FirstOrDefault(r => r.DonorId == CurrentUserId);
        if (existing is null)
        {
            db.DonorResponses.Add(new DonorResponse
            {
                BloodRequestId = id,
                DonorId = CurrentUserId,
                Status = newStatus,
            });
        }
        else
        {
            existing.Status = newStatus;
            existing.RespondedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        // Let the requester know in real time.
        await hub.Clients.Group($"user-{request.RequesterId}").SendAsync("DonorResponded", new
        {
            RequestId = id,
            DonorName = donor.FullName,
            BloodType = donor.DonorProfile?.BloodType,
            Status = newStatus,
        });

        return Ok(new { status = newStatus });
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = Roles.Requester)]
    public async Task<IActionResult> UpdateStatus(int id, UpdateStatusDto dto)
    {
        if (dto.Status != RequestStatuses.Fulfilled && dto.Status != RequestStatuses.Cancelled)
            return BadRequest(new { message = "Status must be 'Fulfilled' or 'Cancelled'." });

        var request = await db.BloodRequests
            .Include(r => r.Responses)
            .FirstOrDefaultAsync(r => r.Id == id && r.RequesterId == CurrentUserId);

        if (request is null) return NotFound();

        request.Status = dto.Status;
        await db.SaveChangesAsync();

        // Tell donors who accepted that the request was resolved.
        foreach (var response in request.Responses.Where(r => r.Status == ResponseStatuses.Accepted))
        {
            await hub.Clients.Group($"user-{response.DonorId}").SendAsync("RequestResolved", new
            {
                RequestId = id,
                Status = dto.Status,
            });
        }

        return Ok(new { request.Status });
    }

    // ---- Messaging: opens once a donor has ACCEPTED a request. One thread per (request, donor). ----

    [HttpGet("{id:int}/messages")]
    public async Task<ActionResult<List<MessageDto>>> GetMessages(int id, [FromQuery] int? donorId)
    {
        var (request, threadDonorId, error) = await ResolveThread(id, donorId);
        if (error is not null) return error;

        var messages = await db.Messages
            .Include(m => m.Sender)
            .Where(m => m.BloodRequestId == id && m.DonorId == threadDonorId)
            .OrderBy(m => m.SentAt)
            .ToListAsync();

        return Ok(messages.Select(ToMessageDto).ToList());
    }

    [HttpPost("{id:int}/messages")]
    public async Task<ActionResult<MessageDto>> SendMessage(int id, SendMessageDto dto)
    {
        var (request, threadDonorId, error) = await ResolveThread(id, dto.DonorId);
        if (error is not null) return error;

        var message = new Message
        {
            BloodRequestId = id,
            DonorId = threadDonorId,
            SenderId = CurrentUserId,
            Text = dto.Text.Trim(),
        };
        db.Messages.Add(message);
        await db.SaveChangesAsync();
        await db.Entry(message).Reference(m => m.Sender).LoadAsync();

        var payload = ToMessageDto(message);

        // Push to the other side of the conversation in real time.
        var recipientId = CurrentUserId == threadDonorId ? request!.RequesterId : threadDonorId;
        await hub.Clients.Group($"user-{recipientId}").SendAsync("NewMessage", new
        {
            RequestId = id,
            DonorId = threadDonorId,
            Message = payload,
        });

        return Ok(payload);
    }

    // Validates access to a conversation: the caller must be the request owner
    // (who picks the donor via donorId) or a donor who has accepted the request.
    private async Task<(BloodRequest? Request, int DonorId, ActionResult? Error)> ResolveThread(int requestId, int? donorId)
    {
        var request = await db.BloodRequests.FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null) return (null, 0, NotFound());

        int threadDonorId;
        if (User.IsInRole(Roles.Donor))
        {
            threadDonorId = CurrentUserId;
        }
        else if (request.RequesterId == CurrentUserId && donorId is int d)
        {
            threadDonorId = d;
        }
        else
        {
            return (null, 0, Forbid());
        }

        var hasAccepted = await db.DonorResponses.AnyAsync(r =>
            r.BloodRequestId == requestId &&
            r.DonorId == threadDonorId &&
            r.Status == ResponseStatuses.Accepted);

        if (!hasAccepted)
            return (null, 0, BadRequest(new { message = "Messaging opens once the donor accepts the request." }));

        return (request, threadDonorId, null);
    }

    private static MessageDto ToMessageDto(Message m) =>
        new(m.Id, m.SenderId, m.Sender.FullName, m.Text, m.SentAt);

    private static RequestSummaryDto ToSummary(BloodRequest r, double? distanceKm, int acceptedCount, string? myResponse) => new(
        r.Id, r.BloodType, r.PatientName, r.HospitalName, r.Urgency, r.Status,
        r.City, r.UnitsNeeded, r.Notes, r.CreatedAt, r.Latitude, r.Longitude,
        r.Requester.FullName, r.Requester.Phone, distanceKm, acceptedCount, myResponse,
        r.DirectedDonorId is not null);
}
