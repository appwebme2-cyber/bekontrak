using System.Security.Claims;
using RefineryContractAPI.Data;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Services;

/// <summary>
/// Mencatat pemakaian token setiap panggilan Claude (chatbot dan ekstraksi) beserta estimasi biayanya,
/// untuk halaman admin "Pemakaian AI". Biaya dihitung dari tabel harga di bawah, jadi angkanya
/// perkiraan; tagihan resmi tetap di Claude Console. Kegagalan mencatat tidak pernah menggagalkan fitur.
/// </summary>
public class AiUsageTracker
{
    /// <summary>Harga USD per 1 juta token: input, output, baca cache, tulis cache (5 menit = 1,25x input).</summary>
    public record ModelPrice(decimal Input, decimal Output, decimal CacheRead, decimal CacheWrite);

    private static readonly Dictionary<string, ModelPrice> Prices = new(StringComparer.OrdinalIgnoreCase)
    {
        ["claude-opus-5-5"] = new(4.00m, 20.00m, 0.20m, 5.00m),
        ["claude-sonnet-5-5"] = new(2.00m, 10.00m, 0.20m, 2.50m),
        ["claude-haiku-5-5"] = new(0.10m, 0.50m, 0.01m, 0.125m),
        ["claude-fable-5-1"] = new(10.00m, 50.00m, 0.25m, 12.50m),
    };

    // Model yang tidak ada di tabel dihitung dengan harga Opus (lebih aman memperkirakan lebih mahal)
    private static readonly ModelPrice FallbackPrice = Prices["claude-opus-5-5"];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContext;

    public AiUsageTracker(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContext)
    {
        _scopeFactory = scopeFactory;
        _httpContext = httpContext;
    }

    public static bool IsKnownModel(string model) => Prices.ContainsKey(model);

    public static decimal EstimateCost(string model, long input, long output, long cacheRead, long cacheWrite)
    {
        var p = Prices.TryGetValue(model, out var price) ? price : FallbackPrice;
        return (input * p.Input + output * p.Output + cacheRead * p.CacheRead + cacheWrite * p.CacheWrite) / 1_000_000m;
    }

    /// <param name="requestId">Sama untuk semua panggilan API dalam satu pertanyaan/ekstraksi.</param>
    public async Task RecordAsync(string requestId, string fitur, string model,
        long? inputTokens, long? outputTokens, long? cacheReadTokens, long? cacheWriteTokens)
    {
        try
        {
            long input = inputTokens ?? 0, output = outputTokens ?? 0, cacheRead = cacheReadTokens ?? 0, cacheWrite = cacheWriteTokens ?? 0;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AiUsageLogs.Add(new AiUsageLog
            {
                IdPermintaan = requestId,
                IdUser = _httpContext.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier),
                Fitur = fitur,
                Model = model,
                InputTokens = input,
                OutputTokens = output,
                CacheReadTokens = cacheRead,
                CacheWriteTokens = cacheWrite,
                BiayaUsd = EstimateCost(model, input, output, cacheRead, cacheWrite),
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AiUsage] Gagal mencatat pemakaian: {ex.Message}");
        }
    }
}
