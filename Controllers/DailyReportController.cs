using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DailyReportController : ControllerBase
{
    private readonly AppDbContext _context;

    public DailyReportController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? disiplin,
        [FromQuery] string? kategori,
        [FromQuery] string? tagNumber,
        [FromQuery] string? direksi,
        [FromQuery] string? status,
        [FromQuery] string? tanggal_dari,
        [FromQuery] string? tanggal_sampai)
    {
        var query = _context.DailyReports.AsQueryable();

        if (!string.IsNullOrEmpty(disiplin))
            query = query.Where(r => r.Disiplin == disiplin);

        if (!string.IsNullOrEmpty(kategori))
            query = query.Where(r => r.Kategori == kategori);
    
        if (!string.IsNullOrEmpty(direksi))
            query = query.Where(r => r.Direksi == direksi);

        if (!string.IsNullOrEmpty(tagNumber))
            query = query.Where(r => r.TagNumber == tagNumber);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.StatusPekerjaan == status);

        if (!string.IsNullOrEmpty(tanggal_dari) && DateTime.TryParse(tanggal_dari, out var dari))
            query = query.Where(r => r.TanggalLaporan >= dari);

        if (!string.IsNullOrEmpty(tanggal_sampai) && DateTime.TryParse(tanggal_sampai, out var sampai))
            query = query.Where(r => r.TanggalLaporan <= sampai);

        var result = await query
            .OrderByDescending(r => r.TanggalLaporan)
            .ThenBy(r => r.Disiplin)
            .Select(r => new
            {
                idReport        = r.IdReport,
                tanggalLaporan  = r.TanggalLaporan,
                disiplin        = r.Disiplin,
                kategori        = r.Kategori,
                direksi         = r.Direksi,
                tagNumber       = r.TagNumber,
                deskripsi       = r.Deskripsi,
                statusPekerjaan = r.StatusPekerjaan,
                catatan         = r.Catatan,
                pengirimWa      = r.PengirimWa,
                createdAt       = r.CreatedAt
            })
            .ToListAsync();

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var r = await _context.DailyReports.FindAsync(id);
        if (r == null) return NotFound();
        return Ok(r);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "admin,pic,manager,section_head,supervisor,technician")]
    public async Task<IActionResult> Delete(string id)
    {
        var r = await _context.DailyReports.FindAsync(id);
        if (r == null) return NotFound();
        _context.DailyReports.Remove(r);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Laporan berhasil dihapus" });
    }
}