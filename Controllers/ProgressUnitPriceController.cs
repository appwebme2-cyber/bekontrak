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
public class ProgressUnitPriceController : ControllerBase
{
    private readonly AppDbContext _context;
    public ProgressUnitPriceController(AppDbContext context) => _context = context;

    [HttpGet("kontrak/{idKontrak}")]
    public async Task<IActionResult> GetByKontrak(string idKontrak)
    {
        var list = await _context.ProgressUnitPrices
            .Where(p => p.IdKontrak == idKontrak)
            .OrderByDescending(p => p.TanggalUpdate)
            .Select(p => new ProgressUnitPriceDto
            {
                IdProgress = p.IdProgress,
                IdKontrak = p.IdKontrak,
                NamaItem = p.NamaItem,
                Satuan = p.Satuan,
                QtyRencana = p.QtyRencana,
                QtyAktual = p.QtyAktual,
                HargaSatuan = p.HargaSatuan,
                TanggalUpdate = p.TanggalUpdate,
                CreatedAt = p.CreatedAt,
                TotalRencana = p.QtyRencana * (double)p.HargaSatuan,
                TotalAktual = p.QtyAktual * (double)p.HargaSatuan
            }).ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateProgressUnitPriceDto dto)
    {
        var progress = new ProgressUnitPrice
        {
            IdKontrak = dto.IdKontrak,
            NamaItem = dto.NamaItem,
            Satuan = dto.Satuan,
            QtyRencana = dto.QtyRencana,
            QtyAktual = dto.QtyAktual,
            HargaSatuan = dto.HargaSatuan,
            TanggalUpdate = dto.TanggalUpdate
        };

        _context.ProgressUnitPrices.Add(progress);
        await _context.SaveChangesAsync();
        return Ok(progress);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] CreateProgressUnitPriceDto dto)
    {
        var progress = await _context.ProgressUnitPrices.FindAsync(id);
        if (progress == null) return NotFound();

        progress.NamaItem = dto.NamaItem;
        progress.Satuan = dto.Satuan;
        progress.QtyRencana = dto.QtyRencana;
        progress.QtyAktual = dto.QtyAktual;
        progress.HargaSatuan = dto.HargaSatuan;
        progress.TanggalUpdate = dto.TanggalUpdate;

        await _context.SaveChangesAsync();
        return Ok(progress);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var progress = await _context.ProgressUnitPrices.FindAsync(id);
        if (progress == null) return NotFound();

        _context.ProgressUnitPrices.Remove(progress);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Progress berhasil dihapus" });
    }
}
