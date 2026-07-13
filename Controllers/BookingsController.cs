using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Dtos;
using Nook.Api.Hubs;
using Nook.Api.Models;
using Nook.Api.Services;

namespace Nook.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/bookings")]
public class BookingsController(AppDbContext db, IHubContext<NotificationHub> hub) : ControllerBase
{
    private static readonly TimeSpan PastGrace = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromHours(12);

    // POST /api/bookings — { workspaceId, startsAt, endsAt, note?, replaceBookingId? } → 201, or 409 on overlap.
    [HttpPost]
    public async Task<IActionResult> Create(CreateBookingDto dto)
    {
        var startsUtc = dto.StartsAt!.Value.UtcDateTime;
        var endsUtc = dto.EndsAt!.Value.UtcDateTime;
        var userId = CurrentUserId();

        if (endsUtc <= startsUtc)
            return BadRequest(new { error = "EndsAt must be after StartsAt." });
        if (startsUtc < DateTime.UtcNow - PastGrace)
            return BadRequest(new { error = "Bookings cannot start in the past." });
        if (endsUtc - startsUtc > MaxDuration)
            return BadRequest(new { error = "Bookings cannot be longer than 12 hours." });

        var workspace = await db.Workspaces
            .Include(w => w.Office)
            .FirstOrDefaultAsync(w => w.Id == dto.WorkspaceId!.Value);
        if (workspace is null)
            return NotFound(new { error = "Workspace not found." });
        if (!workspace.IsActive)
            return BadRequest(new { error = "This space is not available for booking." });

        var overlaps = await db.Bookings.AnyAsync(b =>
            b.WorkspaceId == workspace.Id
            && b.Status == BookingStatus.Confirmed
            && b.StartsAt < endsUtc
            && b.EndsAt > startsUtc);
        if (overlaps)
            return Conflict(new { error = $"{workspace.Name} is already booked for that time." });

        // Same-user overlap guard: one person can't hold overlapping bookings on two different desks.
        var ownConflict = await db.Bookings
            .Include(b => b.Workspace)
            .Where(b => b.UserId == userId
                && b.WorkspaceId != workspace.Id
                && b.Status == BookingStatus.Confirmed
                && b.StartsAt < endsUtc
                && b.EndsAt > startsUtc)
            .FirstOrDefaultAsync();

        if (ownConflict is not null)
        {
            // If the client asked to replace a specific booking, re-verify server-side that it's
            // actually eligible (owned by this user, still Confirmed, and really overlaps) before
            // trusting it — otherwise treat it as if no override was supplied.
            Booking? replacing = null;
            if (dto.ReplaceBookingId.HasValue)
            {
                var candidate = await db.Bookings
                    .Include(b => b.Workspace)
                    .FirstOrDefaultAsync(b => b.Id == dto.ReplaceBookingId.Value);
                if (candidate is not null
                    && candidate.UserId == userId
                    && candidate.Status == BookingStatus.Confirmed
                    && candidate.StartsAt < endsUtc
                    && candidate.EndsAt > startsUtc)
                {
                    replacing = candidate;
                }
            }

            if (replacing is null)
            {
                return Conflict(new
                {
                    error = $"You already have {ownConflict.Workspace.Name} booked " +
                             $"{ownConflict.StartsAt.ToLocalTime():HH:mm}–{ownConflict.EndsAt.ToLocalTime():HH:mm}, " +
                             "which overlaps this time.",
                    code = "OwnBookingConflict",
                    conflictingBooking = new
                    {
                        id = ownConflict.Id,
                        workspaceName = ownConflict.Workspace.Name,
                        startsAt = ownConflict.StartsAt,
                        endsAt = ownConflict.EndsAt,
                    },
                });
            }

            var replaceNote = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
            var replacement = new Booking
            {
                WorkspaceId = workspace.Id,
                UserId = userId,
                StartsAt = startsUtc,
                EndsAt = endsUtc,
                Note = replaceNote,
            };

            await using var transaction = await db.Database.BeginTransactionAsync();

            replacing.Status = BookingStatus.Cancelled;
            db.Bookings.Add(replacement);
            await db.SaveChangesAsync();

            await transaction.CommitAsync();

            await hub.Clients.All.SendAsync("bookingCancelled", new
            {
                workspaceId = replacing.WorkspaceId,
                workspaceName = replacing.Workspace.Name,
                bookingId = replacing.Id,
                startsAt = replacing.StartsAt,
                endsAt = replacing.EndsAt,
            });

            await hub.Clients.All.SendAsync("spaceBooked", new
            {
                workspaceId = workspace.Id,
                workspaceName = workspace.Name,
                startsAt = replacement.StartsAt,
                endsAt = replacement.EndsAt,
            });

            return Created($"/api/bookings/{replacement.Id}", ToDto(replacement, workspace));
        }

        var note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
        var booking = new Booking
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            StartsAt = startsUtc,
            EndsAt = endsUtc,
            Note = note,
        };

        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        await hub.Clients.All.SendAsync("spaceBooked", new
        {
            workspaceId = workspace.Id,
            workspaceName = workspace.Name,
            startsAt = booking.StartsAt,
            endsAt = booking.EndsAt,
        });

        return Created($"/api/bookings/{booking.Id}", ToDto(booking, workspace));
    }

    // GET /api/bookings/mine — { upcoming, past } split by EndsAt vs now.
    [HttpGet("mine")]
    public async Task<ActionResult<MyBookingsDto>> Mine()
    {
        var userId = CurrentUserId();
        var now = DateTime.UtcNow;

        var bookings = await db.Bookings
            .Include(b => b.Workspace).ThenInclude(w => w.Office)
            .Where(b => b.UserId == userId)
            .ToListAsync();

        var upcoming = bookings
            .Where(b => b.EndsAt > now)
            .OrderBy(b => b.StartsAt)
            .Select(b => ToDto(b, b.Workspace))
            .ToList();

        var past = bookings
            .Where(b => b.EndsAt <= now)
            .OrderByDescending(b => b.StartsAt)
            .Select(b => ToDto(b, b.Workspace))
            .ToList();

        return Ok(new MyBookingsDto(upcoming, past));
    }

    // GET /api/bookings/all?date=YYYY-MM-DD — Manager only; that day's bookings with booker name.
    [HttpGet("all")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> All([FromQuery] DateOnly? date)
    {
        var day = date ?? LocalDay.Today;
        var (startUtc, endUtc) = LocalDay.ToUtcWindow(day);

        var bookings = await db.Bookings
            .Include(b => b.Workspace).ThenInclude(w => w.Office)
            .Include(b => b.User)
            .Where(b => b.StartsAt < endUtc && b.EndsAt > startUtc)
            .OrderBy(b => b.StartsAt)
            .ToListAsync();

        var result = bookings.Select(b => new ManagerBookingDto(
            b.Id,
            new BookingWorkspaceDto(b.Workspace.Id, b.Workspace.Name, b.Workspace.Type, b.Workspace.Office.Name),
            b.StartsAt, b.EndsAt, b.Status, b.Note, b.User.FullName));

        return Ok(result);
    }

    // PATCH /api/bookings/{id}/cancel — owner or Manager.
    [HttpPatch("{id:int}/cancel")]
    public async Task<IActionResult> Cancel(int id)
    {
        var booking = await db.Bookings
            .Include(b => b.Workspace).ThenInclude(w => w.Office)
            .FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
            return NotFound(new { error = "Booking not found." });

        if (booking.UserId != CurrentUserId() && !User.IsInRole(Roles.Manager))
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "You can only cancel your own bookings." });

        if (booking.Status == BookingStatus.Cancelled)
            return BadRequest(new { error = "Booking is already cancelled." });

        booking.Status = BookingStatus.Cancelled;
        await db.SaveChangesAsync();

        await hub.Clients.All.SendAsync("bookingCancelled", new
        {
            workspaceId = booking.WorkspaceId,
            workspaceName = booking.Workspace.Name,
            bookingId = booking.Id,
            startsAt = booking.StartsAt,
            endsAt = booking.EndsAt,
        });

        return Ok(ToDto(booking, booking.Workspace));
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static BookingDto ToDto(Booking b, Workspace w) => new(
        b.Id,
        new BookingWorkspaceDto(w.Id, w.Name, w.Type, w.Office.Name),
        b.StartsAt, b.EndsAt, b.Status, b.Note);
}
