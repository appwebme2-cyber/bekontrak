using System.Collections.Concurrent;

namespace RefineryContractAPI.Services;

public class FileTokenService
{
    private readonly ConcurrentDictionary<string, (string key, DateTime expires)> _tokens = new();

    public string CreateToken(string key)
    {
        PurgeExpired();
        var token = Guid.NewGuid().ToString("N");
        _tokens[token] = (key, DateTime.UtcNow.AddMinutes(5));
        return token;
    }

    public bool IsValid(string token, string key)
    {
        if (!_tokens.TryGetValue(token, out var entry)) return false;
        return entry.key == key && entry.expires > DateTime.UtcNow;
    }

    private void PurgeExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kv in _tokens)
            if (kv.Value.expires < now)
                _tokens.TryRemove(kv.Key, out _);
    }
}
