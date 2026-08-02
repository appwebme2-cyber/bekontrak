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
public class VendorsController : ControllerBase
{
    private readonly AppDbContext _context;

    public VendorsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var vendors = await _context.Vendors
            .OrderBy(v => v.NamaVendor)
            .Select(v => new VendorDto
            {
                IdVendor = v.IdVendor,
                NamaVendor = v.NamaVendor,
                Npwp = v.Npwp,
                Alamat = v.Alamat,
                PicNama = v.PicNama,
                PicKontak = v.PicKontak,
                StatusVendor = v.StatusVendor,
                Score = v.Score,
                CreatedAt = v.CreatedAt,
                UpdatedAt = v.UpdatedAt
            }).ToListAsync();

        return Ok(vendors);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var v = await _context.Vendors.FindAsync(id);
        if (v == null) return NotFound();

        return Ok(new VendorDto
        {
            IdVendor = v.IdVendor,
            NamaVendor = v.NamaVendor,
            Npwp = v.Npwp,
            Alamat = v.Alamat,
            PicNama = v.PicNama,
            PicKontak = v.PicKontak,
            StatusVendor = v.StatusVendor,
            Score = v.Score,
            CreatedAt = v.CreatedAt,
            UpdatedAt = v.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateVendorDto dto)
    {
        var vendor = new Vendor
        {
            NamaVendor = dto.NamaVendor,
            Npwp = dto.Npwp,
            Alamat = dto.Alamat,
            PicNama = dto.PicNama,
            PicKontak = dto.PicKontak,
            StatusVendor = dto.StatusVendor,
            Score = dto.Score
        };

        _context.Vendors.Add(vendor);
        await _context.SaveChangesAsync();
        return Ok(vendor);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdateVendorDto dto)
    {
        var vendor = await _context.Vendors.FindAsync(id);
        if (vendor == null) return NotFound();

        vendor.NamaVendor = dto.NamaVendor;
        vendor.Npwp = dto.Npwp;
        vendor.Alamat = dto.Alamat;
        vendor.PicNama = dto.PicNama;
        vendor.PicKontak = dto.PicKontak;
        vendor.StatusVendor = dto.StatusVendor;
        vendor.Score = dto.Score;
        vendor.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(vendor);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var vendor = await _context.Vendors.FindAsync(id);
        if (vendor == null) return NotFound();

        _context.Vendors.Remove(vendor);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Vendor berhasil dihapus" });
    }
}
