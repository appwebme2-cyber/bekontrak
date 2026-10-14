using System.Security.Claims;
using Anthropic.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Services;

namespace RefineryContractAPI.Controllers;

/// <summary>
/// Chatbot internal MAESTRO. Hanya untuk role internal (bukan External/Vendor) dan dibatasi
/// jumlah pertanyaan per user per hari (env var Chat__DailyLimit, default 30).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private const int DefaultDailyLimit = 30;
    private const int MaxQuestionLength = 2000;

    private readonly AppDbContext _context;
    private readonly ChatService _chat;
    private readonly IConfiguration _config;

    public ChatController(AppDbContext context, ChatService chat, IConfiguration config)
    {
        _context = context;
        _chat = chat;
        _config = config;
    }

    // GET /api/Chat/quota
    [HttpGet("quota")]
    public async Task<IActionResult> GetQuota()
    {
        if (!IsInternal()) return Forbidden();

        var limit = DailyLimit();
        var used = await GetUsageAsync(UserId(), Today());
        return Ok(new { limit, terpakai = used, sisa = Math.Max(limit - used, 0) });
    }

    // POST /api/Chat
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatRequestDto dto, CancellationToken ct)
    {
        if (!IsInternal()) return Forbidden();

        var question = (dto.Question ?? string.Empty).Trim();
        if (question.Length == 0)
            return BadRequest(new { message = "Pertanyaan tidak boleh kosong." });
        if (question.Length > MaxQuestionLength)
            return BadRequest(new { message = $"Pertanyaan terlalu panjang (maksimal {MaxQuestionLength} karakter)." });

        var userId = UserId();
        var today = Today();
        var limit = DailyLimit();
        var used = await GetUsageAsync(userId, today);
        if (used >= limit)
            return StatusCode(429, new { message = $"Batas {limit} pertanyaan per hari sudah tercapai. Silakan coba lagi besok.", sisa = 0 });

        try
        {
            var history = (dto.History ?? new List<ChatHistoryDto>())
                .Select(h => new ChatHistoryItem(h.Role, h.Content))
                .ToList();

            var answer = await _chat.AskAsync(question, history, ct);

            // Hanya pertanyaan yang berhasil dijawab yang mengurangi kuota
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_chat_usage (id_user, tanggal, jumlah) VALUES ({userId}, {today}, 1)
                ON CONFLICT (id_user, tanggal) DO UPDATE SET jumlah = ai_chat_usage.jumlah + 1", ct);

            return Ok(new { answer = answer.Answer, raw = answer.Raw, sisa = Math.Max(limit - used - 1, 0) });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
        catch (AnthropicRateLimitException)
        {
            return StatusCode(503, new { message = "Layanan AI sedang sibuk. Coba lagi beberapa saat lagi." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Chat] {ex}");
            return StatusCode(500, new { message = "Terjadi kesalahan saat memproses pertanyaan. Coba lagi." });
        }
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    // Internal = semua role selain External (dan Vendor, nama lamanya)
    private bool IsInternal()
    {
        var role = (User.FindFirstValue(ClaimTypes.Role) ?? string.Empty).Trim().ToLowerInvariant();
        return role is not ("external" or "vendor") && !string.IsNullOrEmpty(UserId());
    }

    private ObjectResult Forbidden() =>
        StatusCode(403, new { message = "Chatbot hanya tersedia untuk pengguna internal." });

    private int DailyLimit() =>
        int.TryParse(_config["Chat:DailyLimit"], out var n) && n > 0 ? n : DefaultDailyLimit;

    // Batas harian mengikuti tanggal WIB
    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));

    private async Task<int> GetUsageAsync(string userId, DateOnly today)
    {
        var rows = await _context.Database
            .SqlQuery<int>($"SELECT jumlah AS \"Value\" FROM ai_chat_usage WHERE id_user = {userId} AND tanggal = {today}")
            .ToListAsync();
        return rows.FirstOrDefault();
    }
}
