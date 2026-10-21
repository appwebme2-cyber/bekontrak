using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;
using RefineryContractAPI.Services;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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

    // Pembatasan percobaan login: 5 kali salah berturut-turut untuk satu email mengunci login email
    // itu 5 menit, supaya password tidak bisa ditebak lewat panggilan API berulang.
    private const int MaxLoginFails = 5;
    private static readonly TimeSpan LoginLockDuration = TimeSpan.FromMinutes(5);
    private static readonly ConcurrentDictionary<string, (int Fails, DateTime LockedUntil)> LoginAttempts = new();

    // ==================== LOGIN ====================
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        var attemptKey = (dto.Email ?? string.Empty).Trim().ToLowerInvariant();

        if (LoginAttempts.TryGetValue(attemptKey, out var attempt) && attempt.LockedUntil > DateTime.UtcNow)
        {
            var minutes = Math.Ceiling((attempt.LockedUntil - DateTime.UtcNow).TotalMinutes);
            return StatusCode(429, new { message = $"Terlalu banyak percobaan login gagal. Coba lagi dalam {minutes} menit." });
        }

        var user = await _context.Profiles
            .FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            if (LoginAttempts.Count > 10000) LoginAttempts.Clear(); // batasi memori dari email acak
            LoginAttempts.AddOrUpdate(
                attemptKey,
                _ => (1, DateTime.MinValue),
                (_, old) =>
                {
                    var fails = (old.LockedUntil > DateTime.MinValue && old.LockedUntil <= DateTime.UtcNow) ? 1 : old.Fails + 1;
                    return fails >= MaxLoginFails ? (0, DateTime.UtcNow.Add(LoginLockDuration)) : (fails, DateTime.MinValue);
                });
            return Unauthorized(new { message = "Email atau password salah" });
        }

        LoginAttempts.TryRemove(attemptKey, out _);

        if (!user.IsActive)
            return Unauthorized(new { message = "Akun Anda belum diaktifkan. Hubungi admin." });

        var token = GenerateToken(user);
        return Ok(new AuthResponseDto
        {
            Token = token,
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role,
            RefreshToken = dto.Mobile ? await BuatRefreshTokenAsync(user.Id, "mobile") : null
        });
    }

    // ==================== REFRESH (MAESTRO Mobile) ====================
    // Tukar kunci perpanjangan dengan token baru. Masa berlaku kunci bergeser 90 hari setiap dipakai, jadi
    // pengguna yang membuka aplikasi minimal sekali dalam 90 hari tidak perlu login ulang. Ditolak bila
    // kunci sudah dicabut (Keluar / ganti password), kedaluwarsa, atau akunnya dinonaktifkan/dihapus.
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
    {
        var ditolak = Unauthorized(new { message = "Sesi berakhir. Silakan login kembali." });
        if (string.IsNullOrWhiteSpace(dto.RefreshToken)) return ditolak;

        var hash = HashRefreshToken(dto.RefreshToken);
        var kunci = await _context.RefreshTokens.FirstOrDefaultAsync(r => r.TokenHash == hash);
        if (kunci == null || kunci.RevokedAt != null || kunci.ExpiresAt <= DateTime.UtcNow) return ditolak;

        var user = await _context.Profiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == kunci.IdUser);
        if (user == null || !user.IsActive)
        {
            kunci.RevokedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return ditolak;
        }

        kunci.LastUsedAt = DateTime.UtcNow;
        kunci.ExpiresAt = DateTime.UtcNow.Add(MasaRefreshToken);
        await _context.SaveChangesAsync();

        // Token baru dibuat dari data akun terkini (peran & RU ikut terbarui)
        return Ok(new AuthResponseDto
        {
            Token = GenerateToken(user),
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role
        });
    }

    // Cabut kunci perpanjangan saat pengguna menekan Keluar di HP (berlaku walau token 8 jamnya sudah habis)
    [HttpPost("refresh/revoke")]
    [AllowAnonymous]
    public async Task<IActionResult> RevokeRefresh([FromBody] RefreshTokenDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.RefreshToken)) return NoContent();
        var hash = HashRefreshToken(dto.RefreshToken);
        await _context.RefreshTokens
            .Where(r => r.TokenHash == hash && r.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.RevokedAt, DateTime.UtcNow));
        return NoContent();
    }

    private static readonly TimeSpan MasaRefreshToken = TimeSpan.FromDays(90);

    public static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task<string> BuatRefreshTokenAsync(string idUser, string perangkat)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        _context.RefreshTokens.Add(new RefreshToken
        {
            IdUser = idUser,
            TokenHash = HashRefreshToken(token),
            Perangkat = perangkat,
            ExpiresAt = DateTime.UtcNow.Add(MasaRefreshToken)
        });
        await _context.SaveChangesAsync();
        return token;
    }

    /// <summary>Cabut semua kunci perpanjangan milik pengguna (mis. setelah ganti password): semua HP harus login ulang.</summary>
    public static Task CabutSemuaRefreshTokenAsync(AppDbContext db, string idUser) =>
        db.RefreshTokens
            .Where(r => r.IdUser == idUser && r.RevokedAt == null)
            .ExecuteUpdateAsync(u => u.SetProperty(r => r.RevokedAt, DateTime.UtcNow));

    // ==================== REGISTER (public) ====================
    // Self-register: role selalu 'user', isActive = false
    // Dimatikan secara default karena user dibuat admin lewat admin-register; form daftar di
    // halaman depan juga disembunyikan. Aktifkan hanya bila perlu dengan env var Auth__AllowPublicRegister=true.
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        if (!string.Equals(_config["Auth:AllowPublicRegister"], "true", StringComparison.OrdinalIgnoreCase))
            return NotFound(new { message = "Pendaftaran mandiri tidak tersedia. Hubungi admin untuk dibuatkan akun." });

        var exists = await _context.Profiles.IgnoreQueryFilters().AnyAsync(u => u.Email == dto.Email); // email unik di semua RU
        if (exists)
            return BadRequest(new { message = "Email sudah terdaftar" });

        var user = new Profile
        {
            Email = dto.Email,
            FullName = dto.FullName ?? dto.Email,
            Role = "user",    // public register selalu user
            IdRu = "RU4",
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
        var exists = await _context.Profiles.IgnoreQueryFilters().AnyAsync(u => u.Email == dto.Email); // email unik di semua RU
        if (exists)
            return BadRequest(new { message = "Email sudah terdaftar" });

        // Multi-RU: Admin RU selalu membuat pengguna di RU-nya sendiri (tidak bisa membuat Super Admin).
        // Super Admin memilih RU; admin tanpa RU = Super Admin, selain admin wajib punya RU (default RU IV).
        var newRole = !string.IsNullOrEmpty(dto.Role) ? dto.Role : "user";
        // Head Office bisa melihat semua RU, jadi hanya Super Admin yang boleh membuatnya
        if (string.Equals(newRole.Trim(), "head_office", StringComparison.OrdinalIgnoreCase) && !User.IsSuperAdmin())
            return StatusCode(StatusCodes.Status403Forbidden,
                new { message = "Akun Head Office (akses semua RU) hanya dapat dibuat oleh Super Admin." });
        var callerRu = User.FindFirst("id_ru")?.Value;
        string? newRu;
        if (!string.IsNullOrEmpty(callerRu))
        {
            newRu = callerRu;
        }
        else
        {
            var requested = (dto.IdRu ?? string.Empty).Trim().ToUpperInvariant();
            if (requested.Length == 0)
                newRu = newRole == "admin" ? null : CurrentRu.DefaultRu;
            else if (CurrentRu.ValidRus.Contains(requested))
                newRu = requested;
            else
                return BadRequest(new { message = "RU tidak valid." });
        }

        var user = new Profile
        {
            Email = dto.Email,
            FullName = dto.FullName ?? dto.Email,
            Role = newRole,
            IdRu = newRu,
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

        var hasUsers = await _context.Profiles.IgnoreQueryFilters().AnyAsync();
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

    // ==================== LOGOUT (blacklist token) ====================
    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (string.IsNullOrEmpty(jti))
            return Ok(new { message = "Logged out" });

        // Parse expiry from token to store accurate expiry
        var authHeader = Request.Headers["Authorization"].ToString();
        DateTime expiresAt = DateTime.UtcNow.AddHours(8);
        if (authHeader.StartsWith("Bearer "))
        {
            var rawToken = authHeader["Bearer ".Length..];
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(rawToken))
            {
                var jwt = handler.ReadJwtToken(rawToken);
                expiresAt = jwt.ValidTo;
            }
        }

        var blacklistEntry = new RefineryContractAPI.Models.TokenBlacklist
        {
            Jti = jti,
            ExpiresAt = expiresAt,
            RevokedAt = DateTime.UtcNow
        };

        _context.TokenBlacklists.Add(blacklistEntry);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Logout berhasil" });
    }

    // ==================== CHANGE PASSWORD ====================
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _context.Profiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.Id == userId);

        if (user == null)
            return NotFound(new { message = "User tidak ditemukan" });

        if (!BCrypt.Net.BCrypt.Verify(dto.OldPassword, user.PasswordHash))
            return BadRequest(new { message = "Password lama tidak sesuai" });

        if (dto.NewPassword.Length < 6)
            return BadRequest(new { message = "Password baru minimal 6 karakter" });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        await _context.SaveChangesAsync();
        await CabutSemuaRefreshTokenAsync(_context, user.Id);

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
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
            new Claim("full_name", user.FullName ?? ""),
            // Kosong = Super Admin (semua RU); dipakai penyaringan data per RU di tahap berikutnya
            new Claim("id_ru", user.IdRu ?? "")
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