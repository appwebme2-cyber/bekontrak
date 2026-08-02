using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FileUploadController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public FileUploadController(IWebHostEnvironment env)
    {
        _env = env;
    }

    [HttpPost]
    public async Task<IActionResult> Upload([FromForm] string folder, IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "File tidak boleh kosong" });

        var allowedTypes = new[] {
            "application/pdf", "application/msword",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.ms-excel",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "image/jpeg", "image/png"
        };

        if (!allowedTypes.Contains(file.ContentType))
            return BadRequest(new { message = "Tipe file tidak diizinkan" });

        var uploadsFolder = Path.Combine(_env.WebRootPath ?? "wwwroot", "uploads", folder ?? "general");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        var ext = Path.GetExtension(file.FileName);
        var uniqueFileName = $"{Guid.NewGuid()}{ext}";
        var filePath = Path.Combine(uploadsFolder, uniqueFileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var fileUrl = $"/uploads/{folder}/{uniqueFileName}";

        return Ok(new
        {
            url = fileUrl,
            path = filePath,
            name = file.FileName,
            size = file.Length,
            type = file.ContentType
        });
    }

    [HttpDelete]
    public IActionResult Delete([FromQuery] string path)
    {
        if (string.IsNullOrEmpty(path)) return BadRequest();

        var fullPath = Path.Combine(_env.WebRootPath ?? "wwwroot", path.TrimStart('/'));
        if (System.IO.File.Exists(fullPath))
            System.IO.File.Delete(fullPath);

        return Ok(new { message = "File berhasil dihapus" });
    }
}
