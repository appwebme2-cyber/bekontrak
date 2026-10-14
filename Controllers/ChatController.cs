using System.Security.Claims;
using System.Text.Json;
using Anthropic.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;
using RefineryContractAPI.DTOs;
using RefineryContractAPI.Models;
using RefineryContractAPI.Services;

namespace RefineryContractAPI.Controllers;

/// <summary>
/// Chatbot internal MAESTRO. Hanya untuk role di Chat__AllowedRoles (default admin; tidak pernah External/Vendor) dan dibatasi
/// jumlah pertanyaan per user per hari (env var Chat__DailyLimit, default 30).
/// Percakapan disimpan per user (ai_chat_session / ai_chat_message) dan hanya bisa dibaca pemiliknya.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private const int DefaultDailyLimit = 30;
    private const int MaxQuestionLength = 2000;
    private const int MaxSessionsPerUser = 50;
    private const int HistoryMessages = 12;

    private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

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
        if (!IsAllowed()) return Forbidden();

        var limit = DailyLimit();
        var used = await GetUsageAsync(UserId(), Today());
        return Ok(new { limit, terpakai = used, sisa = Math.Max(limit - used, 0) });
    }

    // ==================== RIWAYAT PERCAKAPAN ====================

    // GET /api/Chat/sessions
    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        if (!IsAllowed()) return Forbidden();

        var userId = UserId();
        var sessions = await _context.AiChatSessions.AsNoTracking()
            .Where(s => s.IdUser == userId)
            .OrderByDescending(s => s.UpdatedAt)
            .Take(MaxSessionsPerUser)
            .Select(s => new { id = s.IdSession, judul = s.Judul, createdAt = s.CreatedAt, updatedAt = s.UpdatedAt })
            .ToListAsync();
        return Ok(sessions);
    }

    // GET /api/Chat/sessions/{id}
    [HttpGet("sessions/{id}")]
    public async Task<IActionResult> GetSession(string id)
    {
        if (!IsAllowed()) return Forbidden();

        var userId = UserId();
        var session = await _context.AiChatSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.IdSession == id && s.IdUser == userId);
        if (session == null) return NotFound(new { message = "Percakapan tidak ditemukan." });

        var messages = await _context.AiChatMessages.AsNoTracking()
            .Where(m => m.IdSession == id)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { id = m.IdMessage, role = m.Role, content = m.Content, createdAt = m.CreatedAt })
            .ToListAsync();

        return Ok(new { id = session.IdSession, judul = session.Judul, updatedAt = session.UpdatedAt, messages });
    }

    // DELETE /api/Chat/sessions/{id}
    [HttpDelete("sessions/{id}")]
    public async Task<IActionResult> DeleteSession(string id)
    {
        if (!IsAllowed()) return Forbidden();

        var userId = UserId();
        // Pesan ikut terhapus lewat ON DELETE CASCADE
        var deleted = await _context.AiChatSessions
            .Where(s => s.IdSession == id && s.IdUser == userId)
            .ExecuteDeleteAsync();
        if (deleted == 0) return NotFound(new { message = "Percakapan tidak ditemukan." });

        return Ok(new { message = "Percakapan dihapus." });
    }

    // ==================== TANYA ====================

    // POST /api/Chat  (jawaban sekaligus)
    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] ChatRequestDto dto, CancellationToken ct)
    {
        var (error, question) = await ValidateAsync(dto);
        if (error != null) return StatusCode(error.Value.Status, error.Value.Body);

        var (status, body) = await RunAsync(dto, question!, null, ct);
        return StatusCode(status, body);
    }

    // POST /api/Chat/stream  (Server-Sent Events: "status" selama proses, lalu "done" atau "error")
    [HttpPost("stream")]
    public async Task AskStream([FromBody] ChatRequestDto dto, CancellationToken ct)
    {
        var (error, question) = await ValidateAsync(dto);
        if (error != null)
        {
            Response.StatusCode = error.Value.Status;
            await Response.WriteAsJsonAsync(error.Value.Body, ct);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        async Task Send(string evt, object data)
        {
            await Response.WriteAsync($"event: {evt}\ndata: {JsonSerializer.Serialize(data, EventJson)}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }

        var (status, body) = await RunAsync(dto, question!, text => Send("status", new { text }), ct);
        await Send(status == 200 ? "done" : "error", body);
    }

    /// <summary>Cek hak akses, isi pertanyaan, dan kuota sebelum memanggil AI.</summary>
    private async Task<((int Status, object Body)? Error, string? Question)> ValidateAsync(ChatRequestDto dto)
    {
        if (!IsAllowed())
            return ((403, new { message = NotAllowedMessage }), null);

        var question = (dto.Question ?? string.Empty).Trim();
        if (question.Length == 0)
            return ((400, new { message = "Pertanyaan tidak boleh kosong." }), null);
        if (question.Length > MaxQuestionLength)
            return ((400, new { message = $"Pertanyaan terlalu panjang (maksimal {MaxQuestionLength} karakter)." }), null);

        var limit = DailyLimit();
        var used = await GetUsageAsync(UserId(), Today());
        if (used >= limit)
            return ((429, new { message = $"Batas {limit} pertanyaan per hari sudah tercapai. Silakan coba lagi besok.", sisa = 0 }), null);

        return (null, question);
    }

    private async Task<(int Status, object Body)> RunAsync(ChatRequestDto dto, string question, Func<string, Task>? onStatus, CancellationToken ct)
    {
        var userId = UserId();
        var today = Today();
        var limit = DailyLimit();

        try
        {
            // Riwayat diambil dari database kalau percakapan sudah tersimpan; selain itu dari kiriman frontend
            AiChatSession? session = null;
            if (!string.IsNullOrWhiteSpace(dto.SessionId))
                session = await _context.AiChatSessions.FirstOrDefaultAsync(s => s.IdSession == dto.SessionId && s.IdUser == userId, ct);

            List<ChatHistoryItem> history;
            if (session != null)
            {
                var recent = await _context.AiChatMessages.AsNoTracking()
                    .Where(m => m.IdSession == session.IdSession)
                    .OrderByDescending(m => m.CreatedAt)
                    .Take(HistoryMessages)
                    .Select(m => new { m.Role, m.Content })
                    .ToListAsync(ct);
                recent.Reverse();
                history = recent.Select(m => new ChatHistoryItem(m.Role, m.Content)).ToList();
            }
            else
            {
                history = (dto.History ?? new List<ChatHistoryDto>())
                    .Select(h => new ChatHistoryItem(h.Role, h.Content))
                    .ToList();
            }

            var context = dto.Context == null
                ? null
                : new ChatPageContext(dto.Context.Path, dto.Context.PageTitle, dto.Context.KontrakId, dto.Context.TagihanId);

            var answer = await _chat.AskAsync(question, history, context, onStatus, ct);

            // Simpan percakapan
            var now = DateTime.UtcNow;
            if (session == null)
            {
                session = new AiChatSession
                {
                    IdUser = userId,
                    Judul = question.Length > 80 ? question[..80] + "…" : question,
                    CreatedAt = now,
                };
                _context.AiChatSessions.Add(session);
            }
            session.UpdatedAt = now;
            // Simpan sesi lebih dulu: EF tidak tahu relasi FK ai_chat_message -> ai_chat_session,
            // jadi urutan insert dalam satu SaveChanges tidak dijamin.
            await _context.SaveChangesAsync(ct);

            _context.AiChatMessages.Add(new AiChatMessage { IdSession = session.IdSession, Role = "user", Content = question, CreatedAt = now });
            _context.AiChatMessages.Add(new AiChatMessage { IdSession = session.IdSession, Role = "assistant", Content = answer.Raw, CreatedAt = now.AddMilliseconds(1) });
            await _context.SaveChangesAsync(ct);

            // Simpan maksimal 50 percakapan terbaru per user
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                DELETE FROM ai_chat_session WHERE id_user = {userId} AND id_session NOT IN (
                    SELECT id_session FROM ai_chat_session WHERE id_user = {userId} ORDER BY updated_at DESC LIMIT {MaxSessionsPerUser})", ct);

            // Hanya pertanyaan yang berhasil dijawab yang mengurangi kuota
            await _context.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ai_chat_usage (id_user, tanggal, jumlah) VALUES ({userId}, {today}, 1)
                ON CONFLICT (id_user, tanggal) DO UPDATE SET jumlah = ai_chat_usage.jumlah + 1", ct);

            var used = await GetUsageAsync(userId, today);
            return (200, new { answer = answer.Answer, raw = answer.Raw, sisa = Math.Max(limit - used, 0), sessionId = session.IdSession });
        }
        catch (InvalidOperationException ex)
        {
            return (503, new { message = ex.Message });
        }
        catch (AnthropicRateLimitException)
        {
            return (503, new { message = "Layanan AI sedang sibuk. Coba lagi beberapa saat lagi." });
        }
        catch (OperationCanceledException)
        {
            return (499, new { message = "Permintaan dibatalkan." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Chat] {ex}");
            return (500, new { message = "Terjadi kesalahan saat memproses pertanyaan. Coba lagi." });
        }
    }

    private string UserId() => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    /// <summary>
    /// Role yang boleh memakai chatbot, dari env var Chat__AllowedRoles (dipisah koma).
    /// Default "admin" (uji coba). Isi "internal" untuk semua role kecuali External/Vendor,
    /// atau daftar role, mis. "admin,manager,head_office". External/Vendor tidak pernah diizinkan.
    /// </summary>
    private bool IsAllowed()
    {
        if (string.IsNullOrEmpty(UserId())) return false;

        var role = (User.FindFirstValue(ClaimTypes.Role) ?? string.Empty).Trim().ToLowerInvariant();
        if (role is "external" or "vendor") return false;

        var setting = _config["Chat:AllowedRoles"];
        var allowed = (string.IsNullOrWhiteSpace(setting) ? "admin" : setting)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.ToLowerInvariant())
            .ToHashSet();

        return allowed.Contains("internal") || allowed.Contains(role);
    }

    private const string NotAllowedMessage = "Chatbot belum tersedia untuk akun Anda.";

    private ObjectResult Forbidden() => StatusCode(403, new { message = NotAllowedMessage });

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
