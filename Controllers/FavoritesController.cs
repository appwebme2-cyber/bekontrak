using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FavoritesController : ControllerBase
{
    private readonly AppDbContext _context;
    public FavoritesController(AppDbContext context) => _context = context;

    // Daftar id kontrak favorit milik user yang sedang login (terbaru dulu)
    [HttpGet]
    public async Task<IActionResult> GetMine()
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var ids = await _context.KontrakFavorits
            .Where(f => f.IdUser == userId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => f.IdKontrak)
            .ToListAsync();

        return Ok(ids);
    }

    [HttpPost("{idKontrak}")]
    public async Task<IActionResult> Add(string idKontrak)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var exists = await _context.KontrakFavorits
                .AnyAsync(f => f.IdUser == userId && f.IdKontrak == idKontrak);
            if (!exists)
            {
                _context.KontrakFavorits.Add(new KontrakFavorit { IdUser = userId, IdKontrak = idKontrak });
                await _context.SaveChangesAsync();
            }
            return Ok(new { message = "Ditambahkan ke favorit" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal menambah favorit: {DescribeException(ex)}" });
        }
    }

    [HttpDelete("{idKontrak}")]
    public async Task<IActionResult> Remove(string idKontrak)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        try
        {
            var existing = await _context.KontrakFavorits
                .Where(f => f.IdUser == userId && f.IdKontrak == idKontrak)
                .ToListAsync();
            if (existing.Count > 0)
            {
                _context.KontrakFavorits.RemoveRange(existing);
                await _context.SaveChangesAsync();
            }
            return Ok(new { message = "Dihapus dari favorit" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal menghapus favorit: {DescribeException(ex)}" });
        }
    }

    // Gabungkan pesan exception + inner exception (DbUpdateException dari EF
    // biasanya cuma bilang "see inner exception" tanpa detail aslinya)
    private static string DescribeException(Exception ex)
    {
        var parts = new List<string>();
        var current = ex;
        while (current != null)
        {
            parts.Add($"{current.GetType().Name}: {current.Message}");
            current = current.InnerException;
        }
        return string.Join(" -> ", parts);
    }
}
