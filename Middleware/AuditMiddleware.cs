using System.Security.Claims;
using RefineryContractAPI.Data;
using RefineryContractAPI.Models;

namespace RefineryContractAPI.Middleware;

public class AuditMiddleware
{
    private readonly RequestDelegate _next;

    // Endpoint yang mau dicatat → nama menu
    private static readonly Dictionary<string, string> MenuMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { "/api/contracts",          "Kontrak" },
        { "/api/tagihan",            "Tagihan" },
        { "/api/vendors",            "Vendor" },
        { "/api/padi",               "User Purchase (PADI)" },
        { "/api/dokumenapproval",    "Approval Dokumen" },
        { "/api/amandemen",          "Amandemen" },
        { "/api/progresslumpsum",    "Progress Lumpsum" },
        { "/api/progressunitprice",  "Progress Unit Price" },
        { "/api/monitoringltsa",     "Monitoring LTSA" },
        { "/api/dailyreport",        "Laporan Harian" },
        { "/api/konfigurasi",        "Konfigurasi Sistem" },
        { "/api/sla-setting",        "SLA Setting" },
        { "/api/logakses",           "Log Akses" },
        { "/api/auth/login",         "Auth" },
        { "/api/auth/logout",        "Auth" },
    };

    private static string? ResolveMenu(string path)
    {
        var lower = path.ToLowerInvariant();
        foreach (var kv in MenuMap)
            if (lower.StartsWith(kv.Key))
                return kv.Value;
        return null;
    }

    private static string ResolveActivity(string method, string path)
    {
        var lower = path.ToLowerInvariant();
        if (lower.Contains("/login"))  return "Login";
        if (lower.Contains("/logout")) return "Logout";
        return method.ToUpperInvariant() switch
        {
            "POST"   => "Tambah",
            "PUT"    => "Ubah",
            "PATCH"  => "Ubah",
            "DELETE" => "Hapus",
            "GET"    => "Lihat",
            _        => method
        };
    }

    public AuditMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        await _next(context);

        var method = context.Request.Method;
        var path   = context.Request.Path.Value ?? "";

        // Hanya log mutation + GET ke endpoint utama resource
        var isGet      = method.Equals("GET",    StringComparison.OrdinalIgnoreCase);
        var isMutation = method.Equals("POST",   StringComparison.OrdinalIgnoreCase)
                      || method.Equals("PUT",    StringComparison.OrdinalIgnoreCase)
                      || method.Equals("PATCH",  StringComparison.OrdinalIgnoreCase)
                      || method.Equals("DELETE", StringComparison.OrdinalIgnoreCase);

        if (!isMutation && !isGet) return;

        // Lewati kalau GET ke sub-resource (path punya segmen tambahan setelah resource)
        if (isGet)
        {
            var segments = path.Trim('/').Split('/');
            // /api/resource → 2 segmen, /api/resource/id → 3+ segmen (skip)
            if (segments.Length > 2) return;
        }

        var menu = ResolveMenu(path);
        if (menu == null) return;

        // Hanya catat respons sukses
        var status = context.Response.StatusCode;
        if (status < 200 || status >= 300) return;

        var user     = context.User;
        var userId   = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var namaUser = user.FindFirst("full_name")?.Value
                    ?? user.FindFirst(ClaimTypes.Email)?.Value
                    ?? "Unknown";
        var role     = user.FindFirst(ClaimTypes.Role)?.Value ?? "";

        if (string.IsNullOrEmpty(userId)) return; // skip request tanpa auth

        var activity = ResolveActivity(method, path);
        var ip       = context.Connection.RemoteIpAddress?.ToString();

        try
        {
            using var scope = context.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LogAkses.Add(new LogAkses
            {
                Id       = Guid.NewGuid().ToString(),
                UserId   = userId,
                NamaUser = namaUser,
                Role     = role,
                Menu     = menu,
                Activity = activity,
                Detail   = $"{method} {path}",
                IpAddress = ip,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        catch
        {
            // Jangan crash request utama karena gagal logging
        }
    }
}
