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

        return Ok(await SimpanInfoAsync(versi, catatan, file.Length));
    }

    // Ambil APK langsung dari link unduhan EAS (expo.dev) oleh server, lalu simpan ke R2 (Super Admin).
    // File tidak lewat koneksi pengunggah, jadi tidak gagal karena internet lambat. Link EAS hanya berlaku
    // sementara, tapi salinannya di R2 tetap.
    [HttpPost("android/dari-link")]
    [Authorize(Roles = "admin")]
    [SuperAdminOnly]
    public async Task<IActionResult> DariLink([FromBody] DariLinkDto dto, [FromServices] IHttpClientFactory httpFactory)
    {
        if (string.IsNullOrWhiteSpace(dto.Versi))
            return BadRequest(new { message = "Versi wajib diisi, mis. 1.0.0." });
        if (!Uri.TryCreate(dto.Url?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps
            || !(uri.Host.Equals("expo.dev", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".expo.dev", StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "Link harus dari expo.dev (https://expo.dev/artifacts/...)." });
        if (uri.AbsolutePath.Contains("/builds/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Itu link halaman build. Salin link unduhan APK-nya (diawali https://expo.dev/artifacts/...)." });

        var sementara = Path.GetTempFileName();
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromMinutes(5);
            long ukuran;
            using (var res = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!res.IsSuccessStatusCode)
                    return BadRequest(new { message = $"APK tidak bisa diambil dari link (HTTP {(int)res.StatusCode}). Link mungkin sudah kedaluwarsa; salin ulang dari expo.dev." });
                if (res.Content.Headers.ContentLength > BatasUkuran)
                    return BadRequest(new { message = "Ukuran APK maksimal 250 MB." });

                await using var sumber = await res.Content.ReadAsStreamAsync();
                await using var tujuan = System.IO.File.Create(sementara);
                var buffer = new byte[81920];
                ukuran = 0;
                int n;
                while ((n = await sumber.ReadAsync(buffer)) > 0)
                {
                    ukuran += n;
                    if (ukuran > BatasUkuran) return BadRequest(new { message = "Ukuran APK maksimal 250 MB." });
                    await tujuan.WriteAsync(buffer.AsMemory(0, n));
                }
            }

            // File APK (ZIP) selalu diawali "PK"; halaman web/HTML ditolak
            await using (var cek = System.IO.File.OpenRead(sementara))
            {
                var awal = new byte[2];
                if (await cek.ReadAsync(awal.AsMemory(0, 2)) < 2 || awal[0] != (byte)'P' || awal[1] != (byte)'K')
                    return BadRequest(new { message = "Link tersebut bukan file APK. Salin link unduhan APK (https://expo.dev/artifacts/...)." });
            }

            await using (var stream = System.IO.File.OpenRead(sementara))
                await _r2.UploadAsync(stream, KunciR2, "application/vnd.android.package-archive");

            return Ok(await SimpanInfoAsync(dto.Versi, dto.Catatan, ukuran));
        }
        catch (TaskCanceledException)
        {
            return StatusCode(504, new { message = "Mengambil APK dari expo.dev terlalu lama. Coba lagi." });
        }
        catch (HttpRequestException ex)
        {
            return BadRequest(new { message = $"Tidak bisa menghubungi expo.dev: {ex.Message}" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal menyimpan APK: {ex.Message}" });
        }
        finally
        {
            try { System.IO.File.Delete(sementara); } catch { /* file sementara */ }
        }
    }

    public class DariLinkDto
    {
        public string? Url { get; set; }
        public string? Versi { get; set; }
        public string? Catatan { get; set; }
    }

    private async Task<object> SimpanInfoAsync(string versi, string? catatan, long ukuran)
    {
        var info = new InfoApk
        {
            Versi = versi.Trim(),
            Ukuran = ukuran,
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

        return new { tersedia = true, versi = info.Versi, ukuran = info.Ukuran, catatan = info.Catatan, diunggahPada = info.DiunggahPada };
    }
}
