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
public class MonitoringLtsaController : ControllerBase
{
    private readonly AppDbContext _context;
    public MonitoringLtsaController(AppDbContext context) => _context = context;

    [HttpGet("kontrak/{idKontrak}")]
    public async Task<IActionResult> GetByKontrak(string idKontrak)
    {
        var list = await _context.MonitoringLtsas
            .Where(m => m.IdKontrak == idKontrak)
            .OrderByDescending(m => m.TanggalKunjungan)
            .Select(m => new MonitoringLtsaDto
            {
                IdLog = m.IdLog,
                IdKontrak = m.IdKontrak,
                TanggalKunjungan = m.TanggalKunjungan,
                JenisLayanan = m.JenisLayanan,
                DurasiJam = m.DurasiJam,
                SlaTerpenuhi = m.SlaTerpenuhi,
                Keterangan = m.Keterangan,
                CreatedAt = m.CreatedAt
            }).ToListAsync();

        return Ok(list);
    }

    [HttpGet("kontrak/{idKontrak}/stats")]
    public async Task<IActionResult> GetStats(string idKontrak)
    {
        var logs = await _context.MonitoringLtsas
            .Where(m => m.IdKontrak == idKontrak)
            .ToListAsync();

        var stats = new
        {
            TotalKunjungan = logs.Count,
            TotalDurasiJam = logs.Sum(l => l.DurasiJam),
            SlaFulfilled = logs.Count(l => l.SlaTerpenuhi == "Yes"),
            SlaNotFulfilled = logs.Count(l => l.SlaTerpenuhi == "No"),
            SlaPercentage = logs.Count > 0
                ? Math.Round((double)logs.Count(l => l.SlaTerpenuhi == "Yes") / logs.Count * 100, 1)
                : 0,
            ByJenisLayanan = new
            {
                Preventive = logs.Count(l => l.JenisLayanan == "Preventive"),
                Corrective = logs.Count(l => l.JenisLayanan == "Corrective"),
                Standby = logs.Count(l => l.JenisLayanan == "Standby")
            }
        };

        return Ok(stats);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMonitoringLtsaDto dto)
    {
        var monitoring = new MonitoringLtsa
        {
            IdKontrak = dto.IdKontrak,
            TanggalKunjungan = dto.TanggalKunjungan,
            JenisLayanan = dto.JenisLayanan,
            DurasiJam = dto.DurasiJam,
            SlaTerpenuhi = dto.SlaTerpenuhi,
            Keterangan = dto.Keterangan
        };

        _context.MonitoringLtsas.Add(monitoring);
        await _context.SaveChangesAsync();
        return Ok(monitoring);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] CreateMonitoringLtsaDto dto)
    {
        var monitoring = await _context.MonitoringLtsas.FindAsync(id);
        if (monitoring == null) return NotFound();

        monitoring.TanggalKunjungan = dto.TanggalKunjungan;
        monitoring.JenisLayanan = dto.JenisLayanan;
        monitoring.DurasiJam = dto.DurasiJam;
        monitoring.SlaTerpenuhi = dto.SlaTerpenuhi;
        monitoring.Keterangan = dto.Keterangan;

        await _context.SaveChangesAsync();
        return Ok(monitoring);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var monitoring = await _context.MonitoringLtsas.FindAsync(id);
        if (monitoring == null) return NotFound();

        _context.MonitoringLtsas.Remove(monitoring);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Log monitoring berhasil dihapus" });
    }
}
