using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;

namespace RefineryContractAPI.Controllers;

/// <summary>
/// API baca-saja (read-only) untuk sistem lain: daftar kontrak dan detailnya (amandemen + progress).
/// Tanpa login user, tetapi wajib header "X-Api-Key" yang cocok dengan env var PublicApi__ApiKey.
/// Kalau env var itu belum diisi (atau kurang dari 32 karakter) endpoint ini mati (404).
/// Sengaja TIDAK menampilkan tagihan, dokumen/file, bukti progress, dan harga satuan.
/// </summary>
[ApiController]
[Route("api/public/contracts")]
[AllowAnonymous]
public class PublicContractsController : ControllerBase
{
    private const int MinKeyLength = 32;
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public PublicContractsController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    // GET /api/public/contracts?page=1&pageSize=50&status=Active&tipe=Lumpsum&q=kata
    [HttpGet]
    public async Task<IActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string? status = null,
        [FromQuery] string? tipe = null,
        [FromQuery] string? q = null)
    {
        var denied = CheckApiKey();
        if (denied != null) return denied;

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.Kontraks.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(k => k.StatusKontrak == status);
        if (!string.IsNullOrWhiteSpace(tipe))
            query = query.Where(k => k.TipeKontrak == tipe);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim().ToLower();
            query = query.Where(k =>
                k.JudulKontrak.ToLower().Contains(term) ||
                (k.NoDokumenKontrak != null && k.NoDokumenKontrak.ToLower().Contains(term)) ||
                (k.NoPoPr != null && k.NoPoPr.ToLower().Contains(term)) ||
                (k.Vendor != null && k.Vendor.NamaVendor.ToLower().Contains(term)));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(k => k.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(k => new
            {
                id = k.IdKontrak,
                judul = k.JudulKontrak,
                noDokumen = k.NoDokumenKontrak,
                noPoPr = k.NoPoPr,
                tipe = k.TipeKontrak,
                status = k.StatusKontrak,
                vendor = k.Vendor != null ? k.Vendor.NamaVendor : null,
                tanggalMulai = k.TanggalMulaiBaru ?? k.TanggalMulai,
                tanggalSelesai = k.TanggalSelesaiBaru ?? k.TanggalSelesai,
                nilaiKontrak = k.NilaiKontrakBaru ?? k.NilaiAwal,
                progressPlan = k.ProgressPlan,
                progressActual = k.ProgressActual,
                hasAmendment = k.HasAmendment,
            })
            .ToListAsync();

        return Ok(new { page, pageSize, total, items });
    }

    // GET /api/public/contracts/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDetail(string id)
    {
        var denied = CheckApiKey();
        if (denied != null) return denied;

        var k = await _context.Kontraks.AsNoTracking()
            .Include(x => x.Vendor)
            .FirstOrDefaultAsync(x => x.IdKontrak == id);
        if (k == null) return NotFound(new { message = "Kontrak tidak ditemukan." });

        var amandemen = await _context.AmandemenKontraks.AsNoTracking()
            .Where(a => a.IdKontrak == id)
            .OrderBy(a => a.NomorUrut)
            .Select(a => new
            {
                nomorUrut = a.NomorUrut,
                noAmandemen = a.NoAmandemen,
                tanggalAmandemen = a.TanggalAmandemen,
                jenis = a.JenisAmandemen,
                nilaiKontrakBaru = a.NilaiKontrakBaru,
                durasiHari = a.DurasiAmandemen,
                tanggalMulaiBaru = a.TanggalMulaiBaru,
                tanggalSelesaiBaru = a.TanggalSelesaiBaru,
                alasan = a.AlasanPerubahan,
            })
            .ToListAsync();

        var progressLumpsum = await _context.ProgressLumpsums.AsNoTracking()
            .Where(p => p.IdKontrak == id)
            .OrderBy(p => p.TanggalUpdate)
            .Select(p => new { milestone = p.Milestone, persen = p.Persen, tanggalUpdate = p.TanggalUpdate })
            .ToListAsync();

        var progressUnitPrice = await _context.ProgressUnitPrices.AsNoTracking()
            .Where(p => p.IdKontrak == id)
            .OrderBy(p => p.TanggalUpdate)
            .Select(p => new
            {
                namaItem = p.NamaItem,
                satuan = p.Satuan,
                qtyRencana = p.QtyRencana,
                qtyAktual = p.QtyAktual,
                tanggalUpdate = p.TanggalUpdate,
            })
            .ToListAsync();

        var monitoringLtsa = await _context.MonitoringLtsas.AsNoTracking()
            .Where(m => m.IdKontrak == id)
            .OrderBy(m => m.TanggalKunjungan)
            .Select(m => new
            {
                tanggalKunjungan = m.TanggalKunjungan,
                jenisLayanan = m.JenisLayanan,
                durasiJam = m.DurasiJam,
                slaTerpenuhi = m.SlaTerpenuhi,
                keterangan = m.Keterangan,
            })
            .ToListAsync();

        return Ok(new
        {
            id = k.IdKontrak,
            judul = k.JudulKontrak,
            noDokumen = k.NoDokumenKontrak,
            noPoPr = k.NoPoPr,
            noIrkap = k.NoIrkap,
            tipe = k.TipeKontrak,
            status = k.StatusKontrak,
            vendor = k.Vendor?.NamaVendor,
            direksiPekerjaan = k.DireksiPekerjaan,
            programKerja = k.ProgramKerja,
            planner = k.Planner,
            tanggalKom = k.TanggalKom,
            tanggalMulai = k.TanggalMulai,
            tanggalSelesai = k.TanggalSelesai,
            tanggalMulaiEfektif = k.TanggalMulaiBaru ?? k.TanggalMulai,
            tanggalSelesaiEfektif = k.TanggalSelesaiBaru ?? k.TanggalSelesai,
            durasiHari = k.DurasiKontrakHari,
            nilaiAwal = k.NilaiAwal,
            nilaiKontrak = k.NilaiKontrakBaru ?? k.NilaiAwal,
            progressPlan = k.ProgressPlan,
            progressActual = k.ProgressActual,
            aktivitasSaatIni = k.AktivitasSaatIni,
            kendala = k.Kendala,
            hasAmendment = k.HasAmendment,
            amandemen,
            progress = new { lumpsum = progressLumpsum, unitPrice = progressUnitPrice, monitoringLtsa },
            updatedAt = k.UpdatedAt,
        });
    }

    /// <summary>Null bila API key valid; selain itu hasil penolakan yang harus dikembalikan.</summary>
    private IActionResult? CheckApiKey()
    {
        var configured = _config["PublicApi:ApiKey"];
        if (string.IsNullOrEmpty(configured) || configured.Length < MinKeyLength)
            return NotFound(new { message = "API publik tidak aktif." });

        var provided = Request.Headers["X-Api-Key"].ToString();
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        if (!CryptographicOperations.FixedTimeEquals(a, b))
            return Unauthorized(new { message = "API key tidak valid." });

        return null;
    }
}
