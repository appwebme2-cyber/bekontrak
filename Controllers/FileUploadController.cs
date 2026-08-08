using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RefineryContractAPI.Services;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileUploadController : ControllerBase
{
    private readonly R2StorageService _r2;
    private readonly FileTokenService _fileTokens;

    public FileUploadController(R2StorageService r2, FileTokenService fileTokens)
    {
        _r2 = r2;
        _fileTokens = fileTokens;
    }

    // ==================== UPLOAD ====================
    [HttpPost]
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

        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "Ukuran file maksimal 20MB" });

        var ext        = Path.GetExtension(file.FileName);
        var safeFolder = string.IsNullOrWhiteSpace(folder) ? "general" : folder.Trim('/');
        var key        = $"{safeFolder}/{Guid.NewGuid()}{ext}";

        using var stream = file.OpenReadStream();
        await _r2.UploadAsync(stream, key, file.ContentType);

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
    public IActionResult GetDownloadToken([FromQuery] string key)
    {
        if (string.IsNullOrEmpty(key)) return BadRequest();
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

        await _r2.DeleteAsync(key);
        return Ok(new { message = "File berhasil dihapus" });
    }
}
