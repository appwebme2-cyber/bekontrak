using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;
using RefineryContractAPI.Services;

namespace RefineryContractAPI.Controllers;

/// <summary>Daftar/hapus token notifikasi push HP (MAESTRO Mobile) milik pengguna yang login.</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PushTokenController : ControllerBase
{
    private readonly AppDbContext _context;

    public PushTokenController(AppDbContext context) => _context = context;

    // POST /api/PushToken  { token, platform }
    [HttpPost]
    [AllowReadOnlyRoles] // akun hanya-baca juga boleh menerima notifikasi
    public async Task<IActionResult> Register([FromBody] PushTokenDto dto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var token = (dto.Token ?? string.Empty).Trim();
        if (token.Length > 200 || !(token.StartsWith("ExponentPushToken[") || token.StartsWith("ExpoPushToken[")))
            return BadRequest(new { message = "Token notifikasi tidak valid." });

        // Satu HP = satu token; bila HP berganti akun, token pindah ke akun yang baru login
        var existing = await _context.PushTokens.FirstOrDefaultAsync(t => t.Token == token);
        if (existing == null)
        {
            _context.PushTokens.Add(new PushToken { IdUser = userId, Token = token, Platform = dto.Platform });
        }
        else
        {
            existing.IdUser = userId;
            existing.Platform = dto.Platform;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();
        return Ok(new { message = "Notifikasi HP aktif." });
    }

    // DELETE /api/PushToken?token=...  (saat keluar atau notifikasi dimatikan)
    [HttpDelete]
    [AllowReadOnlyRoles]
    public async Task<IActionResult> Unregister([FromQuery] string token)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        await _context.PushTokens.Where(t => t.Token == token && t.IdUser == userId).ExecuteDeleteAsync();
        return Ok(new { message = "Notifikasi HP dimatikan." });
    }
}
