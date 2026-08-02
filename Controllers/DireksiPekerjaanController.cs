using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/direksi-pekerjaan")]
[Authorize]
public class DireksiPekerjaanController : ControllerBase
{
    private readonly AppDbContext _context;

    public DireksiPekerjaanController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.DireksiPekerjaans
            .OrderBy(d => d.Nama)
            .Select(d => new DireksiPekerjaanDto
            {
                IdDireksiPekerjaan = d.IdDireksiPekerjaan,
                Nama = d.Nama,
                Jabatan = d.Jabatan,
                SubArea = d.SubArea,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt
            }).ToListAsync();

        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var d = await _context.DireksiPekerjaans.FindAsync(id);
        if (d == null) return NotFound();

        return Ok(new DireksiPekerjaanDto
        {
            IdDireksiPekerjaan = d.IdDireksiPekerjaan,
            Nama = d.Nama,
            Jabatan = d.Jabatan,
            SubArea = d.SubArea,
            CreatedAt = d.CreatedAt,
            UpdatedAt = d.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateDireksiPekerjaanDto dto)
    {
        var entity = new DireksiPekerjaan
        {
            Nama = dto.Nama,
            Jabatan = dto.Jabatan,
            SubArea = dto.SubArea
        };

        _context.DireksiPekerjaans.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateDireksiPekerjaanDto dto)
    {
        var entity = await _context.DireksiPekerjaans.FindAsync(id);
        if (entity == null) return NotFound();

        entity.Nama = dto.Nama;
        entity.Jabatan = dto.Jabatan;
        entity.SubArea = dto.SubArea;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var entity = await _context.DireksiPekerjaans.FindAsync(id);
        if (entity == null) return NotFound();

        _context.DireksiPekerjaans.Remove(entity);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Direksi Pekerjaan berhasil dihapus" });
    }
}
