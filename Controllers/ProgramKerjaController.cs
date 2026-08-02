using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/program-kerja")]
[Authorize]
public class ProgramKerjaController : ControllerBase
{
    private readonly AppDbContext _context;

    public ProgramKerjaController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.ProgramKerjas
            .OrderBy(p => p.Nama)
            .Select(p => new ProgramKerjaDto
            {
                IdProgramKerja = p.IdProgramKerja,
                Nama = p.Nama,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt
            }).ToListAsync();

        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var p = await _context.ProgramKerjas.FindAsync(id);
        if (p == null) return NotFound();

        return Ok(new ProgramKerjaDto
        {
            IdProgramKerja = p.IdProgramKerja,
            Nama = p.Nama,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProgramKerjaDto dto)
    {
        var entity = new ProgramKerja
        {
            Nama = dto.Nama
        };

        _context.ProgramKerjas.Add(entity);
        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateProgramKerjaDto dto)
    {
        var entity = await _context.ProgramKerjas.FindAsync(id);
        if (entity == null) return NotFound();

        entity.Nama = dto.Nama;
        entity.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(entity);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var entity = await _context.ProgramKerjas.FindAsync(id);
        if (entity == null) return NotFound();

        _context.ProgramKerjas.Remove(entity);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Program Kerja berhasil dihapus" });
    }
}
