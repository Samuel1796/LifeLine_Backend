using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;
using Nook.Api.Dtos;
using Nook.Api.Models;
using Nook.Api.Services;

namespace Nook.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/spaces")]
public class SpacesController(AppDbContext db) : ControllerBase
{
    // GET /api/spaces?from=ISO&to=ISO&type=&officeId=&includeInactive=
    // Availability is computed for the [from, to) window; default now -> now+1h.
    // includeInactive=true is honoured for Managers only (silently ignored otherwise).
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? type,
        [FromQuery] int? officeId,
        [FromQuery] bool includeInactive = false)
    {
        var fromUtc = (from ?? DateTimeOffset.UtcNow).UtcDateTime;
        var toUtc = (to ?? (from ?? DateTimeOffset.UtcNow).AddHours(1)).UtcDateTime;
        if (toUtc <= fromUtc)
            return BadRequest(new { error = "'to' must be after 'from'." });

        var query = db.Workspaces.Include(w => w.Office).AsQueryable();
        if (!(includeInactive && User.IsInRole(Roles.Manager)))
            query = query.Where(w => w.IsActive);
        if (!string.IsNullOrWhiteSpace(type))
            query = query.Where(w => w.Type == type);
        if (officeId is int oid)
            query = query.Where(w => w.OfficeId == oid);

        var spaces = await query
            .OrderBy(w => w.Office.Ordinal)
            .ThenBy(w => w.Type)
            .ThenBy(w => w.Name)
            .ToListAsync();

        var ids = spaces.Select(s => s.Id).ToList();

        // Current booking: the one overlapping the requested [from, to) window.
        var overlapping = await db.Bookings
            .Include(b => b.User)
            .Where(b => ids.Contains(b.WorkspaceId)
                        && b.Status == BookingStatus.Confirmed
                        && b.StartsAt < toUtc
                        && b.EndsAt > fromUtc)
            .OrderBy(b => b.StartsAt)
            .ToListAsync();

        var bookingByWorkspace = overlapping
            .GroupBy(b => b.WorkspaceId)
            .ToDictionary(g => g.Key, g => g.First());

        // Next booking: the next confirmed booking starting at/after the window
        // end, on the same local day as the window end — powers "Free until HH:MM".
        var localDay = DateOnly.FromDateTime(toUtc.ToLocalTime());
        var (_, localDayEndUtc) = LocalDay.ToUtcWindow(localDay);

        var upcoming = await db.Bookings
            .Include(b => b.User)
            .Where(b => ids.Contains(b.WorkspaceId)
                        && b.Status == BookingStatus.Confirmed
                        && b.StartsAt >= toUtc
                        && b.StartsAt < localDayEndUtc)
            .OrderBy(b => b.StartsAt)
            .ToListAsync();

        var nextBookingByWorkspace = upcoming
            .GroupBy(b => b.WorkspaceId)
            .ToDictionary(g => g.Key, g => g.First());

        var userId = CurrentUserId();
        var result = spaces.Select(w =>
        {
            bookingByWorkspace.TryGetValue(w.Id, out var booking);
            nextBookingByWorkspace.TryGetValue(w.Id, out var nextBooking);
            return new SpaceListItemDto(
                w.Id, w.OfficeId, w.Office.Name, w.Name, w.Type, w.Capacity,
                SplitAmenities(w.Amenities),
                IsActive: w.IsActive,
                Available: booking is null,
                CurrentBooking: booking is null
                    ? null
                    : new CurrentBookingDto(
                        booking.StartsAt, booking.EndsAt, booking.User.FullName,
                        booking.User.Department, booking.UserId == userId),
                NextBooking: nextBooking is null
                    ? null
                    : new NextBookingDto(nextBooking.StartsAt, nextBooking.User.FullName));
        });

        return Ok(result);
    }

    // GET /api/spaces/{id}?date=YYYY-MM-DD — detail plus that day's confirmed bookings.
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id, [FromQuery] DateOnly? date)
    {
        var workspace = await db.Workspaces.Include(w => w.Office).FirstOrDefaultAsync(w => w.Id == id);
        if (workspace is null)
            return NotFound(new { error = "Space not found." });

        var day = date ?? LocalDay.Today;
        var (startUtc, endUtc) = LocalDay.ToUtcWindow(day);
        var userId = CurrentUserId();

        var bookings = await db.Bookings
            .Include(b => b.User)
            .Where(b => b.WorkspaceId == id
                        && b.Status == BookingStatus.Confirmed
                        && b.StartsAt < endUtc
                        && b.EndsAt > startUtc)
            .OrderBy(b => b.StartsAt)
            .Select(b => new SpaceBookingDto(b.Id, b.StartsAt, b.EndsAt, b.User.FullName, b.UserId == userId))
            .ToListAsync();

        return Ok(new SpaceDetailDto(
            workspace.Id, workspace.OfficeId, workspace.Office.Name, workspace.Name, workspace.Type,
            workspace.Capacity, SplitAmenities(workspace.Amenities), workspace.IsActive,
            day.ToString("yyyy-MM-dd"), bookings));
    }

    // POST /api/spaces — Manager only.
    [HttpPost]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Create(CreateSpaceDto dto)
    {
        if (!WorkspaceTypes.All.Contains(dto.Type))
            return BadRequest(new { error = "Type must be 'Desk', 'Room' or 'Office'." });

        var office = await db.Offices.FindAsync(dto.OfficeId!.Value);
        if (office is null)
            return BadRequest(new { error = "Office not found." });

        var workspace = new Workspace
        {
            OfficeId = office.Id,
            Name = dto.Name.Trim(),
            Type = dto.Type,
            Capacity = dto.Capacity!.Value,
            Amenities = JoinAmenities(dto.Amenities),
        };

        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync();

        return Created($"/api/spaces/{workspace.Id}", ToSpaceDto(workspace, office.Name));
    }

    // PUT /api/spaces/{id} — Manager only; same fields plus isActive.
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Update(int id, UpdateSpaceDto dto)
    {
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == id);
        if (workspace is null)
            return NotFound(new { error = "Space not found." });

        if (!WorkspaceTypes.All.Contains(dto.Type))
            return BadRequest(new { error = "Type must be 'Desk', 'Room' or 'Office'." });

        var office = await db.Offices.FindAsync(dto.OfficeId!.Value);
        if (office is null)
            return BadRequest(new { error = "Office not found." });

        workspace.OfficeId = office.Id;
        workspace.Name = dto.Name.Trim();
        workspace.Type = dto.Type;
        workspace.Capacity = dto.Capacity!.Value;
        workspace.Amenities = JoinAmenities(dto.Amenities);
        workspace.IsActive = dto.IsActive!.Value;

        await db.SaveChangesAsync();

        return Ok(ToSpaceDto(workspace, office.Name));
    }

    // GET /api/spaces/occupancy?date=YYYY-MM-DD — Manager only. Flat per-office list.
    [HttpGet("occupancy")]
    [Authorize(Roles = Roles.Manager)]
    public async Task<IActionResult> Occupancy([FromQuery] DateOnly? date)
    {
        var day = date ?? LocalDay.Today;
        var (startUtc, endUtc) = LocalDay.ToUtcWindow(day);

        // "Booked now": for today this is the current instant; for another date
        // it is the same local time of day on that date.
        var nowUtc = DateTime.UtcNow;
        var referenceUtc = nowUtc >= startUtc && nowUtc < endUtc
            ? nowUtc
            : startUtc + DateTime.Now.TimeOfDay;

        var offices = await db.Offices.OrderBy(o => o.Ordinal).ToListAsync();
        var workspaces = await db.Workspaces.Where(w => w.IsActive).ToListAsync();
        var dayBookings = await db.Bookings
            .Where(b => b.Status == BookingStatus.Confirmed
                        && b.StartsAt < endUtc
                        && b.EndsAt > startUtc)
            .ToListAsync();

        var bookedNowWorkspaceIds = dayBookings
            .Where(b => b.StartsAt <= referenceUtc && b.EndsAt > referenceUtc)
            .Select(b => b.WorkspaceId)
            .ToHashSet();

        var officeDtos = offices.Select(o =>
        {
            var inOffice = workspaces.Where(w => w.OfficeId == o.Id).ToList();
            return new OccupancyOfficeDto(
                o.Id,
                o.Name,
                inOffice.Count,
                inOffice.Count(w => bookedNowWorkspaceIds.Contains(w.Id)));
        }).ToList();

        return Ok(new OccupancyDto(day.ToString("yyyy-MM-dd"), officeDtos, dayBookings.Count));
    }

    private int CurrentUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private static string[] SplitAmenities(string amenities) =>
        amenities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string JoinAmenities(string[]? amenities) =>
        amenities is null
            ? string.Empty
            : string.Join(',', amenities.Select(a => a.Trim()).Where(a => a.Length > 0));

    private static SpaceDto ToSpaceDto(Workspace w, string officeName) =>
        new(w.Id, w.OfficeId, officeName, w.Name, w.Type, w.Capacity, SplitAmenities(w.Amenities), w.IsActive);
}
