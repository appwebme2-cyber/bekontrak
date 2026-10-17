using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.Services;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileUploadController : ControllerBase
{
    private readonly R2StorageService _r2;
    private readonly FileTokenService _fileTokens;
    private readonly AppDbContext _context;
    private readonly ICurrentRu _currentRu;

    public FileUploadController(R2StorageService r2, FileTokenService fileTokens, AppDbContext context, ICurrentRu currentRu)
    {
        _r2 = r2;
        _fileTokens = fileTokens;
        _context = context;
        _currentRu = currentRu;
    }

    // ==================== UPLOAD ====================
    [HttpPost]
    [RequestSizeLimit(100 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 100 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] string folder, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });

        var allowedTypes = new[]
        {
            "application/pdf", "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "image/jpeg", "image/png", "image/jpg"
        };

        if (!allowedTypes.Contains(file.ContentType))
            return BadRequest(new { message = "Tipe file tidak diizinkan" });

        if (file.Length > 100 * 1024 * 1024)
            return BadRequest(new { message = "Ukuran file maksimal 100MB" });

        var ext        = Path.GetExtension(file.FileName);
        var safeFolder = string.IsNullOrWhiteSpace(folder) ? "general" : folder.Trim('/');
        var key        = $"{safeFolder}/{Guid.NewGuid()}{ext}";

        try
        {
            using var stream = file.OpenReadStream();
            await _r2.UploadAsync(stream, key, file.ContentType);
        }
        catch (Amazon.S3.AmazonS3Exception ex)
        {
            return StatusCode(500, new { message = $"R2 error: {ex.ErrorCode} - {ex.Message}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Upload failed: {ex.GetType().Name} - {ex.Message}" });
        }

        var url = $"/api/FileUpload/file/{key}";

        return Ok(new
        {
            url,
            key,
            name = file.FileName,
            size = file.Length,
            type = file.ContentType
        });
    }

    // ==================== DOWNLOAD TOKEN ====================
    [HttpGet("download-token")]
    public async Task<IActionResult> GetDownloadToken([FromQuery] string key)
    {
        if (string.IsNullOrEmpty(key)) return BadRequest();
        if (!await BolehDiaksesAsync(key))
            return NotFound(new { message = "Dokumen tidak ditemukan di RU Anda." });
        var token = _fileTokens.CreateToken(key);
        return Ok(new { token });
    }

    // ==================== SERVE (proxy) ====================
    [HttpGet("file/{*key}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetFile(string key, [FromQuery] string t)
    {
        if (string.IsNullOrEmpty(key)) return BadRequest();

        if (string.IsNullOrEmpty(t) || !_fileTokens.IsValid(t, key))
            return Unauthorized(new { message = "Token akses tidak valid atau sudah kadaluarsa" });

        try
        {
            var (stream, contentType, contentLength) = await _r2.GetAsync(key);
            Response.ContentLength = contentLength;
            return File(stream, contentType, enableRangeProcessing: true);
        }
        catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NoContent
                                                   || ex.StatusCode == System.Net.HttpStatusCode.NotFound
                                                   || ex.ErrorCode == "NoSuchKey")
        {
            return NotFound();
        }
    }

    // ==================== DELETE ====================
    [HttpDelete]
    public async Task<IActionResult> Delete([FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path)) return BadRequest();

        var marker = "/api/FileUpload/file/";
        var key = path.Contains(marker)
            ? path[(path.IndexOf(marker) + marker.Length)..]
            : path.TrimStart('/');

        if (!await BolehDiaksesAsync(key))
            return NotFound(new { message = "Dokumen tidak ditemukan di RU Anda." });

        await _r2.DeleteAsync(key);
        return Ok(new { message = "File berhasil dihapus" });
    }

    // ==================== MULTI-RU ====================
    // Dokumen yang tercatat di data RU lain tidak boleh dibuka/dihapus. File yang belum tercatat di
    // data mana pun (baru diupload, formulir belum disimpan) tetap boleh, supaya pratinjau saat
    // mengisi formulir tetap jalan. Super Admin/Head Office saat "Semua RU" boleh semua.
    private async Task<bool> BolehDiaksesAsync(string key)
    {
        if (_currentRu.Filter == null) return true;

        // Escape karakter khusus LIKE (\ % _) supaya nama file dicari apa adanya
        var pola = "%" + key.TrimStart('/').Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
        if (await DipakaiAsync(pola, semuaRu: false)) return true;   // tercatat di data RU ini
        return !await DipakaiAsync(pola, semuaRu: true);             // tidak tercatat di data RU mana pun
    }

    private async Task<bool> DipakaiAsync(string pola, bool semuaRu)
    {
        var kontrak = semuaRu ? _context.Kontraks.IgnoreQueryFilters() : _context.Kontraks;
        if (await kontrak.AnyAsync(k => (k.ContractDocuments != null && EF.Functions.Like(k.ContractDocuments, pola))
                                     || (k.AmendmentDocuments != null && EF.Functions.Like(k.AmendmentDocuments, pola))))
            return true;

        var amandemen = semuaRu ? _context.AmandemenKontraks.IgnoreQueryFilters() : _context.AmandemenKontraks;
        if (await amandemen.AnyAsync(a => a.AmendmentDocuments != null && EF.Functions.Like(a.AmendmentDocuments, pola)))
            return true;

        var tagihan = semuaRu ? _context.Tagihans.IgnoreQueryFilters() : _context.Tagihans;
        if (await tagihan.AnyAsync(t => (t.DokumenTagihan != null && EF.Functions.Like(t.DokumenTagihan, pola))
                                     || (t.DokumenMemo != null && EF.Functions.Like(t.DokumenMemo, pola))))
            return true;

        var padi = semuaRu ? _context.Padis.IgnoreQueryFilters() : _context.Padis;
        if (await padi.AnyAsync(p => p.DokumenPendukung != null && EF.Functions.Like(p.DokumenPendukung, pola)))
            return true;

        var dokumen = semuaRu ? _context.DokumenApprovals.IgnoreQueryFilters() : _context.DokumenApprovals;
        if (await dokumen.AnyAsync(d => EF.Functions.Like(d.FilePath, pola)
                                     || (d.FileUrl != null && EF.Functions.Like(d.FileUrl, pola))))
            return true;

        var draft = semuaRu ? _context.MaterialRequirementDrafts.IgnoreQueryFilters() : _context.MaterialRequirementDrafts;
        return await draft.AnyAsync(d => (d.RekomendasiDocuments != null && EF.Functions.Like(d.RekomendasiDocuments, pola))
                                      || (d.GambarKerjaDocuments != null && EF.Functions.Like(d.GambarKerjaDocuments, pola)));
    }
}
