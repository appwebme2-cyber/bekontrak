using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.Services;

namespace RefineryContractAPI.Controllers;

/// <summary>Daftar Refinery Unit untuk pilihan RU di tampilan (multi-RU).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RuController : ControllerBase
{
    private static readonly string[] StatusAktif = { "active", "aktif" };
    private static readonly string[] StatusSelesai = { "completed", "selesai" };

    private readonly AppDbContext _context;
    private readonly ICurrentRu _currentRu;

    public RuController(AppDbContext context, ICurrentRu currentRu)
    {
        _context = context;
        _currentRu = currentRu;
    }

    // GET /api/Ru
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.Rus.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Urutan)
            .Select(r => new { idRu = r.IdRu, kode = r.Kode, nama = r.Nama, label = r.Kode + " " + r.Nama })
            .ToListAsync();
        return Ok(list);
    }

    // GET /api/Ru/perbandingan — ringkasan per RU untuk Super Admin & Head Office.
    // Selalu menghitung semua RU, apa pun RU yang sedang dipilih di header.
    [HttpGet("perbandingan")]
    public async Task<IActionResult> GetPerbandingan()
    {
        if (!_currentRu.IsCrossRu)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Perbandingan RU hanya untuk Super Admin dan Head Office." });

        var rus = await _context.Rus.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Urutan)
            .Select(r => new { r.IdRu, r.Kode, r.Nama })
            .ToListAsync();

        // Ambil kolom seperlunya lalu hitung di memori (jumlah data per RU masih ribuan baris)
        var kontrak = await _context.Kontraks.IgnoreQueryFilters().AsNoTracking()
            .Select(k => new
            {
                k.IdRu,
                k.StatusKontrak,
                k.KomTerlambat,
                k.ProgressActual,
                Nilai = k.NilaiKontrakBaru ?? k.NilaiAwal ?? 0m,
            })
            .ToListAsync();

        var tagihan = await _context.Tagihans.IgnoreQueryFilters().AsNoTracking()
            .Select(t => new { t.Kontrak!.IdRu, t.NilaiTagihan })
            .ToListAsync();

        var padi = await _context.Padis.IgnoreQueryFilters().AsNoTracking()
            .Select(p => new { p.IdRu, p.Nilai })
            .ToListAsync();

        var pengguna = await _context.Profiles.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.IsActive && p.IdRu != null)
            .Select(p => p.IdRu)
            .ToListAsync();

        var rows = rus.Select(r =>
        {
            var k = kontrak.Where(x => x.IdRu == r.IdRu).ToList();
            var aktif = k.Where(x => StatusAktif.Contains((x.StatusKontrak ?? "").Trim().ToLowerInvariant())).ToList();
            var t = tagihan.Where(x => x.IdRu == r.IdRu).ToList();
            var p = padi.Where(x => x.IdRu == r.IdRu).ToList();
            return new
            {
                idRu = r.IdRu,
                kode = r.Kode,
                nama = r.Nama,
                label = r.Kode + " " + r.Nama,
                jumlahKontrak = k.Count,
                kontrakAktif = aktif.Count,
                kontrakPreKom = k.Count(x => (x.StatusKontrak ?? "").Trim().Equals("Pre-KOM", StringComparison.OrdinalIgnoreCase)),
                kontrakSelesai = k.Count(x => StatusSelesai.Contains((x.StatusKontrak ?? "").Trim().ToLowerInvariant())),
                komTerlambat = k.Count(x => x.KomTerlambat),
                nilaiKontrak = k.Sum(x => x.Nilai),
                rataProgressAktif = aktif.Count(x => x.ProgressActual.HasValue) > 0
                    ? Math.Round(aktif.Where(x => x.ProgressActual.HasValue).Average(x => x.ProgressActual!.Value), 1)
                    : (double?)null,
                jumlahTagihan = t.Count,
                nilaiTagihan = t.Sum(x => x.NilaiTagihan),
                jumlahPadi = p.Count,
                nilaiPadi = p.Sum(x => x.Nilai),
                penggunaAktif = pengguna.Count(x => x == r.IdRu),
            };
        }).ToList();

        return Ok(rows);
    }
}
