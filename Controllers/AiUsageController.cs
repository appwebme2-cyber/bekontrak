using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;
using RefineryContractAPI.Services;

namespace RefineryContractAPI.Controllers;

/// <summary>
/// Halaman admin "Pemakaian AI": estimasi biaya Claude dari ai_usage_log dan perkiraan sisa saldo
/// (total top-up yang dicatat admin dikurangi total biaya). Semua angka perkiraan.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "admin")]
public class AiUsageController : ControllerBase
{
    private const decimal DefaultKursIdr = 16500m;

    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public AiUsageController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    // GET /api/AiUsage/summary?days=30
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] int days = 30)
    {
        days = Math.Clamp(days, 7, 90);

        // Batas hari/bulan mengikuti WIB (UTC+7); data disimpan dalam UTC
        var nowWib = DateTime.UtcNow.AddHours(7);
        var todayStartUtc = nowWib.Date.AddHours(-7);
        var monthStartUtc = new DateTime(nowWib.Year, nowWib.Month, 1).AddHours(-7);
        var periodStartUtc = nowWib.Date.AddDays(-(days - 1)).AddHours(-7);

        var totalBiaya = await _context.AiUsageLogs.SumAsync(l => (decimal?)l.BiayaUsd) ?? 0m;
        var totalTopup = await _context.AiCreditTopups.SumAsync(t => (decimal?)t.JumlahUsd) ?? 0m;
        var biayaHariIni = await _context.AiUsageLogs.Where(l => l.CreatedAt >= todayStartUtc).SumAsync(l => (decimal?)l.BiayaUsd) ?? 0m;
        var biayaBulanIni = await _context.AiUsageLogs.Where(l => l.CreatedAt >= monthStartUtc).SumAsync(l => (decimal?)l.BiayaUsd) ?? 0m;
        var pencatatanSejak = await _context.AiUsageLogs.MinAsync(l => (DateTime?)l.CreatedAt);

        var logs = await _context.AiUsageLogs.AsNoTracking()
            .Where(l => l.CreatedAt >= periodStartUtc)
            .Select(l => new { l.CreatedAt, l.IdPermintaan, l.IdUser, l.Fitur, l.Model, l.InputTokens, l.OutputTokens, l.CacheReadTokens, l.BiayaUsd })
            .ToListAsync();

        // Grafik harian: semua hari dalam periode tampil, termasuk yang kosong
        var perHari = logs
            .GroupBy(l => l.CreatedAt.AddHours(7).Date)
            .ToDictionary(g => g.Key, g => new { biaya = g.Sum(l => l.BiayaUsd), permintaan = g.Select(l => l.IdPermintaan).Distinct().Count() });
        var harian = Enumerable.Range(0, days)
            .Select(i => nowWib.Date.AddDays(-(days - 1) + i))
            .Select(d => new
            {
                tanggal = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                biayaUsd = perHari.TryGetValue(d, out var v) ? Math.Round(v.biaya, 4) : 0m,
                permintaan = perHari.TryGetValue(d, out var w) ? w.permintaan : 0,
            })
            .ToList();

        var perFitur = logs
            .GroupBy(l => l.Fitur)
            .Select(g => new { fitur = g.Key, biayaUsd = Math.Round(g.Sum(l => l.BiayaUsd), 4), permintaan = g.Select(l => l.IdPermintaan).Distinct().Count() })
            .OrderByDescending(x => x.biayaUsd)
            .ToList();

        var perModel = logs
            .GroupBy(l => l.Model)
            .Select(g => new
            {
                model = g.Key,
                biayaUsd = Math.Round(g.Sum(l => l.BiayaUsd), 4),
                inputTokens = g.Sum(l => l.InputTokens + l.CacheReadTokens),
                outputTokens = g.Sum(l => l.OutputTokens),
                hargaDiketahui = AiUsageTracker.IsKnownModel(g.Key),
            })
            .OrderByDescending(x => x.biayaUsd)
            .ToList();

        var userIds = logs.Where(l => l.IdUser != null).Select(l => l.IdUser!).Distinct().ToList();
        var names = await _context.Profiles.IgnoreQueryFilters().AsNoTracking() // nama Super Admin (tanpa RU) tetap tampil
            .Where(p => userIds.Contains(p.Id))
            .Select(p => new { p.Id, Nama = p.FullName ?? p.Email })
            .ToDictionaryAsync(p => p.Id, p => p.Nama);

        var perUser = logs
            .GroupBy(l => l.IdUser ?? "")
            .Select(g => new
            {
                nama = g.Key == "" ? "(tidak diketahui)" : names.GetValueOrDefault(g.Key, "(pengguna dihapus)"),
                biayaUsd = Math.Round(g.Sum(l => l.BiayaUsd), 4),
                permintaan = g.Select(l => l.IdPermintaan).Distinct().Count(),
            })
            .OrderByDescending(x => x.biayaUsd)
            .Take(10)
            .ToList();

        var topups = await _context.AiCreditTopups.AsNoTracking()
            .OrderByDescending(t => t.Tanggal)
            .Select(t => new { id = t.IdTopup, jumlahUsd = t.JumlahUsd, tanggal = t.Tanggal, catatan = t.Catatan })
            .ToListAsync();

        var sisa = totalTopup - totalBiaya;
        // Peringatan bila sisa di bawah 10% dari total top-up (minimal USD 5)
        var batasPeringatan = Math.Max(5m, totalTopup * 0.10m);

        return Ok(new
        {
            kursIdr = KursIdr(),
            totalTopupUsd = totalTopup,
            totalBiayaUsd = Math.Round(totalBiaya, 4),
            sisaUsd = Math.Round(sisa, 4),
            saldoMenipis = totalTopup > 0 && sisa <= batasPeringatan,
            biayaHariIniUsd = Math.Round(biayaHariIni, 4),
            biayaBulanIniUsd = Math.Round(biayaBulanIni, 4),
            pencatatanSejak,
            periodeHari = days,
            harian,
            perFitur,
            perModel,
            perUser,
            topups,
        });
    }

    // POST /api/AiUsage/topup
    [HttpPost("topup")]
    public async Task<IActionResult> AddTopup([FromBody] AiTopupDto dto)
    {
        if (dto.JumlahUsd <= 0 || dto.JumlahUsd > 100000)
            return BadRequest(new { message = "Jumlah top-up harus antara 0 dan 100.000 USD." });

        var topup = new AiCreditTopup
        {
            JumlahUsd = Math.Round(dto.JumlahUsd, 2),
            Tanggal = dto.Tanggal ?? DateTime.UtcNow,
            Catatan = string.IsNullOrWhiteSpace(dto.Catatan) ? null : dto.Catatan.Trim(),
            CreatedBy = User.FindFirstValue(ClaimTypes.NameIdentifier),
            CreatedAt = DateTime.UtcNow,
        };
        _context.AiCreditTopups.Add(topup);
        await _context.SaveChangesAsync();
        return Ok(new { id = topup.IdTopup });
    }

    // DELETE /api/AiUsage/topup/{id}
    [HttpDelete("topup/{id}")]
    public async Task<IActionResult> DeleteTopup(string id)
    {
        var deleted = await _context.AiCreditTopups.Where(t => t.IdTopup == id).ExecuteDeleteAsync();
        return deleted == 0 ? NotFound(new { message = "Data top-up tidak ditemukan." }) : Ok(new { message = "Top-up dihapus." });
    }

    // Kurs USD ke Rupiah untuk tampilan perkiraan; ubah lewat env var AiUsage__KursIdr
    private decimal KursIdr() =>
        decimal.TryParse(_config["AiUsage:KursIdr"], NumberStyles.Number, CultureInfo.InvariantCulture, out var k) && k > 0 ? k : DefaultKursIdr;
}
