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
public class MaterialRequirementDraftsController : ControllerBase
{
    private readonly AppDbContext _context;
    public MaterialRequirementDraftsController(AppDbContext context) => _context = context;

    private static KontrakSummaryDto? ToKontrakSummary(Kontrak? k)
    {
        if (k == null) return null;
        return new KontrakSummaryDto
        {
            IdKontrak = k.IdKontrak,
            JudulKontrak = k.JudulKontrak,
            TipeKontrak = k.TipeKontrak,
            StatusKontrak = k.StatusKontrak,
            NilaiAwal = k.NilaiAwal,
            NilaiKontrakBaru = k.NilaiKontrakBaru,
            HasAmendment = k.HasAmendment,
            DireksiPekerjaan = k.DireksiPekerjaan,
            TanggalMulai = k.TanggalMulai,
            TanggalSelesai = k.TanggalSelesai,
            Vendor = k.Vendor == null ? null : new VendorDto
            {
                IdVendor = k.Vendor.IdVendor,
                NamaVendor = k.Vendor.NamaVendor
            }
        };
    }

    private static MaterialRequirementLineDto ToLineDto(MaterialRequirementLine l) => new MaterialRequirementLineDto
    {
        IdLine = l.IdLine,
        IdDraft = l.IdDraft,
        Jenis = l.Jenis,
        IdRabItem = l.IdRabItem,
        KodeItemSnapshot = l.KodeItemSnapshot,
        UraianPekerjaan = l.UraianPekerjaan,
        Satuan = l.Satuan,
        VolumeKalkulasi = l.VolumeKalkulasi,
        CatatanKalkulasi = l.CatatanKalkulasi,
        VolumeKlaim = l.VolumeKlaim,
        Urutan = l.Urutan,
        CreatedAt = l.CreatedAt,
        UpdatedAt = l.UpdatedAt
    };

    private static MaterialRequirementDraftDto ToDto(MaterialRequirementDraft d, bool includeLines = false) => new MaterialRequirementDraftDto
    {
        IdDraft = d.IdDraft,
        IdKontrak = d.IdKontrak,
        NomorMrf = d.NomorMrf,
        TagUnit = d.TagUnit,
        LokasiArea = d.LokasiArea,
        TanggalRekomendasi = d.TanggalRekomendasi,
        Problem = d.Problem,
        RekomendasiSolusi = d.RekomendasiSolusi,
        Status = d.Status,
        RekomendasiDocuments = d.RekomendasiDocuments,
        GambarKerjaDocuments = d.GambarKerjaDocuments,
        Catatan = d.Catatan,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        Kontrak = ToKontrakSummary(d.Kontrak),
        Lines = includeLines ? d.Lines.OrderBy(l => l.Urutan).ThenBy(l => l.CreatedAt).Select(ToLineDto).ToList() : new List<MaterialRequirementLineDto>()
    };

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? idKontrak)
    {
        var query = _context.MaterialRequirementDrafts
            .Include(d => d.Kontrak).ThenInclude(k => k!.Vendor)
            .AsQueryable();

        if (!string.IsNullOrEmpty(idKontrak))
            query = query.Where(d => d.IdKontrak == idKontrak);

        var drafts = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();
        return Ok(drafts.Select(d => ToDto(d)));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var draft = await _context.MaterialRequirementDrafts
            .Include(d => d.Kontrak).ThenInclude(k => k!.Vendor)
            .Include(d => d.Lines)
            .FirstOrDefaultAsync(d => d.IdDraft == id);

        if (draft == null) return NotFound();
        return Ok(ToDto(draft, includeLines: true));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMaterialRequirementDraftDto dto)
    {
        try
        {
            var draft = new MaterialRequirementDraft
            {
                IdKontrak = dto.IdKontrak,
                NomorMrf = dto.NomorMrf,
                TagUnit = dto.TagUnit,
                LokasiArea = dto.LokasiArea,
                TanggalRekomendasi = dto.TanggalRekomendasi,
                Problem = dto.Problem,
                RekomendasiSolusi = dto.RekomendasiSolusi,
                Status = string.IsNullOrEmpty(dto.Status) ? "Draft" : dto.Status,
                RekomendasiDocuments = dto.RekomendasiDocuments,
                GambarKerjaDocuments = dto.GambarKerjaDocuments,
                Catatan = dto.Catatan
            };

            _context.MaterialRequirementDrafts.Add(draft);
            await _context.SaveChangesAsync();

            var saved = await _context.MaterialRequirementDrafts
                .Include(d => d.Kontrak).ThenInclude(k => k!.Vendor)
                .FirstAsync(d => d.IdDraft == draft.IdDraft);

            return Ok(ToDto(saved));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal membuat draft kebutuhan: {DescribeException(ex)}" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateMaterialRequirementDraftDto dto)
    {
        try
        {
            var draft = await _context.MaterialRequirementDrafts.FindAsync(id);
            if (draft == null) return NotFound();

            draft.NomorMrf = dto.NomorMrf;
            draft.TagUnit = dto.TagUnit;
            draft.LokasiArea = dto.LokasiArea;
            draft.TanggalRekomendasi = dto.TanggalRekomendasi;
            draft.Problem = dto.Problem;
            draft.RekomendasiSolusi = dto.RekomendasiSolusi;
            draft.Status = string.IsNullOrEmpty(dto.Status) ? draft.Status : dto.Status;
            draft.RekomendasiDocuments = dto.RekomendasiDocuments;
            draft.GambarKerjaDocuments = dto.GambarKerjaDocuments;
            draft.Catatan = dto.Catatan;
            draft.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var saved = await _context.MaterialRequirementDrafts
                .Include(d => d.Kontrak).ThenInclude(k => k!.Vendor)
                .FirstAsync(d => d.IdDraft == draft.IdDraft);

            return Ok(ToDto(saved));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal memperbarui draft kebutuhan: {DescribeException(ex)}" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        try
        {
            var draft = await _context.MaterialRequirementDrafts
                .Include(d => d.Lines)
                .FirstOrDefaultAsync(d => d.IdDraft == id);
            if (draft == null) return NotFound();

            _context.MaterialRequirementLines.RemoveRange(draft.Lines);
            _context.MaterialRequirementDrafts.Remove(draft);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Draft kebutuhan berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal menghapus draft kebutuhan: {DescribeException(ex)}" });
        }
    }

    // Gabungkan pesan exception + inner exception (DbUpdateException dari EF
    // biasanya cuma bilang "see inner exception" tanpa detail aslinya)
    private static string DescribeException(Exception ex)
    {
        var parts = new List<string>();
        var current = ex;
        while (current != null)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
            current = current.InnerException;
        }
        return string.Join(" -> ", parts);
    }
}
