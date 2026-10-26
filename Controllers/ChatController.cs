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
/// Chatbot internal MAESTRO. Hanya untuk role di Chat__AllowedRoles (default semua role internal; tidak pernah
/// External/Vendor), wajib PIN (header X-Chat-Pin, sama dengan env var PIN fitur AI), dan dibatasi
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
    private readonly PinGuard _pinGuard;

    public ChatController(AppDbContext context, ChatService chat, IConfiguration config, PinGuard pinGuard)
    {
        _context = context;
        _chat = chat;
        _config = config;
        _pinGuard = pinGuard;
    }

    // POST /api/Chat/verify-pin  (membuka kunci chatbot di frontend)
    [HttpPost("verify-pin")]
    public IActionResult VerifyPin([FromBody] ChatPinDto dto)
    {
        if (!IsAllowed()) return Forbidden();

        var error = _pinGuard.Check(UserId(), dto.Pin);
        return error == null ? Ok(new { message = "PIN benar." }) : PinRejected(error.Value);
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
        if (CheckPin() is { } pinError) return PinRejected(pinError);

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
        if (CheckPin() is { } pinError) return PinRejected(pinError);

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
        if (CheckPin() is { } pinError) return PinRejected(pinError);

        var userId = UserId();
        // Pesan ikut terhapus lewat ON DELETE CASCADE
        var deleted = await _context.AiChatSessions
            .Where(s => s.IdSession == id && s.IdUser == userId)
            .ExecuteDeleteAsync();
        if (deleted == 0) return NotFound(new { message = "Percakapan tidak ditemukan." });

        return Ok(new { message = "Percakapan dihapus." });
    }

    // ==================== SUARA (ElevenLabs) ====================

    private const int MaxTeksSuara = 2500;
    // Suara bawaan ElevenLabs bila ElevenLabs__VoiceId tidak diisi; disarankan pilih suara Indonesia di Voice Library
    private const string DefaultVoiceId = "EXAVITQu4vr4xnSDxMaL";
    private const string DefaultModelId = "eleven_flash_v2_5";

    // POST /api/Chat/suara  (bacakan teks dengan suara natural ElevenLabs, hasil MP3)
    // Kunci di env var ElevenLabs__ApiKey. Bila kunci belum diisi, kuota habis, atau ElevenLabs gangguan, jawab 503
    // supaya tampilan memakai suara bawaan browser. Hasil disimpan di R2 per teks, jadi kalimat yang sama
    // (mis. ucapan saat menunggu) hanya sekali memakai kuota ElevenLabs.
    [HttpPost("suara")]
    public async Task<IActionResult> Suara([FromBody] ChatSuaraDto dto, [FromServices] IHttpClientFactory httpFactory,
        [FromServices] R2StorageService r2, CancellationToken ct)
    {
        if (!IsAllowed()) return Forbidden();
        if (CheckPin() is { } pinError) return PinRejected(pinError);

        var apiKey = _config["ElevenLabs:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return StatusCode(503, new { message = "Suara ElevenLabs belum dikonfigurasi." });

        var teks = (dto.Teks ?? string.Empty).Trim();
        if (teks.Length == 0) return BadRequest(new { message = "Teks kosong." });
        if (teks.Length > MaxTeksSuara) return BadRequest(new { message = "Teks terlalu panjang untuk suara ElevenLabs." });

        var voiceId = string.IsNullOrWhiteSpace(_config["ElevenLabs:VoiceId"]) ? DefaultVoiceId : _config["ElevenLabs:VoiceId"]!.Trim();
        var modelId = string.IsNullOrWhiteSpace(_config["ElevenLabs:ModelId"]) ? DefaultModelId : _config["ElevenLabs:ModelId"]!.Trim();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(teks))).ToLowerInvariant();
        var kunci = $"suara/{modelId}/{voiceId}/{hash}.mp3";

        // Sudah pernah dibuat: ambil dari R2 tanpa memakai kuota
        try
        {
            var (stream, _, _) = await r2.GetAsync(kunci);
            await using (stream)
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, ct);
                return File(ms.ToArray(), "audio/mpeg");
            }
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // belum ada di cache
        }

        byte[] audio;
        try
        {
            var http = httpFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(30);
            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.elevenlabs.io/v1/text-to-speech/{Uri.EscapeDataString(voiceId)}?output_format=mp3_44100_64");
            req.Headers.Add("xi-api-key", apiKey);
            // language_code tidak didukung model multilingual_v2; untuk model lain dipaksa Bahasa Indonesia
            object body = modelId.StartsWith("eleven_multilingual", StringComparison.OrdinalIgnoreCase)
                ? (object)new { text = teks, model_id = modelId }
                : new { text = teks, model_id = modelId, language_code = "id" };
            req.Content = JsonContent.Create(body);
            using var res = await http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode)
            {
                var detail = await res.Content.ReadAsStringAsync(ct);
                Console.WriteLine($"[Suara] ElevenLabs {(int)res.StatusCode}: {(detail.Length > 300 ? detail[..300] : detail)}");
                return StatusCode(503, new { message = "Suara ElevenLabs tidak tersedia (kuota habis atau gangguan)." });
            }
            audio = await res.Content.ReadAsByteArrayAsync(ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"[Suara] ElevenLabs gagal: {ex.Message}");
            return StatusCode(503, new { message = "Suara ElevenLabs tidak tersedia." });
        }

        try
        {
            using var simpan = new MemoryStream(audio);
            await r2.UploadAsync(simpan, kunci, "audio/mpeg");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Suara] Gagal menyimpan cache suara: {ex.Message}");
        }

        return File(audio, "audio/mpeg");
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

        if (CheckPin() is { } pinError)
            return ((pinError.Status, PinRejectedBody(pinError)), null);

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
    /// Default "internal" (semua role kecuali External/Vendor), atau daftar role,
    /// mis. "admin,manager,head_office". External/Vendor tidak pernah diizinkan.
    /// </summary>
    private bool IsAllowed()
    {
        if (string.IsNullOrEmpty(UserId())) return false;

        var role = (User.FindFirstValue(ClaimTypes.Role) ?? string.Empty).Trim().ToLowerInvariant();
        if (role is "external" or "vendor") return false;

        var setting = _config["Chat:AllowedRoles"];
        var allowed = (string.IsNullOrWhiteSpace(setting) ? "internal" : setting)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.ToLowerInvariant())
            .ToHashSet();

        return allowed.Contains("internal") || allowed.Contains(role);
    }

    private const string NotAllowedMessage = "Chatbot belum tersedia untuk akun Anda.";

    private ObjectResult Forbidden() => StatusCode(403, new { message = NotAllowedMessage });

    // PIN dikirim frontend lewat header di setiap permintaan chatbot (kecuali cek kuota)
    private (int Status, string Message)? CheckPin() =>
        _pinGuard.Check(UserId(), Request.Headers["X-Chat-Pin"].ToString());

    // pinInvalid memberi tahu frontend untuk meminta PIN lagi (beda dengan 403 karena role)
    private static object PinRejectedBody((int Status, string Message) error) =>
        new { message = error.Message, pinInvalid = error.Status is 403 or 429 };

    private ObjectResult PinRejected((int Status, string Message) error) =>
        StatusCode(error.Status, PinRejectedBody(error));

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
