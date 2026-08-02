using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProgressLumpsumController : ControllerBase
{
    private readonly AppDbContext _context;
    public ProgressLumpsumController(AppDbContext context) => _context = context;

    [HttpGet("kontrak/{idKontrak}")]
    public async Task<IActionResult> GetByKontrak(string idKontrak)
    {
        var list = await _context.ProgressLumpsums
            .Where(p => p.IdKontrak == idKontrak)
            .OrderByDescending(p => p.TanggalUpdate)
            .Select(p => new ProgressLumpsumDto
            {
                IdProgress = p.IdProgress,
                IdKontrak = p.IdKontrak,
                Milestone = p.Milestone,
                Persen = p.Persen,
                TanggalUpdate = p.TanggalUpdate,
                Evidence = p.Evidence,
                CreatedAt = p.CreatedAt
            }).ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProgressLumpsumDto dto)
    {
        var progress = new ProgressLumpsum
        {
            IdKontrak = dto.IdKontrak,
            Milestone = dto.Milestone,
            Persen = dto.Persen,
            TanggalUpdate = dto.TanggalUpdate,
            Evidence = dto.Evidence
        };

        _context.ProgressLumpsums.Add(progress);

        // Update progress_actual di kontrak
        var kontrak = await _context.Kontraks.FindAsync(dto.IdKontrak);
        if (kontrak != null)
        {
            kontrak.ProgressActual = dto.Persen;
            kontrak.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        return Ok(progress);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] CreateProgressLumpsumDto dto)
    {
        var progress = await _context.ProgressLumpsums.FindAsync(id);
        if (progress == null) return NotFound();

        progress.Milestone = dto.Milestone;
        progress.Persen = dto.Persen;
        progress.TanggalUpdate = dto.TanggalUpdate;
        progress.Evidence = dto.Evidence;

        await _context.SaveChangesAsync();
        return Ok(progress);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var progress = await _context.ProgressLumpsums.FindAsync(id);
        if (progress == null) return NotFound();

        _context.ProgressLumpsums.Remove(progress);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Progress berhasil dihapus" });
    }
}
