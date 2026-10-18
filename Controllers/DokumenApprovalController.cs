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
public class DokumenApprovalController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly PushNotifier _push;
    private readonly R2StorageService _r2;

    public DokumenApprovalController(AppDbContext context, IWebHostEnvironment env, PushNotifier push, R2StorageService r2)
    {
        _push = push;
        _r2 = r2;
        _context = context;
        _env = env;
    }

    // GET: semua dokumen (untuk approval dashboard)
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? status, [FromQuery] string? idKontrak)
    {
        var query = _context.DokumenApprovals
            .Include(d => d.Kontrak)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status))
            query = query.Where(d => d.StatusApproval == status);

        if (!string.IsNullOrEmpty(idKontrak))
            query = query.Where(d => d.IdKontrak == idKontrak);

        var list = await query
            .OrderByDescending(d => d.CreatedAt)
            .Select(d => new DokumenApprovalDto
            {
                IdDokumen = d.IdDokumen,
                IdKontrak = d.IdKontrak,
                TipeDokumen = d.TipeDokumen,
                NamaDokumen = d.NamaDokumen,
                DeskripsiDokumen = d.DeskripsiDokumen,
                FileUrl = d.FileUrl,
                NamaFile = d.NamaFile,
                TipeFile = d.TipeFile,
                UkuranFile = d.UkuranFile,
                StatusApproval = d.StatusApproval,
                CatatanReviewer = d.CatatanReviewer,
                UploadedBy = d.UploadedBy,
                ReviewedBy = d.ReviewedBy,
                ReviewedAt = d.ReviewedAt,
                CreatedAt = d.CreatedAt,
                JudulKontrak = d.Kontrak != null ? d.Kontrak.JudulKontrak : null
            })
            .ToListAsync();

        return Ok(list);
    }

    // GET: dokumen by ID
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var d = await _context.DokumenApprovals
            .Include(d => d.Kontrak)
            .FirstOrDefaultAsync(d => d.IdDokumen == id);

        if (d == null) return NotFound();

        return Ok(new DokumenApprovalDto
        {
            IdDokumen = d.IdDokumen,
            IdKontrak = d.IdKontrak,
            TipeDokumen = d.TipeDokumen,
            NamaDokumen = d.NamaDokumen,
            DeskripsiDokumen = d.DeskripsiDokumen,
            FileUrl = d.FileUrl,
            NamaFile = d.NamaFile,
            TipeFile = d.TipeFile,
            UkuranFile = d.UkuranFile,
            StatusApproval = d.StatusApproval,
            CatatanReviewer = d.CatatanReviewer,
            UploadedBy = d.UploadedBy,
            ReviewedBy = d.ReviewedBy,
            ReviewedAt = d.ReviewedAt,
            CreatedAt = d.CreatedAt,
            JudulKontrak = d.Kontrak != null ? d.Kontrak.JudulKontrak : null
        });
    }

    // POST: upload dokumen baru
    [HttpPost("upload")]
    public async Task<IActionResult> Upload([FromForm] string idKontrak,
        [FromForm] string tipeDokumen,
        [FromForm] string namaDokumen,
        [FromForm] string? deskripsiDokumen,
        [FromForm] string? uploadedBy,
        IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });

        // Validasi tipe file
        var allowedTypes = new[] { "application/pdf", "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "image/jpeg", "image/png" };

        if (!allowedTypes.Contains(file.ContentType))
            return BadRequest(new { message = "Tipe file tidak diizinkan. Gunakan PDF, Word, Excel, atau gambar." });

        // Kontrak harus ada di RU pengguna sebelum file diunggah (tidak meninggalkan file yatim di R2)
        if (!await _context.Kontraks.AnyAsync(k => k.IdKontrak == idKontrak))
            return NotFound(new { message = "Kontrak tidak ditemukan di RU Anda." });

        // Disimpan di R2 (sama dengan dokumen kontrak), bukan disk server yang hilang setiap deploy.
        // Dibuka lewat token unduh /api/FileUpload/file/{key} yang hanya diberikan untuk data di RU pengguna.
        var ext = Path.GetExtension(file.FileName);
        var key = $"dokumen-approval/{idKontrak}/{Guid.NewGuid()}{ext}";
        try
        {
            using var stream = file.OpenReadStream();
            await _r2.UploadAsync(stream, key, file.ContentType);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal menyimpan file: {ex.Message}" });
        }
        var filePath = key;
        var fileUrl = $"/api/FileUpload/file/{key}";

        var dokumen = new DokumenApproval
        {
            IdKontrak = idKontrak,
            TipeDokumen = tipeDokumen,
            NamaDokumen = namaDokumen,
            DeskripsiDokumen = deskripsiDokumen,
            FilePath = filePath,
            FileUrl = fileUrl,
            NamaFile = file.FileName,
            TipeFile = file.ContentType,
            UkuranFile = file.Length,
            StatusApproval = "Pending",
            UploadedBy = uploadedBy,
        };

        _context.DokumenApprovals.Add(dokumen);
        await _context.SaveChangesAsync();

        // Kabari penyetuju di RU kontrak ini lewat notifikasi HP (berjalan di latar belakang)
        _push.DokumenBaru(idKontrak, namaDokumen);

        return Ok(new { message = "Dokumen berhasil diupload", idDokumen = dokumen.IdDokumen, fileUrl });
    }

    // PUT: review (approve/reject)
    [HttpPut("{id}/review")]
    public async Task<IActionResult> Review(string id, [FromBody] ReviewDokumenDto dto)
    {
        var dokumen = await _context.DokumenApprovals.FindAsync(id);
        if (dokumen == null) return NotFound();

        if (dto.StatusApproval != "Approved" && dto.StatusApproval != "Rejected")
            return BadRequest(new { message = "Status harus Approved atau Rejected" });

        dokumen.StatusApproval = dto.StatusApproval;
        dokumen.CatatanReviewer = dto.CatatanReviewer;
        dokumen.ReviewedBy = dto.ReviewedBy;
        dokumen.ReviewedAt = DateTime.UtcNow;
        dokumen.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = $"Dokumen berhasil {dto.StatusApproval}" });
    }

    // DELETE: hapus dokumen
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var dokumen = await _context.DokumenApprovals.FindAsync(id);
        if (dokumen == null) return NotFound();

        // Hapus file: di R2 untuk unggahan baru, di disk untuk unggahan lama
        if (dokumen.FileUrl?.StartsWith("/api/FileUpload/file/") == true)
        {
            try { await _r2.DeleteAsync(dokumen.FilePath); }
            catch (Exception ex) { Console.WriteLine($"[DokumenApproval] Gagal hapus file R2 {dokumen.FilePath}: {ex.Message}"); }
        }
        else if (System.IO.File.Exists(dokumen.FilePath))
        {
            System.IO.File.Delete(dokumen.FilePath);
        }

        _context.DokumenApprovals.Remove(dokumen);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Dokumen berhasil dihapus" });
    }
}
