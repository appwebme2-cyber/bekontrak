using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace RefineryContractAPI.Services;

/// <summary>
/// Verifikasi PIN fitur AI (ekstraksi dokumen dan chatbot) terhadap env variable "PIN".
/// Tanpa PIN terkonfigurasi fitur ditolak (fail closed). Perbandingan constant-time, dan
/// 5 kali salah berturut-turut per user mengunci 10 menit supaya PIN tidak bisa ditebak lewat API.
/// </summary>
public class PinGuard
{
    private const int MaxPinFails = 5;
    private static readonly TimeSpan PinLockDuration = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, (int Fails, DateTime LockedUntil)> _attempts = new();
    private readonly IConfiguration _config;

    public PinGuard(IConfiguration config) => _config = config;

    /// <summary>Null kalau PIN benar; selain itu status HTTP dan pesan penolakan.</summary>
    public (int Status, string Message)? Check(string? userKey, string? pin)
    {
        var expected = _config["PIN"];
        if (string.IsNullOrEmpty(expected))
            return (503, "PIN belum dikonfigurasi di server. Hubungi admin untuk menambahkan environment variable PIN.");

        var key = string.IsNullOrEmpty(userKey) ? "anonymous" : userKey;
        if (_attempts.TryGetValue(key, out var attempt) && attempt.LockedUntil > DateTime.UtcNow)
        {
            var minutes = Math.Ceiling((attempt.LockedUntil - DateTime.UtcNow).TotalMinutes);
            return (429, $"Terlalu banyak PIN salah. Coba lagi dalam {minutes} menit.");
        }

        if (!Matches(pin, expected))
        {
            if (_attempts.Count > 10000) _attempts.Clear(); // batasi memori
            _attempts.AddOrUpdate(
                key,
                _ => (1, DateTime.MinValue),
                (_, old) =>
                {
                    var fails = (old.LockedUntil > DateTime.MinValue && old.LockedUntil <= DateTime.UtcNow) ? 1 : old.Fails + 1;
                    return fails >= MaxPinFails ? (0, DateTime.UtcNow.Add(PinLockDuration)) : (fails, DateTime.MinValue);
                });
            return (403, "PIN salah.");
        }

        _attempts.TryRemove(key, out _);
        return null;
    }

    private static bool Matches(string? provided, string expected)
    {
        var a = Encoding.UTF8.GetBytes(provided ?? string.Empty);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
