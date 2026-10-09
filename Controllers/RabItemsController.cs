using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class RabItemsController : ControllerBase
{
    private readonly AppDbContext _context;
    public RabItemsController(AppDbContext context) => _context = context;

    private static RabItemDto ToDto(RabItem r) => new RabItemDto
    {
        IdRabItem = r.IdRabItem,
        IdKontrak = r.IdKontrak,
        KodeItem = r.KodeItem,
        Kategori = r.Kategori,
        UraianPekerjaan = r.UraianPekerjaan,
        Satuan = r.Satuan,
        HargaSatuanUpah = r.HargaSatuanUpah,
        HargaSatuanMaterial = r.HargaSatuanMaterial,
        HargaSatuanAlat = r.HargaSatuanAlat,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt
    };

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? idKontrak)
    {
        var query = _context.RabItems.AsQueryable();
        if (!string.IsNullOrEmpty(idKontrak))
            query = query.Where(r => r.IdKontrak == idKontrak);

        var list = await query
            .OrderBy(r => r.KodeItem)
            .Select(r => ToDto(r))
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var r = await _context.RabItems.FindAsync(id);
        if (r == null) return NotFound();
        return Ok(ToDto(r));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateRabItemDto dto)
    {
        try
        {
            var item = new RabItem
            {
                IdKontrak = dto.IdKontrak,
                KodeItem = dto.KodeItem,
                Kategori = dto.Kategori,
                UraianPekerjaan = dto.UraianPekerjaan,
                Satuan = dto.Satuan,
                HargaSatuanUpah = dto.HargaSatuanUpah,
                HargaSatuanMaterial = dto.HargaSatuanMaterial,
                HargaSatuanAlat = dto.HargaSatuanAlat
            };

            _context.RabItems.Add(item);
            await _context.SaveChangesAsync();
            return Ok(ToDto(item));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal membuat item RAB: {DescribeException(ex)}" });
        }
    }

    // Impor banyak item sekaligus (mis. dari file Excel RAB). Item yang kode + uraiannya sudah
    // ada di kontrak yang sama dilewati supaya impor ulang tidak menggandakan data.
    [HttpPost("bulk")]
    public async Task<IActionResult> CreateBulk([FromBody] BulkCreateRabItemsDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.IdKontrak))
                return BadRequest(new { message = "Kontrak belum dipilih." });
            if (dto.Items == null || dto.Items.Count == 0)
                return BadRequest(new { message = "Tidak ada item untuk diimpor." });

            static string Key(string kode, string uraian) =>
                $"{kode.Trim().ToLowerInvariant()}|{uraian.Trim().ToLowerInvariant()}";

            var existing = await _context.RabItems
                .Where(r => r.IdKontrak == dto.IdKontrak)
                .Select(r => new { r.KodeItem, r.UraianPekerjaan })
                .ToListAsync();
            var seen = new HashSet<string>(existing.Select(e => Key(e.KodeItem, e.UraianPekerjaan)));

            var added = 0;
            var skipped = 0;
            foreach (var item in dto.Items)
            {
                if (string.IsNullOrWhiteSpace(item.KodeItem) ||
                    string.IsNullOrWhiteSpace(item.UraianPekerjaan) ||
                    string.IsNullOrWhiteSpace(item.Satuan) ||
                    !seen.Add(Key(item.KodeItem, item.UraianPekerjaan)))
                {
                    skipped++;
                    continue;
                }

                _context.RabItems.Add(new RabItem
                {
                    IdKontrak = dto.IdKontrak,
                    KodeItem = item.KodeItem.Trim(),
                    Kategori = item.Kategori,
                    UraianPekerjaan = item.UraianPekerjaan.Trim(),
                    Satuan = item.Satuan.Trim(),
                    HargaSatuanUpah = item.HargaSatuanUpah,
                    HargaSatuanMaterial = item.HargaSatuanMaterial,
                    HargaSatuanAlat = item.HargaSatuanAlat
                });
                added++;
            }

            await _context.SaveChangesAsync();
            return Ok(new { added, skipped });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal impor item RAB: {DescribeException(ex)}" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateRabItemDto dto)
    {
        try
        {
            var item = await _context.RabItems.FindAsync(id);
            if (item == null) return NotFound();

            item.KodeItem = dto.KodeItem;
            item.Kategori = dto.Kategori;
            item.UraianPekerjaan = dto.UraianPekerjaan;
            item.Satuan = dto.Satuan;
            item.HargaSatuanUpah = dto.HargaSatuanUpah;
            item.HargaSatuanMaterial = dto.HargaSatuanMaterial;
            item.HargaSatuanAlat = dto.HargaSatuanAlat;
            item.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(ToDto(item));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal memperbarui item RAB: {DescribeException(ex)}" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        try
        {
            var item = await _context.RabItems.FindAsync(id);
            if (item == null) return NotFound();

            _context.RabItems.Remove(item);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Item RAB berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal menghapus item RAB: {DescribeException(ex)}" });
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
