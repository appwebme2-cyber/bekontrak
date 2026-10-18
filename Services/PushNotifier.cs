using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;

namespace RefineryContractAPI.Services;

/// <summary>
/// Menentukan siapa menerima notifikasi push dan isinya. Data dibaca tanpa saringan RU (proses ini
/// berjalan di latar belakang, bukan atas nama satu pengguna), lalu penerima disaring manual:
/// pengguna RU hanya menerima kabar RU-nya, Super Admin & Head Office semua RU.
/// </summary>
public class PushNotifier
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ExpoPushService _push;
    private readonly RolePermissionService _permissions;

    public PushNotifier(IServiceScopeFactory scopes, ExpoPushService push, RolePermissionService permissions)
    {
        _scopes = scopes;
        _push = push;
        _permissions = permissions;
    }

    /// <summary>Kabari pengguna yang boleh approval di RU kontrak tsb bahwa ada dokumen baru. Tidak menunda request.</summary>
    public void DokumenBaru(string idKontrak, string namaDokumen)
    {
        // Jangan bawa konteks request pengunggah (saringan RU & login) ke proses latar belakang
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var kontrak = await db.Kontraks.IgnoreQueryFilters().AsNoTracking()
                        .Where(k => k.IdKontrak == idKontrak)
                        .Select(k => new { k.IdRu, k.JudulKontrak })
                        .FirstOrDefaultAsync();
                    if (kontrak == null) return;

                    var penerima = await PenerimaAsync(db, kontrak.IdRu, "canApprovalDokumen");
                    var tokens = await db.PushTokens.AsNoTracking()
                        .Where(t => penerima.Contains(t.IdUser))
                        .Select(t => t.Token)
                        .ToListAsync();

                    var isi = $"{namaDokumen} · {Ringkas(kontrak.JudulKontrak)}";
                    await _push.KirimAsync(tokens.Select(t => new PushPesan(t, "Dokumen baru menunggu approval", isi, "/approval")).ToList());
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Push] Warning (dokumen baru): {ex.Message}");
                }
            });
        }
    }

    /// <summary>Pengguna aktif yang punya token HP, bisa melihat RU tsb, dan punya hak <paramref name="flag"/>.</summary>
    private async Task<List<string>> PenerimaAsync(AppDbContext db, string? idRu, string flag)
    {
        var users = await db.Profiles.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.IsActive && db.PushTokens.Any(t => t.IdUser == p.Id))
            .Select(p => new { p.Id, p.Role, p.IdRu })
            .ToListAsync();

        var hasil = new List<string>();
        foreach (var u in users)
        {
            var role = (u.Role ?? string.Empty).Trim().ToLowerInvariant();
            if (!LihatRu(role, u.IdRu, idRu)) continue;
            if (!await _permissions.HasFlagAsync(role, flag)) continue;
            hasil.Add(u.Id);
        }
        return hasil;
    }

    /// <summary>Aturan sama dengan CurrentRu: Super Admin (admin tanpa RU) & Head Office melihat semua RU.</summary>
    public static bool LihatRu(string role, string? userRu, string? dataRu)
    {
        var lintasRu = (role == "admin" && string.IsNullOrEmpty(userRu)) || role == "head_office";
        if (lintasRu) return true;
        var ru = string.IsNullOrEmpty(userRu) ? CurrentRu.DefaultRu : userRu;
        return string.Equals(ru, dataRu, StringComparison.OrdinalIgnoreCase);
    }

    public static string Ringkas(string judul) => judul.Length > 70 ? judul[..67] + "..." : judul;
}
