using System.Security.Claims;

namespace RefineryContractAPI.Services;

/// <summary>Data yang dimiliki satu RU (kolom id_ru). RU diisi otomatis oleh AppDbContext saat disimpan.</summary>
public interface IRuOwned
{
    string? IdRu { get; set; }
}

/// <summary>
/// RU yang berlaku untuk request saat ini (multi-RU). Dipakai filter otomatis di AppDbContext.
/// - User RU: hanya data RU-nya sendiri.
/// - Super Admin (admin tanpa RU) dan Head Office (kantor pusat): semua RU, atau satu RU yang dipilih
///   lewat header "X-Ru" (pemilih RU di tampilan).
/// - Tanpa login (startup, login, API publik ber-API key): tanpa filter.
/// </summary>
public interface ICurrentRu
{
    /// <summary>RU untuk menyaring data; null = semua RU (tanpa filter).</summary>
    string? Filter { get; }

    /// <summary>RU untuk data baru yang disimpan.</summary>
    string WriteRu { get; }

    /// <summary>True untuk Super Admin dan Head Office (boleh melihat lintas RU).</summary>
    bool IsCrossRu { get; }
}

public class CurrentRu : ICurrentRu
{
    public const string DefaultRu = "RU4"; // RU IV Cilacap: pemilik semua data sebelum multi-RU

    public static readonly HashSet<string> ValidRus = new(StringComparer.OrdinalIgnoreCase)
    {
        "RU2", "RU3", "RU4", "RU5", "RU6", "RU7",
    };

    private readonly IHttpContextAccessor _http;
    private (string? Filter, string WriteRu, bool CrossRu)? _resolved;

    public CurrentRu(IHttpContextAccessor http) => _http = http;

    public string? Filter => Resolve().Filter;
    public string WriteRu => Resolve().WriteRu;
    public bool IsCrossRu => Resolve().CrossRu;

    private (string? Filter, string WriteRu, bool CrossRu) Resolve()
    {
        if (_resolved.HasValue) return _resolved.Value;

        var ctx = _http.HttpContext;
        var user = ctx?.User;
        if (user?.Identity?.IsAuthenticated != true)
        {
            // Sengaja TIDAK disimpan: DbContext request yang sama juga dipakai saat token sedang divalidasi
            // (sebelum User terisi). Kalau hasil "tanpa filter" ini diingat, query berikutnya di request
            // yang sama ikut tanpa saringan RU.
            return (null, DefaultRu, false);
        }

        var role = (user.FindFirstValue(ClaimTypes.Role) ?? string.Empty).Trim().ToLowerInvariant();

        // Token yang terbit sebelum multi-RU belum punya klaim id_ru. Saat itu semua user non-admin
        // adalah RU IV dan admin adalah Super Admin, jadi fallback ini tepat untuk token lama.
        var claim = user.FindFirst("id_ru");
        var userRu = claim != null ? claim.Value : (role == "admin" ? string.Empty : DefaultRu);

        var crossRu = (role == "admin" && string.IsNullOrEmpty(userRu)) || role == "head_office";
        if (crossRu)
        {
            var selected = ctx!.Request.Headers["X-Ru"].ToString().Trim().ToUpperInvariant();
            var chosen = ValidRus.Contains(selected) ? selected : null;
            _resolved = (chosen, chosen ?? DefaultRu, true);
        }
        else
        {
            var ru = string.IsNullOrEmpty(userRu) ? DefaultRu : userRu;
            _resolved = (ru, ru, false);
        }
        return _resolved.Value;
    }
}
