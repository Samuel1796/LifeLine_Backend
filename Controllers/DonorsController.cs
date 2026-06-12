using System.Security.Claims;
using BloodDonorFinder.Api.Data;
using BloodDonorFinder.Api.Dtos;
using BloodDonorFinder.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BloodDonorFinder.Api.Controllers;

[ApiController]
[Route("api/donors")]
[Authorize]
public class DonorsController(AppDbContext db) : ControllerBase
{
    private int CurrentUserId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("me/profile")]
    [Authorize(Roles = Roles.Donor)]
    public async Task<ActionResult<DonorProfileResponseDto>> GetMyProfile()
    {
        var profile = await db.DonorProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == CurrentUserId);

        return profile is null
            ? NotFound(new { message = "No donor profile yet." })
            : Ok(ToDto(profile));
    }

    [HttpPut("me/profile")]
    [Authorize(Roles = Roles.Donor)]
    public async Task<ActionResult<DonorProfileResponseDto>> UpsertMyProfile(DonorProfileDto dto)
    {
        if (!BloodTypes.IsValid(dto.BloodType))
            return BadRequest(new { message = "Invalid blood type." });

        var profile = await db.DonorProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == CurrentUserId);

        if (profile is null)
        {
            profile = new DonorProfile { UserId = CurrentUserId };
            db.DonorProfiles.Add(profile);
        }

        profile.BloodType = dto.BloodType;
        profile.Latitude = dto.Latitude;
        profile.Longitude = dto.Longitude;
        profile.City = dto.City;
        profile.IsAvailable = dto.IsAvailable;
        profile.IsAnonymous = dto.IsAnonymous;
        profile.LastDonationDate = dto.LastDonationDate;

        await db.SaveChangesAsync();
        await db.Entry(profile).Reference(p => p.User).LoadAsync();

        return Ok(ToDto(profile));
    }

    [HttpPatch("me/availability")]
    [Authorize(Roles = Roles.Donor)]
    public async Task<IActionResult> SetAvailability(AvailabilityDto dto)
    {
        var profile = await db.DonorProfiles.FirstOrDefaultAsync(p => p.UserId == CurrentUserId);
        if (profile is null)
            return NotFound(new { message = "Create your donor profile first." });

        profile.IsAvailable = dto.IsAvailable;
        await db.SaveChangesAsync();
        return Ok(new { profile.IsAvailable });
    }

    // Donor map for requesters. Includes unavailable donors (shown with a tag client-side);
    // anonymous donors keep their contact info but their name is masked.
    [HttpGet("map")]
    [Authorize(Roles = Roles.Requester)]
    public async Task<ActionResult<List<MapDonorDto>>> Map()
    {
        var profiles = await db.DonorProfiles
            .Include(p => p.User)
            .ToListAsync();

        var result = profiles.Select(p => new MapDonorDto(
            p.UserId,
            p.IsAnonymous ? "Anonymous donor" : p.User.FullName,
            p.User.Phone,
            p.BloodType,
            p.Latitude,
            p.Longitude,
            p.City,
            p.IsAvailable,
            p.IsAnonymous
        )).ToList();

        return Ok(result);
    }

    private static DonorProfileResponseDto ToDto(DonorProfile p) => new(
        p.UserId, p.User.FullName, p.BloodType, p.Latitude, p.Longitude,
        p.City, p.IsAvailable, p.IsAnonymous, p.LastDonationDate);
}
