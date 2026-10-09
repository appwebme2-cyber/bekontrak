using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;
using RefineryContractAPI.Services;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MaterialRequirementDraftsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly AiExtractionService _ai;
    private readonly IConfiguration _config;
    public MaterialRequirementDraftsController(AppDbContext context, AiExtractionService ai, IConfiguration config)
    {
        _context = context;
        _ai = ai;
        _config = config;
    }

    // Pembatasan percobaan PIN per user: 5 kali salah berturut-turut mengunci 10 menit,
    // supaya PIN 6 digit tidak bisa ditebak lewat panggilan API berulang.
    private const int MaxPinFails = 5;
    private static readonly TimeSpan PinLockDuration = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<string, (int Fails, DateTime LockedUntil)> PinAttempts = new();

    private static bool PinMatches(string? provided, string expected)
    {
        var a = Encoding.UTF8.GetBytes(provided ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

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

    [HttpPost("{id}/extract-ai")]
    public async Task<IActionResult> ExtractAi(string id, [FromBody] ExtractAiRequestDto dto)
    {
        try
        {
            // PIN dibaca dari env variable "PIN" di server; tanpa PIN terkonfigurasi fitur ditolak (fail closed)
            var expectedPin = _config["PIN"];
            if (string.IsNullOrEmpty(expectedPin))
                return StatusCode(503, new { message = "PIN belum dikonfigurasi di server. Hubungi admin untuk menambahkan environment variable PIN." });

            var userKey = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
            if (PinAttempts.TryGetValue(userKey, out var attempt) && attempt.LockedUntil > DateTime.UtcNow)
            {
                var minutes = Math.Ceiling((attempt.LockedUntil - DateTime.UtcNow).TotalMinutes);
                return StatusCode(429, new { message = $"Terlalu banyak PIN salah. Coba lagi dalam {minutes} menit." });
            }

            if (!PinMatches(dto?.Pin, expectedPin))
            {
                PinAttempts.AddOrUpdate(
                    userKey,
                    _ => (1, DateTime.MinValue),
                    (_, old) =>
                    {
                        var fails = (old.LockedUntil > DateTime.MinValue && old.LockedUntil <= DateTime.UtcNow) ? 1 : old.Fails + 1;
                        return fails >= MaxPinFails ? (0, DateTime.UtcNow.Add(PinLockDuration)) : (fails, DateTime.MinValue);
                    });
                return StatusCode(403, new { message = "PIN salah." });
            }
            PinAttempts.TryRemove(userKey, out _);

            var draft = await _context.MaterialRequirementDrafts.FindAsync(id);
            if (draft == null) return NotFound();

            var documents = new List<AiDocumentRef>();
            documents.AddRange(ParseDocumentsForAi(draft.RekomendasiDocuments));
            documents.AddRange(ParseDocumentsForAi(draft.GambarKerjaDocuments));

            if (documents.Count == 0)
                return BadRequest(new { message = "Belum ada dokumen Rekomendasi/Gambar Kerja yang diupload." });

            var existingRabItems = await _context.RabItems
                .Where(r => r.IdKontrak == draft.IdKontrak)
                .OrderBy(r => r.CreatedAt)
                .Select(r => new AiRabItemContext(r.IdRabItem, r.KodeItem, r.UraianPekerjaan, r.Satuan))
                .ToListAsync();

            var result = await _ai.ExtractAsync(documents, existingRabItems);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal ekstrak dengan AI: {DescribeException(ex)}" });
        }
    }

    // Dokumen tersimpan sebagai JSON string array { id, name, size, type, url } di
    // kolom RekomendasiDocuments/GambarKerjaDocuments (pola sama seperti ContractDocuments).
    // url berbentuk ".../api/FileUpload/file/{key}" - resolve key-nya di sini supaya
    // AiExtractionService bisa ambil bytes langsung dari R2 (server-to-server, tanpa token).
    private static List<AiDocumentRef> ParseDocumentsForAi(string? documentsJson)
    {
        var result = new List<AiDocumentRef>();
        if (string.IsNullOrWhiteSpace(documentsJson)) return result;

        try
        {
            using var doc = JsonDocument.Parse(documentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;

            const string marker = "/api/FileUpload/file/";
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var url = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
                var type = item.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(type)) continue;

                var idx = url.IndexOf(marker, StringComparison.Ordinal);
                if (idx < 0) continue;

                var key = url[(idx + marker.Length)..];
                result.Add(new AiDocumentRef(key, type));
            }
        }
        catch (JsonException)
        {
            // dokumen tersimpan dalam format tak terduga - lewati saja, bukan fatal
        }

        return result;
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
