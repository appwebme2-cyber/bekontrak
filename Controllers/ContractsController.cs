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
public class ContractsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ContractsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var userRole = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;

        // Kalau role vendor/external → hanya tampilkan kontrak milik vendor tersebut
        if ((userRole == "vendor" || userRole == "external") && userId != null)
        {
            var profile = await _context.Profiles.FindAsync(userId);
            if (profile?.IdVendor == null)
                return Ok(new List<KontrakDto>()); // vendor tanpa id_vendor → kosong

            var vendorContracts = await _context.Kontraks
                .Include(k => k.Vendor)
                .Where(k => k.IdVendor == profile.IdVendor)
                .OrderByDescending(k => k.CreatedAt)
                .Select(k => MapToDto(k))
                .ToListAsync();

            return Ok(vendorContracts);
        }

        // Admin, PIC, Viewer → tampilkan semua kontrak
        var contracts = await _context.Kontraks
            .Include(k => k.Vendor)
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => MapToDto(k))
            .ToListAsync();

        return Ok(contracts);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var k = await _context.Kontraks
            .Include(k => k.Vendor)
            .FirstOrDefaultAsync(k => k.IdKontrak == id);

        if (k == null) return NotFound();
        var dto = MapToDto(k);
        dto.JumlahTagihan = await _context.Tagihans.CountAsync(t => t.IdKontrak == id);
        return Ok(dto);
    }

    [HttpGet("{id}/tagihan-count")]
    public async Task<IActionResult> GetTagihanCount(string id)
    {
        var exists = await _context.Kontraks.AnyAsync(k => k.IdKontrak == id);
        if (!exists) return NotFound();

        var count = await _context.Tagihans.CountAsync(t => t.IdKontrak == id);
        return Ok(new { jumlahTagihan = count });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateKontrakDto dto)
    {
        var kontrak = new Kontrak
        {
            IdVendor = dto.IdVendor,
            JudulKontrak = dto.JudulKontrak,
            NoDokumenKontrak = dto.NoDokumenKontrak,
            NoPoPr = dto.NoPoPr,
            DireksiPekerjaan = dto.DireksiPekerjaan,
            ProgramKerja = dto.ProgramKerja,
            Planner = dto.Planner,
            KboBagian = dto.KboBagian,
            TipeKontrak = dto.TipeKontrak,
            StatusKontrak = dto.StatusKontrak,
            TanggalSpbDiterima = dto.TanggalSpbDiterima,
            TanggalTerimaDokumen = dto.TanggalTerimaDokumen,
            TanggalMaksimalKom = dto.TanggalMaksimalKom,
            TanggalMulai = dto.TanggalMulai,
            TanggalSelesai = dto.TanggalSelesai,
            SlaKomHari = dto.SlaKomHari,
            EstimasiTanggalKom = dto.EstimasiTanggalKom,
            TanggalKom = dto.TanggalKom,
            KomTerlambat = dto.KomTerlambat,
            NilaiAwal = dto.NilaiAwal,
            DurasiKontrakHari = dto.DurasiKontrakHari,
            ProgressPlan = dto.ProgressPlan,
            ProgressActual = dto.ProgressActual,
            AktivitasSaatIni = dto.AktivitasSaatIni,
            Kendala = dto.Kendala,
            Disiplin = dto.Disiplin,
            TkdnPercentage = dto.TkdnPercentage,
            TanggalLkp = dto.TanggalLkp,
            SCurveData = dto.SCurveData,
            ContractDocuments = dto.ContractDocuments,
            AmendmentDocuments = dto.AmendmentDocuments,
            TanggalMpl = dto.TanggalMpl,
            TanggalMpa = dto.TanggalMpa,
            MasaPemeliharaanHari = dto.MasaPemeliharaanHari
        };

        _context.Kontraks.Add(kontrak);
        await _context.SaveChangesAsync();

        var result = await _context.Kontraks
            .Include(k => k.Vendor)
            .FirstOrDefaultAsync(k => k.IdKontrak == kontrak.IdKontrak);

        return Ok(MapToDto(result!));
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateKontrakDto dto)
    {
        var kontrak = await _context.Kontraks.FindAsync(id);
        if (kontrak == null) return NotFound();

        kontrak.IdVendor = dto.IdVendor;
        kontrak.JudulKontrak = dto.JudulKontrak;
        kontrak.NoDokumenKontrak = dto.NoDokumenKontrak;
        kontrak.NoPoPr = dto.NoPoPr;
        kontrak.DireksiPekerjaan = dto.DireksiPekerjaan;
        kontrak.ProgramKerja = dto.ProgramKerja;
        kontrak.Planner = dto.Planner;
        kontrak.KboBagian = dto.KboBagian;
        kontrak.TipeKontrak = dto.TipeKontrak;
        kontrak.StatusKontrak = dto.StatusKontrak;
        kontrak.TanggalSpbDiterima = dto.TanggalSpbDiterima;
        kontrak.TanggalTerimaDokumen = dto.TanggalTerimaDokumen;
        kontrak.TanggalMaksimalKom = dto.TanggalMaksimalKom;
        kontrak.TanggalMulai = dto.TanggalMulai;
        kontrak.TanggalSelesai = dto.TanggalSelesai;
        kontrak.SlaKomHari = dto.SlaKomHari;
        kontrak.EstimasiTanggalKom = dto.EstimasiTanggalKom;
        kontrak.TanggalKom = dto.TanggalKom;
        kontrak.KomTerlambat = dto.KomTerlambat;
        kontrak.NilaiAwal = dto.NilaiAwal;
        kontrak.DurasiKontrakHari = dto.DurasiKontrakHari;
        kontrak.ProgressPlan = dto.ProgressPlan;
        kontrak.ProgressActual = dto.ProgressActual;
        kontrak.AktivitasSaatIni = dto.AktivitasSaatIni;
        kontrak.Kendala = dto.Kendala;
        kontrak.Disiplin = dto.Disiplin;
        kontrak.TkdnPercentage = dto.TkdnPercentage;
        kontrak.TanggalLkp = dto.TanggalLkp;
        if (dto.SCurveData != null) kontrak.SCurveData = dto.SCurveData;
        kontrak.ContractDocuments = dto.ContractDocuments;
        kontrak.AmendmentDocuments = dto.AmendmentDocuments;
        kontrak.TanggalMpl = dto.TanggalMpl;
        kontrak.TanggalMpa = dto.TanggalMpa;
        kontrak.MasaPemeliharaanHari = dto.MasaPemeliharaanHari;
        kontrak.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var result = await _context.Kontraks
            .Include(k => k.Vendor)
            .FirstOrDefaultAsync(k => k.IdKontrak == id);

        return Ok(MapToDto(result!));
    }

    [HttpPut("{id}/scurve")]
    public async Task<IActionResult> UpdateSCurve(string id, [FromBody] UpdateSCurveDto dto)
    {
        var kontrak = await _context.Kontraks.FindAsync(id);
        if (kontrak == null) return NotFound();

        kontrak.SCurveData = dto.SCurveData;
        kontrak.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "S-Curve berhasil disimpan" });
    }

    [HttpPut("{id}/progress")]
    public async Task<IActionResult> UpdateProgress(string id, [FromBody] UpdateProgressDto dto)
    {
        var kontrak = await _context.Kontraks.FindAsync(id);
        if (kontrak == null) return NotFound();

        kontrak.ProgressPlan = dto.ProgressPlan;
        kontrak.ProgressActual = dto.ProgressActual;
        kontrak.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Progress berhasil diperbarui" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var kontrak = await _context.Kontraks.FindAsync(id);
        if (kontrak == null) return NotFound();

        _context.Kontraks.Remove(kontrak);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Kontrak berhasil dihapus" });
    }

    private static KontrakDto MapToDto(Kontrak k) => new()
    {
        IdKontrak = k.IdKontrak,
        IdVendor = k.IdVendor,
        JudulKontrak = k.JudulKontrak,
        NoDokumenKontrak = k.NoDokumenKontrak,
        NoPoPr = k.NoPoPr,
        DireksiPekerjaan = k.DireksiPekerjaan,
        ProgramKerja = k.ProgramKerja,
        Planner = k.Planner,
        KboBagian = k.KboBagian,
        TipeKontrak = k.TipeKontrak,
        StatusKontrak = k.StatusKontrak,
        TanggalSpbDiterima = k.TanggalSpbDiterima,
        TanggalTerimaDokumen = k.TanggalTerimaDokumen,
        TanggalMaksimalKom = k.TanggalMaksimalKom,
        TanggalMulai = k.TanggalMulai,
        TanggalSelesai = k.TanggalSelesai,
        SlaKomHari = k.SlaKomHari,
        EstimasiTanggalKom = k.EstimasiTanggalKom,
        TanggalKom = k.TanggalKom,
        KomTerlambat = k.KomTerlambat,
        NilaiAwal = k.NilaiAwal,
        DurasiKontrakHari = k.DurasiKontrakHari,
        ProgressPlan = k.ProgressPlan,
        ProgressActual = k.ProgressActual,
        AktivitasSaatIni = k.AktivitasSaatIni,
        Kendala = k.Kendala,
        Disiplin = k.Disiplin,
        TkdnPercentage = k.TkdnPercentage,
        TanggalLkp = k.TanggalLkp,
        HasAmendment = k.HasAmendment,
        NoAmandemen = k.NoAmandemen,
        TanggalAmandemen = k.TanggalAmandemen,
        JenisAmandemen = k.JenisAmandemen,
        NilaiKontrakBaru = k.NilaiKontrakBaru,
        DurasiAmandemen = k.DurasiAmandemen,
        TanggalMulaiBaru = k.TanggalMulaiBaru,
        TanggalSelesaiBaru = k.TanggalSelesaiBaru,
        AlasanPerubahan = k.AlasanPerubahan,
        ContractDocuments = k.ContractDocuments,
        AmendmentDocuments = k.AmendmentDocuments,
        SCurveData = k.SCurveData,
        CreatedAt = k.CreatedAt,
        UpdatedAt = k.UpdatedAt,
        TanggalMpl = k.TanggalMpl,
        TanggalMpa = k.TanggalMpa,
        MasaPemeliharaanHari = k.MasaPemeliharaanHari,
        Vendor = k.Vendor == null ? null : new VendorDto
        {
            IdVendor = k.Vendor.IdVendor,
            NamaVendor = k.Vendor.NamaVendor,
            Alamat = k.Vendor.Alamat,
            Npwp = k.Vendor.Npwp,
            PicNama = k.Vendor.PicNama,
            PicKontak = k.Vendor.PicKontak
        }
    };
}