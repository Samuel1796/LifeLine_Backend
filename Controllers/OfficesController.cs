using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Nook.Api.Data;

namespace Nook.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/offices")]
public class OfficesController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var offices = await db.Offices
            .OrderBy(o => o.Ordinal)
            .Select(o => new { id = o.Id, name = o.Name, ordinal = o.Ordinal })
            .ToListAsync();

        return Ok(offices);
    }
}
