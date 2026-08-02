using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/sla-setting")]
[Authorize]
public class SlaSettingController : ControllerBase
{
    private readonly AppDbContext _context;

    public SlaSettingController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var data = await _context.SlaSettings
            .OrderBy(s => s.KodeTahap)
            .Select(s => new SlaSettingDto
            {
                KodeTahap     = s.KodeTahap,
                BatasHari     = s.BatasHari,
                WarningPersen = s.WarningPersen,
            })
            .ToListAsync();

        return Ok(data);
    }

    [HttpPut("{kodeTahap}")]
    public async Task<IActionResult> Update(string kodeTahap, [FromBody] UpdateSlaSettingDto dto)
    {
        var setting = await _context.SlaSettings.FindAsync(kodeTahap);
        if (setting == null) return NotFound();

        setting.BatasHari     = dto.BatasHari;
        setting.WarningPersen = dto.WarningPersen;

        await _context.SaveChangesAsync();
        return Ok(setting);
    }
}