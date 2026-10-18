using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;

namespace RefineryContractAPI.Services;

/// <summary>
/// Ringkasan harian ke HP setiap pagi (setelah jam 07.00 WIB): KOM terlambat, dokumen menunggu
/// approval, dan tagihan mandek ≥30 hari — aturan sama dengan lonceng notifikasi MAESTRO web.
/// Hanya dikirim bila ada yang perlu diperhatikan, dan paling banyak sekali sehari
/// (dicatat di tabel push_log, aman walau server restart).
/// </summary>
public class PushDigestService : BackgroundService
{
    private const int JamKirimWib = 7;

    private readonly IServiceScopeFactory _scopes;
    private readonly ExpoPushService _push;
    private readonly RolePermissionService _permissions;

    public PushDigestService(IServiceScopeFactory scopes, ExpoPushService push, RolePermissionService permissions)
    {
        _scopes = scopes;
        _push = push;
        _permissions = permissions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // beri waktu startup & migrasi selesai
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await KirimJikaWaktunyaAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.WriteLine($"[Push] Warning (ringkasan harian): {ex.Message}");
                }
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // server berhenti
        }
    }

    private async Task KirimJikaWaktunyaAsync(CancellationToken ct)
    {
        var nowWib = DateTime.UtcNow.AddHours(7);
        if (nowWib.Hour < JamKirimWib) return;

        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Kunci per hari: hanya proses pertama yang berhasil mencatat yang mengirim
        var kunci = $"digest-{nowWib:yyyy-MM-dd}";
        var baru = await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO push_log (kunci) VALUES ({0}) ON CONFLICT (kunci) DO NOTHING", new object[] { kunci }, ct);
        if (baru == 0) return;

        var users = await db.Profiles.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.IsActive && db.PushTokens.Any(t => t.IdUser == p.Id))
            .Select(p => new { p.Id, p.Role, p.IdRu })
            .ToListAsync(ct);
        if (users.Count == 0) return;

        var now = DateTime.UtcNow;
        var batasMandek = now.AddDays(-30);
        var komTerlambat = await db.Kontraks.IgnoreQueryFilters().AsNoTracking()
            .Where(k => k.StatusKontrak == "Pre-KOM" && k.TanggalMaksimalKom != null && k.TanggalMaksimalKom < now)
            .Select(k => k.IdRu)
            .ToListAsync(ct);
        var dokumenPending = await db.DokumenApprovals.IgnoreQueryFilters().AsNoTracking()
            .Where(d => d.StatusApproval == "Pending")
            .Select(d => d.Kontrak!.IdRu)
            .ToListAsync(ct);
        var tagihanMandek = await db.Tagihans.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.UpdatedAt <= batasMandek
                && !EF.Functions.ILike(t.StatusTagihan, "%payment%")
                && !EF.Functions.ILike(t.StatusTagihan, "%selesai%"))
            .Select(t => t.Kontrak!.IdRu)
            .ToListAsync(ct);

        var tokens = await db.PushTokens.AsNoTracking()
            .Select(t => new { t.IdUser, t.Token })
            .ToListAsync(ct);

        var pesan = new List<PushPesan>();
        foreach (var u in users)
        {
            var role = (u.Role ?? string.Empty).Trim().ToLowerInvariant();
            if (role is "external" or "vendor") continue; // vendor tidak memantau KOM/tagihan

            int Hitung(List<string?> ruData) => ruData.Count(ru => PushNotifier.LihatRu(role, u.IdRu, ru));
            var kom = Hitung(komTerlambat);
            var dok = await _permissions.HasFlagAsync(role, "canApprovalDokumen") ? Hitung(dokumenPending) : 0;
            var tag = Hitung(tagihanMandek);
            if (kom + dok + tag == 0) continue;

            var bagian = new List<string>();
            if (kom > 0) bagian.Add($"{kom} KOM terlambat");
            if (dok > 0) bagian.Add($"{dok} dokumen menunggu approval");
            if (tag > 0) bagian.Add($"{tag} tagihan ≥30 hari di satu tahap");
            var isi = string.Join(" · ", bagian);

            pesan.AddRange(tokens.Where(t => t.IdUser == u.Id)
                .Select(t => new PushPesan(t.Token, "MAESTRO · Perlu perhatian hari ini", isi, "/notifikasi")));
        }

        await _push.KirimAsync(pesan, ct);
        Console.WriteLine($"[Push] Ringkasan harian terkirim ke {pesan.Count} HP");
    }
}
