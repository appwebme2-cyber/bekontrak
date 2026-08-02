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
public class PadiController : ControllerBase
{
    private readonly AppDbContext _context;
    public PadiController(AppDbContext context) => _context = context;

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var list = await _context.Padis
            .Include(p => p.Vendor)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PadiDto
            {
                IdPadi = p.IdPadi,
                NoPembelian = p.NoPembelian,
                Tanggal = p.Tanggal,
                JudulPembelian = p.JudulPembelian,
                NoPoPr = p.NoPoPr,
                Nilai = p.Nilai,
                IdVendor = p.IdVendor,
                LinkPembelian = p.LinkPembelian,
                Bagian = p.Bagian,
                DokumenPendukung = p.DokumenPendukung,
                StatusPurchase = p.StatusPurchase,
                TanggalBast = p.TanggalBast,
                TanggalSaGr = p.TanggalSaGr,
                TanggalInvoice = p.TanggalInvoice,
                TanggalPaymentApproval = p.TanggalPaymentApproval,
                TanggalPaid = p.TanggalPaid,
                CatatanStatus = p.CatatanStatus,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                Vendor = p.Vendor == null ? null : new VendorSummaryDto
                {
                    NamaVendor = p.Vendor.NamaVendor
                }
            }).ToListAsync();

        return Ok(list);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var p = await _context.Padis
            .Include(p => p.Vendor)
            .FirstOrDefaultAsync(p => p.IdPadi == id);

        if (p == null) return NotFound();

        return Ok(new PadiDto
        {
            IdPadi = p.IdPadi,
            NoPembelian = p.NoPembelian,
            Tanggal = p.Tanggal,
            JudulPembelian = p.JudulPembelian,
            NoPoPr = p.NoPoPr,
            Nilai = p.Nilai,
            IdVendor = p.IdVendor,
            LinkPembelian = p.LinkPembelian,
            Bagian = p.Bagian,
            DokumenPendukung = p.DokumenPendukung,
            StatusPurchase = p.StatusPurchase,
            TanggalBast = p.TanggalBast,
            TanggalSaGr = p.TanggalSaGr,
            TanggalInvoice = p.TanggalInvoice,
            TanggalPaymentApproval = p.TanggalPaymentApproval,
            TanggalPaid = p.TanggalPaid,
            CatatanStatus = p.CatatanStatus,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            Vendor = p.Vendor == null ? null : new VendorSummaryDto
            {
                NamaVendor = p.Vendor.NamaVendor
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreatePadiDto dto)
    {
        var padi = new Padi
        {
            NoPembelian = dto.NoPembelian,
            Tanggal = dto.Tanggal,
            JudulPembelian = dto.JudulPembelian,
            NoPoPr = dto.NoPoPr,
            Nilai = dto.Nilai,
            IdVendor = string.IsNullOrEmpty(dto.IdVendor) ? null : dto.IdVendor,
            LinkPembelian = dto.LinkPembelian,
            Bagian = dto.Bagian,
            DokumenPendukung = dto.DokumenPendukung,
            StatusPurchase = dto.StatusPurchase,
            TanggalBast = dto.TanggalBast,
            TanggalSaGr = dto.TanggalSaGr,
            TanggalInvoice = dto.TanggalInvoice,
            TanggalPaymentApproval = dto.TanggalPaymentApproval,
            TanggalPaid = dto.TanggalPaid,
            CatatanStatus = dto.CatatanStatus
        };

        _context.Padis.Add(padi);
        await _context.SaveChangesAsync();
        return Ok(padi);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] UpdatePadiDto dto)
    {
        var padi = await _context.Padis.FindAsync(id);
        if (padi == null) return NotFound();

        padi.NoPembelian = dto.NoPembelian;
        padi.Tanggal = dto.Tanggal;
        padi.JudulPembelian = dto.JudulPembelian;
        padi.NoPoPr = dto.NoPoPr;
        padi.Nilai = dto.Nilai;
        padi.IdVendor = string.IsNullOrEmpty(dto.IdVendor) ? null : dto.IdVendor;
        padi.LinkPembelian = dto.LinkPembelian;
        padi.Bagian = dto.Bagian;
        padi.DokumenPendukung = dto.DokumenPendukung;
        padi.StatusPurchase = dto.StatusPurchase;
        padi.TanggalBast = dto.TanggalBast;
        padi.TanggalSaGr = dto.TanggalSaGr;
        padi.TanggalInvoice = dto.TanggalInvoice;
        padi.TanggalPaymentApproval = dto.TanggalPaymentApproval;
        padi.TanggalPaid = dto.TanggalPaid;
        padi.CatatanStatus = dto.CatatanStatus;
        padi.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();
        return Ok(padi);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var padi = await _context.Padis.FindAsync(id);
        if (padi == null) return NotFound();

        _context.Padis.Remove(padi);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Data pembelian berhasil dihapus" });
    }
}