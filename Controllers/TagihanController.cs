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
public class TagihanController : ControllerBase
{
    private readonly AppDbContext _context;
    public TagihanController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.Tagihans
            .Include(t => t.Kontrak).ThenInclude(k => k!.Vendor)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new TagihanDto
            {
                IdTagihan = t.IdTagihan, IdKontrak = t.IdKontrak,
                NomorTagihan = t.NomorTagihan, TanggalTagihan = t.TanggalTagihan,
                TipeKontrak = t.TipeKontrak, Termin = t.Termin,
                NilaiTagihan = t.NilaiTagihan, StatusTagihan = t.StatusTagihan,
                MemoRequired = t.MemoRequired, TanggalPengirimanMemo = t.TanggalPengirimanMemo,
                DokumenTagihan = t.DokumenTagihan, DokumenMemo = t.DokumenMemo,
                Catatan = t.Catatan, CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt,
                Kontrak = t.Kontrak == null ? null : new KontrakSummaryDto
                {
                    IdKontrak = t.Kontrak.IdKontrak, JudulKontrak = t.Kontrak.JudulKontrak,
                    TipeKontrak = t.Kontrak.TipeKontrak,
                    Vendor = t.Kontrak.Vendor == null ? null : new VendorDto
                    { IdVendor = t.Kontrak.Vendor.IdVendor, NamaVendor = t.Kontrak.Vendor.NamaVendor }
                }
            }).ToListAsync();
        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var t = await _context.Tagihans
            .Include(t => t.Kontrak).ThenInclude(k => k!.Vendor)
            .FirstOrDefaultAsync(t => t.IdTagihan == id);
        if (t == null) return NotFound();
        return Ok(new TagihanDto
        {
            IdTagihan = t.IdTagihan, IdKontrak = t.IdKontrak,
            NomorTagihan = t.NomorTagihan, TanggalTagihan = t.TanggalTagihan,
            TipeKontrak = t.TipeKontrak, Termin = t.Termin,
            NilaiTagihan = t.NilaiTagihan, StatusTagihan = t.StatusTagihan,
            MemoRequired = t.MemoRequired, TanggalPengirimanMemo = t.TanggalPengirimanMemo,
            DokumenTagihan = t.DokumenTagihan, DokumenMemo = t.DokumenMemo,
            Catatan = t.Catatan, CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt,
            Kontrak = t.Kontrak == null ? null : new KontrakSummaryDto
            {
                IdKontrak = t.Kontrak.IdKontrak, JudulKontrak = t.Kontrak.JudulKontrak,
                TipeKontrak = t.Kontrak.TipeKontrak,
                Vendor = t.Kontrak.Vendor == null ? null : new VendorDto
                {
                    IdVendor = t.Kontrak.Vendor.IdVendor, NamaVendor = t.Kontrak.Vendor.NamaVendor,
                    Alamat = t.Kontrak.Vendor.Alamat, PicNama = t.Kontrak.Vendor.PicNama,
                    PicKontak = t.Kontrak.Vendor.PicKontak
                }
            }
        });
    }

    [HttpGet("kontrak/{idKontrak}")]
    public async Task<IActionResult> GetByKontrak(string idKontrak)
    {
        var list = await _context.Tagihans
            .Where(t => t.IdKontrak == idKontrak).OrderBy(t => t.CreatedAt)
            .Select(t => new TagihanDto
            {
                IdTagihan = t.IdTagihan, IdKontrak = t.IdKontrak,
                NomorTagihan = t.NomorTagihan, TanggalTagihan = t.TanggalTagihan,
                TipeKontrak = t.TipeKontrak, Termin = t.Termin,
                NilaiTagihan = t.NilaiTagihan, StatusTagihan = t.StatusTagihan,
                MemoRequired = t.MemoRequired, TanggalPengirimanMemo = t.TanggalPengirimanMemo,
                DokumenTagihan = t.DokumenTagihan, DokumenMemo = t.DokumenMemo,
                Catatan = t.Catatan, CreatedAt = t.CreatedAt, UpdatedAt = t.UpdatedAt,
            }).ToListAsync();
        return Ok(list);
    }

    // ===================== GET SLA TAGIHAN =====================
    [HttpGet("{id}/sla")]
    public async Task<IActionResult> GetSla(string id)
    {
        var sla = await _context.SlaTagihans.FirstOrDefaultAsync(s => s.IdTagihan == id);
        if (sla == null) return NotFound(new { message = "Data SLA tagihan belum ada" });
        return Ok(MapSlaToDto(sla));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTagihanDto dto)
    {
        var tagihan = new Tagihan
        {
            IdKontrak = dto.IdKontrak, NomorTagihan = dto.NomorTagihan,
            TanggalTagihan = dto.TanggalTagihan, TipeKontrak = dto.TipeKontrak,
            Termin = dto.Termin, NilaiTagihan = dto.NilaiTagihan,
            StatusTagihan = dto.StatusTagihan, MemoRequired = dto.MemoRequired,
            TanggalPengirimanMemo = dto.TanggalPengirimanMemo,
            DokumenTagihan = dto.DokumenTagihan, DokumenMemo = dto.DokumenMemo,
            Catatan = dto.Catatan
        };
        _context.Tagihans.Add(tagihan);
        await _context.SaveChangesAsync();

        // ===== Buat baris SLA + cap tgl_masuk untuk status awal =====
        var now = DateTime.UtcNow;
        var sla = new SlaTagihan
        {
            IdKontrak = tagihan.IdKontrak,
            IdTagihan = tagihan.IdTagihan,
            CreatedAt = now,
            UpdatedAt = now
        };
        var kodeAwal = NormalizeKode(tagihan.StatusTagihan);
        if (kodeAwal != null)
            SetTglMasuk(sla, kodeAwal, now);

        _context.SlaTagihans.Add(sla);
        await _context.SaveChangesAsync();

        return Ok(tagihan);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateTagihanDto dto)
    {
        var tagihan = await _context.Tagihans.FindAsync(id);
        if (tagihan == null) return NotFound();

        // Simpan status lama SEBELUM ditimpa
        var statusLama = tagihan.StatusTagihan;

        tagihan.NomorTagihan = dto.NomorTagihan;
        tagihan.TanggalTagihan = dto.TanggalTagihan;
        tagihan.TipeKontrak = dto.TipeKontrak;
        tagihan.Termin = dto.Termin;
        tagihan.NilaiTagihan = dto.NilaiTagihan;
        tagihan.StatusTagihan = dto.StatusTagihan;
        tagihan.MemoRequired = dto.MemoRequired;
        tagihan.TanggalPengirimanMemo = dto.TanggalPengirimanMemo;
        tagihan.DokumenTagihan = dto.DokumenTagihan;
        tagihan.DokumenMemo = dto.DokumenMemo;
        tagihan.Catatan = dto.Catatan;
        tagihan.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // ===== Cap tanggal kalau status berubah =====
        if (!string.Equals(statusLama, dto.StatusTagihan, StringComparison.OrdinalIgnoreCase))
        {
            await StampPerubahanStatus(tagihan, statusLama, dto.StatusTagihan);
        }

        return Ok(tagihan);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var tagihan = await _context.Tagihans.FindAsync(id);
        if (tagihan == null) return NotFound();

        // Hapus juga baris SLA terkait (kalau ada)
        var sla = await _context.SlaTagihans.FirstOrDefaultAsync(s => s.IdTagihan == id);
        if (sla != null) _context.SlaTagihans.Remove(sla);

        _context.Tagihans.Remove(tagihan);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Tagihan berhasil dihapus" });
    }

    // ============================================================
    //  Helper SLA Tagihan
    // ============================================================

    private async Task StampPerubahanStatus(Tagihan tagihan, string? statusLama, string? statusBaru)
    {
        var sla = await _context.SlaTagihans.FirstOrDefaultAsync(s => s.IdTagihan == tagihan.IdTagihan);
        var now = DateTime.UtcNow;

        if (sla == null)
        {
            sla = new SlaTagihan
            {
                IdKontrak = tagihan.IdKontrak,
                IdTagihan = tagihan.IdTagihan,
                CreatedAt = now,
                UpdatedAt = now
            };
            _context.SlaTagihans.Add(sla);
        }

        var kodeLama = NormalizeKode(statusLama);
        var kodeBaru = NormalizeKode(statusBaru);

        // Tutup tahap lama (cap tgl_selesai) dan buka tahap baru (cap tgl_masuk)
        if (kodeLama != null) SetTglSelesai(sla, kodeLama, now);
        if (kodeBaru != null) SetTglMasuk(sla, kodeBaru, now);

        sla.UpdatedAt = now;
        await _context.SaveChangesAsync();
    }

    // Normalisasi status_tagihan → kode_tahap kanonik.
    // VERIFIKASI dulu daftar ini terhadap nilai status_tagihan yang sebenarnya dipakai.
    private static string? NormalizeKode(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return null;
        var s = status.Trim().ToUpperInvariant();
        return s switch
        {
            "BA JOINT INSPECTION" or "BA_JOINT_INSPECTION" => "BA_JOINT_INSPECTION",
            "BA COMMISSIONING" or "BA_COMMISSIONING" => "BA_COMMISSIONING",
            "BA PENERIMAAN MATERIAL" or "BA_PENERIMAAN_MATERIAL" => "BA_PENERIMAAN_MATERIAL",
            "LKP" => "LKP",
            "PERHITUNGAN" or "PERHITUNGAN TAGIHAN" or "PERHITUNGAN_TAGIHAN" => "PERHITUNGAN",
            "BASTP" or "BAST" => "BASTP",
            "BAKP" or "BAPP" or "BAKP/BAPP" => "BAKP",
            "IVENDOR" or "I-VENDOR" or "SUBMIT I-VENDOR" or "SUBMIT IVENDOR" => "IVENDOR",
            "SA" => "SA",
            "PA" => "PA",
            "VERIFIKASI" or "VERIFICATION" => "VERIFIKASI",
            "PAYMENT" or "SELESAI" or "PAYMENT/SELESAI" or "PAYMENT / SELESAI" => "PAYMENT",
            _ => null
        };
    }

    private static void SetTglMasuk(SlaTagihan sla, string kode, DateTime when)
    {
        switch (kode)
        {
            case "BA_JOINT_INSPECTION":    sla.TglMasukBaJointInspection    ??= when; break;
            case "BA_COMMISSIONING":       sla.TglMasukBaCommissioning       ??= when; break;
            case "BA_PENERIMAAN_MATERIAL": sla.TglMasukBaPenerimaanMaterial ??= when; break;
            case "LKP":                    sla.TglMasukLkp                   ??= when; break;
            case "PERHITUNGAN":            sla.TglMasukPerhitungan           ??= when; break;
            case "BASTP":                  sla.TglMasukBast                  ??= when; break;
            case "BAKP":                   sla.TglMasukBakp                  ??= when; break;
            case "IVENDOR":                sla.TglMasukIvendor               ??= when; break;
            case "SA":                     sla.TglMasukSa                    ??= when; break;
            case "PA":                     sla.TglMasukPa                    ??= when; break;
            case "VERIFIKASI":             sla.TglMasukVerifikasi            ??= when; break;
            case "PAYMENT":                sla.TglMasukPayment               ??= when; break;
        }
    }

    private static void SetTglSelesai(SlaTagihan sla, string kode, DateTime when)
    {
        switch (kode)
        {
            case "BA_JOINT_INSPECTION":    sla.TglSelesaiBaJointInspection    ??= when; break;
            case "BA_COMMISSIONING":       sla.TglSelesaiBaCommissioning       ??= when; break;
            case "BA_PENERIMAAN_MATERIAL": sla.TglSelesaiBaPenerimaanMaterial ??= when; break;
            case "LKP":                    sla.TglSelesaiLkp                   ??= when; break;
            case "PERHITUNGAN":            sla.TglSelesaiPerhitungan           ??= when; break;
            case "BASTP":                  sla.TglSelesaiBast                  ??= when; break;
            case "BAKP":                   sla.TglSelesaiBakp                  ??= when; break;
            case "IVENDOR":                sla.TglSelesaiIvendor               ??= when; break;
            case "SA":                     sla.TglSelesaiSa                    ??= when; break;
            case "PA":                     sla.TglSelesaiPa                    ??= when; break;
            case "VERIFIKASI":             sla.TglSelesaiVerifikasi            ??= when; break;
            case "PAYMENT":                sla.TglSelesaiPayment               ??= when; break;
        }
    }

    private static SlaTagihanDto MapSlaToDto(SlaTagihan s) => new()
    {
        Id = s.Id, IdKontrak = s.IdKontrak, IdTagihan = s.IdTagihan,
        TglMasukBaJointInspection = s.TglMasukBaJointInspection, TglSelesaiBaJointInspection = s.TglSelesaiBaJointInspection,
        TglMasukBaCommissioning = s.TglMasukBaCommissioning, TglSelesaiBaCommissioning = s.TglSelesaiBaCommissioning,
        TglMasukBaPenerimaanMaterial = s.TglMasukBaPenerimaanMaterial, TglSelesaiBaPenerimaanMaterial = s.TglSelesaiBaPenerimaanMaterial,
        TglMasukLkp = s.TglMasukLkp, TglSelesaiLkp = s.TglSelesaiLkp,
        TglMasukPerhitungan = s.TglMasukPerhitungan, TglSelesaiPerhitungan = s.TglSelesaiPerhitungan,
        TglMasukBast = s.TglMasukBast, TglSelesaiBast = s.TglSelesaiBast,
        TglMasukBakp = s.TglMasukBakp, TglSelesaiBakp = s.TglSelesaiBakp,
        TglMasukIvendor = s.TglMasukIvendor, TglSelesaiIvendor = s.TglSelesaiIvendor,
        TglMasukSa = s.TglMasukSa, TglSelesaiSa = s.TglSelesaiSa,
        TglMasukPa = s.TglMasukPa, TglSelesaiPa = s.TglSelesaiPa,
        TglMasukVerifikasi = s.TglMasukVerifikasi, TglSelesaiVerifikasi = s.TglSelesaiVerifikasi,
        TglMasukPayment = s.TglMasukPayment, TglSelesaiPayment = s.TglSelesaiPayment,
        CreatedAt = s.CreatedAt, UpdatedAt = s.UpdatedAt
    };
}