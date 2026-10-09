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
public class MaterialRequirementLinesController : ControllerBase
{
    private readonly AppDbContext _context;
    public MaterialRequirementLinesController(AppDbContext context) => _context = context;

    private static MaterialRequirementLineDto ToDto(MaterialRequirementLine l) => new MaterialRequirementLineDto
    {
        IdLine = l.IdLine,
        IdDraft = l.IdDraft,
        Jenis = l.Jenis,
        IdRabItem = l.IdRabItem,
        KodeItemSnapshot = l.KodeItemSnapshot,
        UraianPekerjaan = l.UraianPekerjaan,
        Satuan = l.Satuan,
        VolumeKalkulasi = l.VolumeKalkulasi,
        CatatanKalkulasi = l.CatatanKalkulasi,
        VolumeKlaim = l.VolumeKlaim,
        Urutan = l.Urutan,
        CreatedAt = l.CreatedAt,
        UpdatedAt = l.UpdatedAt
    };

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? idDraft)
    {
        var query = _context.MaterialRequirementLines.AsQueryable();
        if (!string.IsNullOrEmpty(idDraft))
            query = query.Where(l => l.IdDraft == idDraft);

        var list = await query
            .OrderBy(l => l.Urutan)
            .ThenBy(l => l.CreatedAt)
            .Select(l => ToDto(l))
            .ToListAsync();

        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var l = await _context.MaterialRequirementLines.FindAsync(id);
        if (l == null) return NotFound();
        return Ok(ToDto(l));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateMaterialRequirementLineDto dto)
    {
        try
        {
            var line = new MaterialRequirementLine
            {
                IdDraft = dto.IdDraft,
                Jenis = dto.Jenis,
                IdRabItem = string.IsNullOrEmpty(dto.IdRabItem) ? null : dto.IdRabItem,
                KodeItemSnapshot = dto.KodeItemSnapshot,
                UraianPekerjaan = dto.UraianPekerjaan,
                Satuan = dto.Satuan,
                VolumeKalkulasi = dto.VolumeKalkulasi,
                CatatanKalkulasi = dto.CatatanKalkulasi,
                VolumeKlaim = dto.VolumeKlaim,
                Urutan = dto.Urutan
            };

            _context.MaterialRequirementLines.Add(line);
            await _context.SaveChangesAsync();
            return Ok(ToDto(line));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal membuat baris kebutuhan: {DescribeException(ex)}" });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateMaterialRequirementLineDto dto)
    {
        try
        {
            var line = await _context.MaterialRequirementLines.FindAsync(id);
            if (line == null) return NotFound();

            line.Jenis = dto.Jenis;
            line.IdRabItem = string.IsNullOrEmpty(dto.IdRabItem) ? null : dto.IdRabItem;
            line.KodeItemSnapshot = dto.KodeItemSnapshot;
            line.UraianPekerjaan = dto.UraianPekerjaan;
            line.Satuan = dto.Satuan;
            line.VolumeKalkulasi = dto.VolumeKalkulasi;
            line.CatatanKalkulasi = dto.CatatanKalkulasi;
            line.VolumeKlaim = dto.VolumeKlaim;
            line.Urutan = dto.Urutan;
            line.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();
            return Ok(ToDto(line));
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal memperbarui baris kebutuhan: {DescribeException(ex)}" });
        }
    }

    // Hapus semua baris (pekerjaan + material) dalam satu draft sekaligus, dipakai saat hasil
    // AI akan menggantikan isi draft supaya tidak dobel.
    [HttpDelete("by-draft/{idDraft}")]
    public async Task<IActionResult> DeleteByDraft(string idDraft)
    {
        try
        {
            var lines = await _context.MaterialRequirementLines
                .Where(l => l.IdDraft == idDraft)
                .ToListAsync();

            _context.MaterialRequirementLines.RemoveRange(lines);
            await _context.SaveChangesAsync();
            return Ok(new { deleted = lines.Count });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal menghapus baris kebutuhan: {DescribeException(ex)}" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        try
        {
            var line = await _context.MaterialRequirementLines.FindAsync(id);
            if (line == null) return NotFound();

            _context.MaterialRequirementLines.Remove(line);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Baris kebutuhan berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Gagal menghapus baris kebutuhan: {DescribeException(ex)}" });
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
