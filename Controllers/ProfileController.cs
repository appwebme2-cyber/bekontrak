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
public class ProfileController : ControllerBase
{
    private readonly AppDbContext _context;
    public ProfileController(AppDbContext context) => _context = context;

    // ==================== GET CURRENT USER ====================
    [HttpGet("me")]
    public async Task<IActionResult> GetMe()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Unauthorized();

        var profile = await _context.Profiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == userId);
        if (profile == null) return NotFound();

        return Ok(MapToDto(profile));
    }

    // ==================== GET ALL USERS ====================
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.Profiles
            .OrderBy(p => p.FullName)
            .ToListAsync();

        return Ok(list.Select(MapToDto));
    }

    // ==================== UPDATE OWN PROFILE ====================
    [HttpPut("me")]
    public async Task<IActionResult> UpdateMe([FromBody] UpdateProfileDto dto)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Unauthorized();

        var profile = await _context.Profiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == userId);
        if (profile == null) return NotFound();

        profile.FullName = dto.FullName ?? profile.FullName;
        profile.UpdatedAt = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(dto.NewPassword))
        {
            if (string.IsNullOrEmpty(dto.CurrentPassword) ||
                !BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, profile.PasswordHash))
                return BadRequest(new { message = "Password lama tidak sesuai" });

            profile.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            // Ganti password: HP yang masih masuk (MAESTRO Mobile) harus login ulang
            await AuthController.CabutSemuaRefreshTokenAsync(_context, profile.Id);
        }

        await _context.SaveChangesAsync();
        return Ok(MapToDto(profile));
    }

    // ==================== UPDATE USER ROLE ====================
    [HttpPut("{id}/role")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateRole(string id, [FromBody] UpdateRoleDto dto)
    {
        var requesterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (id == requesterId)
            return BadRequest(new { message = "Tidak dapat mengubah role akun sendiri" });

        var profile = await _context.Profiles.FindAsync(id);
        if (profile == null) return NotFound(new { message = "User tidak ditemukan" });
        if (GuardHeadOffice(profile, dto.Role) is { } ditolak) return ditolak;

        profile.Role = dto.Role;
        profile.UpdatedAt = DateTime.UtcNow;
        // Multi-RU: non-admin wajib punya RU (RU kosong hanya untuk Super Admin)
        if (profile.Role != "admin" && string.IsNullOrEmpty(profile.IdRu))
            profile.IdRu = "RU4";

        await _context.SaveChangesAsync();
        return Ok(new { message = "Role berhasil diperbarui" });
    }

    // ==================== UPDATE USER FULL DATA ====================
    [HttpPut("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateUser(string id, [FromBody] AdminUpdateUserDto dto)
    {
        var requesterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (id == requesterId)
            return BadRequest(new { message = "Tidak dapat mengedit akun sendiri" });

        var profile = await _context.Profiles.FindAsync(id);
        if (profile == null) return NotFound(new { message = "User tidak ditemukan" });
        if (GuardHeadOffice(profile, dto.Role) is { } ditolak) return ditolak;

        if (!string.IsNullOrEmpty(dto.FullName))
            profile.FullName = dto.FullName;

        // Multi-RU: hanya Super Admin yang boleh memindahkan RU pengguna
        if (dto.IdRu != null && IsSuperAdmin())
        {
            var ru = dto.IdRu.Trim().ToUpperInvariant();
            if (ru.Length == 0)
                profile.IdRu = null; // hanya berlaku untuk admin (= Super Admin); non-admin diisi lagi di bawah
            else if (CurrentRu.ValidRus.Contains(ru))
                profile.IdRu = ru;
            else
                return BadRequest(new { message = "RU tidak valid." });
        }

        if (!string.IsNullOrEmpty(dto.Role))
            profile.Role = dto.Role;
        // Multi-RU: non-admin wajib punya RU (RU kosong hanya untuk Super Admin)
        if (profile.Role != "admin" && string.IsNullOrEmpty(profile.IdRu))
            profile.IdRu = "RU4";

        if (!string.IsNullOrEmpty(dto.Email))
        {
            if (!System.Net.Mail.MailAddress.TryCreate(dto.Email, out _))
                return BadRequest(new { message = "Format email tidak valid." });

            var emailTaken = await _context.Profiles.IgnoreQueryFilters()
                .AnyAsync(p => p.Email == dto.Email && p.Id != id);
            if (emailTaken)
                return Conflict(new { message = "Email sudah digunakan oleh pengguna lain." });

            profile.Email = dto.Email;
        }

        profile.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(MapToDto(profile));
    }

    // ==================== TOGGLE ACTIVE STATUS ====================
    [HttpPatch("{id}/status")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> ToggleStatus(string id)
    {
        var requesterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (id == requesterId)
            return BadRequest(new { message = "Tidak dapat mengubah status akun sendiri" });

        var profile = await _context.Profiles.FindAsync(id);
        if (profile == null) return NotFound(new { message = "User tidak ditemukan" });
        if (GuardHeadOffice(profile, null) is { } ditolak) return ditolak;

        profile.IsActive = !profile.IsActive;
        profile.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Status berhasil diubah", isActive = profile.IsActive });
    }

    // ==================== DELETE USER ====================
    [HttpDelete("{id}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var requesterId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (id == requesterId)
            return BadRequest(new { message = "Tidak dapat menghapus akun sendiri" });

        var profile = await _context.Profiles.FindAsync(id);
        if (profile == null) return NotFound(new { message = "User tidak ditemukan" });
        if (GuardHeadOffice(profile, null) is { } ditolak) return ditolak;

        _context.Profiles.Remove(profile);
        await _context.SaveChangesAsync();

        return Ok(new { message = "User berhasil dihapus" });
    }

    // ==================== ASSIGN VENDOR ====================
    [HttpPut("{id}/assign-vendor")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AssignVendor(string id, [FromBody] AssignVendorDto dto)
    {
        var profile = await _context.Profiles.FindAsync(id);
        if (profile == null) return NotFound(new { message = "User tidak ditemukan" });
        if (GuardHeadOffice(profile, null) is { } ditolak) return ditolak;

        profile.IdVendor = dto.IdVendor;
        profile.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Vendor berhasil di-assign ke user" });
    }

    // ==================== HELPER ====================
    private static ProfileDto MapToDto(Profile p) => new ProfileDto
    {
        Id = p.Id,
        Email = p.Email,
        FullName = p.FullName,
        Role = p.Role,
        IdVendor = p.IdVendor,
        CreatedAt = p.CreatedAt,
        IsActive = p.IsActive,   // ← tambah ini
        IdRu = p.IdRu
    };

    private bool IsSuperAdmin() => User.IsSuperAdmin();

    // Multi-RU: Head Office bisa melihat semua RU, jadi hanya Super Admin yang boleh memberi role itu
    // atau mengelola akun Head Office (admin RU tidak bisa membuka akses lintas RU).
    private IActionResult? GuardHeadOffice(Profile target, string? newRole)
    {
        if (IsSuperAdmin()) return null;
        if (!IsHeadOffice(target.Role) && !IsHeadOffice(newRole)) return null;
        return StatusCode(StatusCodes.Status403Forbidden,
            new { message = "Akun Head Office (akses semua RU) hanya dapat dikelola oleh Super Admin." });
    }

    private static bool IsHeadOffice(string? role) =>
        string.Equals(role?.Trim(), "head_office", StringComparison.OrdinalIgnoreCase);
}