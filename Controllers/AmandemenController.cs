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
public class AmandemenController : ControllerBase
{
    private readonly AppDbContext _context;
    public AmandemenController(AppDbContext context) => _context = context;

    [HttpGet("kontrak/{idKontrak}")]
    public async Task<IActionResult> GetByKontrak(string idKontrak)
    {
        var list = await _context.AmandemenKontraks
            .Where(a => a.IdKontrak == idKontrak)
            .OrderBy(a => a.NomorUrut)
            .Select(a => new AmandemenDto
            {
                IdAmandemen = a.IdAmandemen,
                IdKontrak = a.IdKontrak,
                NomorUrut = a.NomorUrut,
                NoAmandemen = a.NoAmandemen,
                TanggalAmandemen = a.TanggalAmandemen,
                JenisAmandemen = a.JenisAmandemen,
                NilaiKontrakBaru = a.NilaiKontrakBaru,
                DurasiAmandemen = a.DurasiAmandemen,
                TanggalMulaiBaru = a.TanggalMulaiBaru,
                TanggalSelesaiBaru = a.TanggalSelesaiBaru,
                AlasanPerubahan = a.AlasanPerubahan,
                CreatedAt = a.CreatedAt
            }).ToListAsync();

        return Ok(list);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAmandemenDto dto)
    {
        var amandemen = new AmandemenKontrak
        {
            IdKontrak = dto.IdKontrak,
            NomorUrut = dto.NomorUrut,
            NoAmandemen = dto.NoAmandemen,
            TanggalAmandemen = dto.TanggalAmandemen,
            JenisAmandemen = dto.JenisAmandemen,
            NilaiKontrakBaru = dto.NilaiKontrakBaru,
            DurasiAmandemen = dto.DurasiAmandemen,
            TanggalMulaiBaru = dto.TanggalMulaiBaru,
            TanggalSelesaiBaru = dto.TanggalSelesaiBaru,
            AlasanPerubahan = dto.AlasanPerubahan
        };

        _context.AmandemenKontraks.Add(amandemen);

        var kontrak = await _context.Kontraks.FindAsync(dto.IdKontrak);
        if (kontrak != null) kontrak.HasAmendment = true;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Berhasil", idAmandemen = amandemen.IdAmandemen });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateAmandemenDto dto)
    {
        var amandemen = await _context.AmandemenKontraks.FindAsync(id);
        if (amandemen == null) return NotFound();

        amandemen.NoAmandemen = dto.NoAmandemen;
        amandemen.TanggalAmandemen = dto.TanggalAmandemen;
        amandemen.JenisAmandemen = dto.JenisAmandemen;
        amandemen.NilaiKontrakBaru = dto.NilaiKontrakBaru;
        amandemen.DurasiAmandemen = dto.DurasiAmandemen;
        amandemen.TanggalMulaiBaru = dto.TanggalMulaiBaru;
        amandemen.TanggalSelesaiBaru = dto.TanggalSelesaiBaru;
        amandemen.AlasanPerubahan = dto.AlasanPerubahan;
        amandemen.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Berhasil", idAmandemen = amandemen.IdAmandemen });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var amandemen = await _context.AmandemenKontraks.FindAsync(id);
        if (amandemen == null) return NotFound();

        var idKontrak = amandemen.IdKontrak;
        _context.AmandemenKontraks.Remove(amandemen);
        await _context.SaveChangesAsync();

        var remaining = await _context.AmandemenKontraks.AnyAsync(a => a.IdKontrak == idKontrak);
        if (!remaining)
        {
            var kontrak = await _context.Kontraks.FindAsync(idKontrak);
            if (kontrak != null) kontrak.HasAmendment = false;
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Amandemen berhasil dihapus" });
    }



    [HttpGet("all")]
public async Task<IActionResult> GetAll()
{
    var list = await _context.AmandemenKontraks
        .OrderByDescending(a => a.CreatedAt)
        .Select(a => new AmandemenDto
        {
            IdAmandemen = a.IdAmandemen,
            IdKontrak = a.IdKontrak,
            NomorUrut = a.NomorUrut,
            NoAmandemen = a.NoAmandemen,
            TanggalAmandemen = a.TanggalAmandemen,
            JenisAmandemen = a.JenisAmandemen,
            NilaiKontrakBaru = a.NilaiKontrakBaru,
            DurasiAmandemen = a.DurasiAmandemen,
            TanggalMulaiBaru = a.TanggalMulaiBaru,
            TanggalSelesaiBaru = a.TanggalSelesaiBaru,
            AlasanPerubahan = a.AlasanPerubahan,
            CreatedAt = a.CreatedAt
        })
        .ToListAsync();

    return Ok(list);
}
}
