using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RefineryContractAPI.Data;

namespace RefineryContractAPI.Services;

/// <summary>
/// Membaca matriks hak akses role yang sama dengan yang dipakai frontend (konfigurasi_sistem,
/// nama "Role_Permission_Matrix") supaya backend menolak penulisan data dari akun yang memang
/// tidak punya hak tulis (Head Office, Guest, External), tidak hanya disembunyikan di tampilan.
/// Nilai default meniru DEFAULT_ROLE_PERMISSIONS di useRolePermissionsConfig.tsx.
/// </summary>
public class RolePermissionService
{
    private const string SettingName = "Role_Permission_Matrix";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private static readonly string[] FlagNames =
    {
        "canCreate", "canEdit", "canDelete", "canManageUsers",
        "canManageVendors", "canUploadDokumen", "canApprovalDokumen",
    };

    private static readonly HashSet<string> StaffRoles = new() { "manager", "section_head", "supervisor", "technician" };
    private static readonly HashSet<string> KnownRoles = new()
    {
        "manager", "section_head", "supervisor", "technician", "head_office", "external", "guest",
    };
    private static readonly Dictionary<string, string> LegacyAliases = new()
    {
        ["pic"] = "manager", ["vendor"] = "external", ["viewer"] = "guest", ["user"] = "guest",
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Dictionary<string, Dictionary<string, bool>> _saved = new();
    private DateTime _loadedAt = DateTime.MinValue;

    public RolePermissionService(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>True kalau role punya minimal satu hak tulis (tambah/ubah/hapus/upload/approval/kelola).</summary>
    public async Task<bool> CanWriteAsync(string? role)
    {
        foreach (var flag in FlagNames)
            if (await HasFlagAsync(role, flag)) return true;
        return false;
    }

    /// <summary>True kalau role punya hak tertentu (mis. "canDelete") menurut Pengaturan Role. Admin selalu boleh.</summary>
    public async Task<bool> HasFlagAsync(string? role, string flag)
    {
        if (string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)) return true;

        var resolved = ResolveRole(role);
        var saved = await GetSavedAsync();
        saved.TryGetValue(resolved, out var roleFlags);
        return roleFlags != null && roleFlags.TryGetValue(flag, out var v) ? v : DefaultFlag(resolved, flag);
    }

    private static string ResolveRole(string? role)
    {
        var r = (role ?? string.Empty).Trim().ToLowerInvariant();
        if (KnownRoles.Contains(r)) return r;
        return LegacyAliases.TryGetValue(r, out var alias) ? alias : "guest";
    }

    private static bool DefaultFlag(string role, string flag) =>
        StaffRoles.Contains(role) && flag is "canCreate" or "canEdit" or "canUploadDokumen" or "canApprovalDokumen";

    private async Task<Dictionary<string, Dictionary<string, bool>>> GetSavedAsync()
    {
        if (DateTime.UtcNow - _loadedAt < CacheDuration) return _saved;

        await _loadLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _loadedAt < CacheDuration) return _saved;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var raw = await db.KonfigurasiSistems
                .Where(k => k.NamaSetting == SettingName)
                .Select(k => k.NilaiSetting)
                .FirstOrDefaultAsync();

            _saved = Parse(raw);
            _loadedAt = DateTime.UtcNow;
        }
        catch
        {
            // Gagal baca konfigurasi: pakai data terakhir (atau default) dan coba lagi di request berikutnya
        }
        finally
        {
            _loadLock.Release();
        }
        return _saved;
    }

    // Bentuk tersimpan: { matrix: { role: { canCreate: bool, ... } }, labels, knownMenus }
    // atau bentuk lama: { role: { ... } } langsung.
    private static Dictionary<string, Dictionary<string, bool>> Parse(string? raw)
    {
        var result = new Dictionary<string, Dictionary<string, bool>>();
        if (string.IsNullOrWhiteSpace(raw)) return result;

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return result;
            if (root.TryGetProperty("matrix", out var matrix) && matrix.ValueKind == JsonValueKind.Object)
                root = matrix;

            foreach (var role in root.EnumerateObject())
            {
                if (role.Value.ValueKind != JsonValueKind.Object) continue;
                var flags = new Dictionary<string, bool>();
                foreach (var flag in FlagNames)
                {
                    if (role.Value.TryGetProperty(flag, out var f) &&
                        (f.ValueKind == JsonValueKind.True || f.ValueKind == JsonValueKind.False))
                        flags[flag] = f.GetBoolean();
                }
                result[role.Name] = flags;
            }
        }
        catch (JsonException)
        {
            // Nilai tersimpan rusak: abaikan, pakai default
        }
        return result;
    }
}
