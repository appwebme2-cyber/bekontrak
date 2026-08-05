using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class KonfigurasiController : ControllerBase
{
    private readonly AppDbContext _context;
    public KonfigurasiController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.KonfigurasiSistems
            .OrderBy(k => k.NamaSetting)
            .Select(k => new KonfigurasiDto
            {
                IdSetting = k.IdSetting,
                NamaSetting = k.NamaSetting,
                NilaiSetting = k.NilaiSetting,
                Deskripsi = k.Deskripsi,
                UpdatedAt = k.UpdatedAt
            }).ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Create([FromBody] CreateKonfigurasiDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.NamaSetting))
            return BadRequest(new { message = "NamaSetting tidak boleh kosong." });

        var exists = await _context.KonfigurasiSistems
            .AnyAsync(k => k.NamaSetting == dto.NamaSetting);
        if (exists)
            return Conflict(new { message = $"Konfigurasi '{dto.NamaSetting}' sudah ada. Gunakan PUT untuk update." });

        var config = new RefineryContractAPI.Models.KonfigurasiSistem
        {
            IdSetting = Guid.NewGuid().ToString(),
            NamaSetting = dto.NamaSetting,
            NilaiSetting = dto.NilaiSetting,
            Deskripsi = dto.Deskripsi,
            UpdatedAt = DateTime.UtcNow
        };

        _context.KonfigurasiSistems.Add(config);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetAll), new KonfigurasiDto
        {
            IdSetting = config.IdSetting,
            NamaSetting = config.NamaSetting,
            NilaiSetting = config.NilaiSetting,
            Deskripsi = config.Deskripsi,
            UpdatedAt = config.UpdatedAt
        });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateKonfigurasiDto dto)
    {
        var config = await _context.KonfigurasiSistems.FindAsync(id);
        if (config == null) return NotFound();

        config.NilaiSetting = dto.NilaiSetting;
        config.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(config);
    }
}
