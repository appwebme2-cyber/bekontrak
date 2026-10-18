using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.Models;
using RefineryContractAPI.Services;

namespace RefineryContractAPI.Controllers;

/// <summary>
/// Unduhan aplikasi MAESTRO Mobile (APK Android) dengan alamat tetap.
/// File disimpan di R2; link unduhan EAS hanya berlaku 15 menit, jadi APK diunggah ke sini oleh Super Admin.
/// </summary>
[ApiController]
[Route("api/app")]
public class AppDownloadController : ControllerBase
{
    private const string KunciR2 = "apps/maestro-android.apk";
    private const string NamaSetting = "Mobile_App_Android";
    private const long BatasUkuran = 250L * 1024 * 1024;

    private readonly AppDbContext _context;
    private readonly R2StorageService _r2;

    public AppDownloadController(AppDbContext context, R2StorageService r2)
    {
        _context = context;
        _r2 = r2;
    }

    public class InfoApk
    {
        public string Versi { get; set; } = string.Empty;
        public long Ukuran { get; set; }
        public string? Catatan { get; set; }
        public DateTime DiunggahPada { get; set; }
        public string? DiunggahOleh { get; set; }
    }

    private async Task<InfoApk?> BacaInfo()
    {
        var nilai = await _context.KonfigurasiSistems
            .Where(k => k.NamaSetting == NamaSetting)
            .Select(k => k.NilaiSetting)
            .FirstOrDefaultAsync();
        if (string.IsNullOrEmpty(nilai)) return null;
        try { return JsonSerializer.Deserialize<InfoApk>(nilai); }
        catch { return null; }
    }

    // Info versi untuk halaman unduh (publik)
    [HttpGet("android/info")]
    [AllowAnonymous]
    public async Task<IActionResult> Info()
    {
        var info = await BacaInfo();
        if (info == null) return Ok(new { tersedia = false });
        return Ok(new { tersedia = true, versi = info.Versi, ukuran = info.Ukuran, catatan = info.Catatan, diunggahPada = info.DiunggahPada });
    }

    // Unduh APK terbaru (publik, alamat tetap)
    [HttpGet("android")]
    [AllowAnonymous]
    public async Task<IActionResult> Unduh()
    {
        var info = await BacaInfo();
        if (info == null) return NotFound(new { message = "APK MAESTRO Mobile belum tersedia." });
        try
        {
            var (stream, _, panjang) = await _r2.GetAsync(KunciR2);
            if (panjang > 0) Response.ContentLength = panjang;
            var nama = $"MAESTRO-{(string.IsNullOrWhiteSpace(info.Versi) ? "android" : info.Versi)}.apk";
            return File(stream, "application/vnd.android.package-archive", nama, enableRangeProcessing: true);
        }
        catch (Amazon.S3.AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound || ex.ErrorCode == "NoSuchKey")
        {
            return NotFound(new { message = "APK MAESTRO Mobile belum tersedia." });
        }
    }

    // Unggah APK versi baru (Super Admin)
    [HttpPost("android")]
    [Authorize(Roles = "admin")]
    [SuperAdminOnly]
    [RequestSizeLimit(250L * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 250L * 1024 * 1024)]
    public async Task<IActionResult> Unggah(IFormFile file, [FromForm] string versi, [FromForm] string? catatan)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File APK tidak boleh kosong." });
        if (!file.FileName.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "File harus berformat .apk." });
        if (file.Length > BatasUkuran)
            return BadRequest(new { message = "Ukuran APK maksimal 250 MB." });
        if (string.IsNullOrWhiteSpace(versi))
            return BadRequest(new { message = "Versi wajib diisi, mis. 1.0.0." });

        // File APK (ZIP) selalu diawali "PK"
        using (var cek = file.OpenReadStream())
        {
            var awal = new byte[2];
            if (await cek.ReadAsync(awal.AsMemory(0, 2)) < 2 || awal[0] != (byte)'P' || awal[1] != (byte)'K')
                return BadRequest(new { message = "File bukan APK yang valid." });
        }

        try
        {
            using var stream = file.OpenReadStream();
            await _r2.UploadAsync(stream, KunciR2, "application/vnd.android.package-archive");
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal menyimpan APK: {ex.Message}" });
        }

        var info = new InfoApk
        {
            Versi = versi.Trim(),
            Ukuran = file.Length,
            Catatan = string.IsNullOrWhiteSpace(catatan) ? null : catatan.Trim(),
            DiunggahPada = DateTime.UtcNow,
            DiunggahOleh = User.FindFirstValue(ClaimTypes.Email) ?? User.Identity?.Name,
        };
        var json = JsonSerializer.Serialize(info);
        var setting = await _context.KonfigurasiSistems.FirstOrDefaultAsync(k => k.NamaSetting == NamaSetting);
        if (setting == null)
        {
            _context.KonfigurasiSistems.Add(new KonfigurasiSistem
            {
                NamaSetting = NamaSetting,
                NilaiSetting = json,
                Deskripsi = "Info APK MAESTRO Mobile terbaru (halaman /download)",
            });
        }
        else
        {
            setting.NilaiSetting = json;
            setting.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();

        return Ok(new { tersedia = true, versi = info.Versi, ukuran = info.Ukuran, catatan = info.Catatan, diunggahPada = info.DiunggahPada });
    }
}
