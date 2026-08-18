using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LogAksesController : ControllerBase
{
    private readonly AppDbContext _context;

    public LogAksesController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? userId,
        [FromQuery] string? menu,
        [FromQuery] string? activity,
        [FromQuery] DateTime? dari,
        [FromQuery] DateTime? sampai,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != "admin")
            return Forbid();

        var q = _context.LogAkses.AsQueryable();

        if (!string.IsNullOrEmpty(userId))   q = q.Where(l => l.UserId == userId);
        if (!string.IsNullOrEmpty(menu))     q = q.Where(l => l.Menu == menu);
        if (!string.IsNullOrEmpty(activity)) q = q.Where(l => l.Activity == activity);
        if (dari.HasValue)    q = q.Where(l => l.CreatedAt >= dari.Value);
        if (sampai.HasValue)  q = q.Where(l => l.CreatedAt <= sampai.Value.AddDays(1));

        var total = await q.CountAsync();
        var items = await q
            .OrderByDescending(l => l.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new {
                l.Id,
                l.UserId,
                namaUser  = l.NamaUser,
                l.Role,
                l.Menu,
                l.Activity,
                l.Detail,
                ipAddress = l.IpAddress,
                createdAt = l.CreatedAt,
            })
            .ToListAsync();

        return Ok(new { total, page, pageSize, items });
    }

    [HttpDelete("cleanup")]
    public async Task<IActionResult> Cleanup([FromQuery] int olderThanDays = 90)
    {
        var role = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (role != "admin") return Forbid();

        var cutoff = DateTime.UtcNow.AddDays(-olderThanDays);
        var deleted = await _context.LogAkses
            .Where(l => l.CreatedAt < cutoff)
            .ExecuteDeleteAsync();

        return Ok(new { deleted, message = $"{deleted} log dihapus (lebih dari {olderThanDays} hari)" });
    }
}
