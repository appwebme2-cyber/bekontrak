using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;

namespace RefineryContractAPI.Services;

/// <summary>Satu notifikasi untuk satu HP. Url = halaman aplikasi yang dibuka saat notifikasi diketuk.</summary>
public record PushPesan(string Token, string Judul, string Isi, string? Url);

/// <summary>
/// Mengirim notifikasi push ke aplikasi MAESTRO Mobile lewat layanan push Expo.
/// Token HP yang sudah tidak terdaftar (aplikasi dihapus/izin dicabut) otomatis dibuang.
/// Env var opsional Expo__AccessToken bila "enhanced push security" diaktifkan di akun Expo.
/// </summary>
public class ExpoPushService
{
    private const string ExpoUrl = "https://exp.host/--/api/v2/push/send";

    private readonly IHttpClientFactory _http;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;

    public ExpoPushService(IHttpClientFactory http, IServiceScopeFactory scopes, IConfiguration config)
    {
        _http = http;
        _scopes = scopes;
        _config = config;
    }

    public async Task KirimAsync(IReadOnlyList<PushPesan> pesan, CancellationToken ct = default)
    {
        if (pesan.Count == 0) return;

        var client = _http.CreateClient();
        var accessToken = _config["Expo:AccessToken"];
        var tidakTerdaftar = new List<string>();

        foreach (var batch in pesan.Chunk(100)) // batas Expo: 100 pesan per permintaan
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, ExpoUrl)
            {
                Content = JsonContent.Create(batch.Select(p => new
                {
                    to = p.Token,
                    title = p.Judul,
                    body = p.Isi,
                    sound = "default",
                    channelId = "default",
                    data = new { url = p.Url },
                })),
            };
            if (!string.IsNullOrEmpty(accessToken))
                req.Headers.Authorization = new("Bearer", accessToken);

            try
            {
                using var res = await client.SendAsync(req, ct);
                var json = await res.Content.ReadAsStringAsync(ct);
                if (!res.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Push] Expo menolak ({(int)res.StatusCode}): {json}");
                    continue;
                }

                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var tiket) && tiket.ValueKind == JsonValueKind.Array)
                {
                    var i = 0;
                    foreach (var t in tiket.EnumerateArray())
                    {
                        if (i < batch.Length
                            && t.TryGetProperty("details", out var detail)
                            && detail.ValueKind == JsonValueKind.Object
                            && detail.TryGetProperty("error", out var err)
                            && err.GetString() == "DeviceNotRegistered")
                            tidakTerdaftar.Add(batch[i].Token);
                        i++;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"[Push] Gagal mengirim notifikasi: {ex.Message}");
            }
        }

        if (tidakTerdaftar.Count > 0)
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.PushTokens.Where(t => tidakTerdaftar.Contains(t.Token)).ExecuteDeleteAsync(ct);
        }
    }
}
