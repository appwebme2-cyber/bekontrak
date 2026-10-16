using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;

namespace RefineryContractAPI.Controllers;

/// <summary>Daftar Refinery Unit untuk pilihan RU di tampilan (multi-RU).</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RuController : ControllerBase
{
    private readonly AppDbContext _context;

    public RuController(AppDbContext context) => _context = context;

    // GET /api/Ru
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.Rus.AsNoTracking()
            .Where(r => r.IsActive)
            .OrderBy(r => r.Urutan)
            .Select(r => new { idRu = r.IdRu, kode = r.Kode, nama = r.Nama, label = r.Kode + " " + r.Nama })
            .ToListAsync();
        return Ok(list);
    }
}
