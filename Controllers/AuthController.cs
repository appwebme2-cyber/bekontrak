using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    // ==================== LOGIN ====================
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var user = await _context.Profiles
            .FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized(new { message = "Email atau password salah" });

        if (!user.IsActive)
            return Unauthorized(new { message = "Akun Anda belum diaktifkan. Hubungi admin." });

        var token = GenerateToken(user);
        return Ok(new AuthResponseDto
        {
            Token = token,
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role
        });
    }

    // ==================== REGISTER (public) ====================
    // Self-register: role selalu 'user', isActive = false
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        var exists = await _context.Profiles.AnyAsync(u => u.Email == dto.Email);
        if (exists)
            return BadRequest(new { message = "Email sudah terdaftar" });

        var user = new Profile
        {
            Email = dto.Email,
            FullName = dto.FullName ?? dto.Email,
            Role = "user",    // public register selalu user
            IsActive = false,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };

        _context.Profiles.Add(user);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Registrasi berhasil. Tunggu admin mengaktifkan akun Anda.", id = user.Id });
    }

    // ==================== ADMIN CREATE USER ====================
    // Admin bisa set role apapun termasuk vendor
    [HttpPost("admin-register")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> AdminRegister([FromBody] RegisterDto dto)
    {
        var exists = await _context.Profiles.AnyAsync(u => u.Email == dto.Email);
        if (exists)
            return BadRequest(new { message = "Email sudah terdaftar" });

        var user = new Profile
        {
            Email = dto.Email,
            FullName = dto.FullName ?? dto.Email,
            Role = !string.IsNullOrEmpty(dto.Role) ? dto.Role : "user",
            IsActive = false,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };

        _context.Profiles.Add(user);
        await _context.SaveChangesAsync();

        return Ok(new { message = "User berhasil ditambahkan.", id = user.Id });
    }

    // ==================== SEED ADMIN (migrasi) ====================
    // Hanya aktif jika env var SEED_ADMIN_TOKEN diset di Railway.
    // Hanya bisa dipakai sekali saat DB benar-benar kosong (0 user).
    // Setelah berhasil login, hapus SEED_ADMIN_TOKEN dari Railway env vars.
    [HttpPost("seed-admin")]
    public async Task<IActionResult> SeedAdmin([FromBody] SeedAdminDto dto)
    {
        var seedToken = _config["SEED_ADMIN_TOKEN"];
        if (string.IsNullOrEmpty(seedToken))
            return NotFound(new { message = "Endpoint ini tidak aktif." });

        if (dto.SeedToken != seedToken)
            return Unauthorized(new { message = "Seed token tidak valid." });

        var hasUsers = await _context.Profiles.AnyAsync();
        if (hasUsers)
            return BadRequest(new { message = "Database sudah ada user. Endpoint ini hanya untuk DB kosong." });

        var admin = new Profile
        {
            Email = dto.Email,
            FullName = dto.FullName ?? "Administrator",
            Role = "admin",
            IsActive = true,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password)
        };

        _context.Profiles.Add(admin);
        await _context.SaveChangesAsync();

        var token = GenerateToken(admin);
        return Ok(new
        {
            message = "Admin berhasil dibuat. Segera hapus SEED_ADMIN_TOKEN dari Railway env vars!",
            token,
            id = admin.Id,
            email = admin.Email,
            role = admin.Role
        });
    }

    // ==================== CHANGE PASSWORD ====================
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _context.Profiles.FindAsync(userId);

        if (user == null)
            return NotFound(new { message = "User tidak ditemukan" });

        if (!BCrypt.Net.BCrypt.Verify(dto.OldPassword, user.PasswordHash))
            return BadRequest(new { message = "Password lama tidak sesuai" });

        if (dto.NewPassword.Length < 6)
            return BadRequest(new { message = "Password baru minimal 6 karakter" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Password berhasil diubah" });
    }

    private string GenerateToken(Profile user)
    {
        var jwtSettings = _config.GetSection("JwtSettings");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiryHours = int.Parse(jwtSettings["ExpiryHours"] ?? "8");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("full_name", user.FullName ?? "")
        };

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"],
            audience: jwtSettings["Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(expiryHours),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}