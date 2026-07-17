using System.Security.Cryptography;
using System.Text;

namespace SchoolPOS.Portal.Web.Infrastructure;

/// <summary>
/// Protege el parámetro <c>state</c> del flujo OAuth (CSRF): firma el <c>schoolId</c> + vencimiento
/// con HMAC-SHA256, sin necesidad de almacenamiento. Se valida en el callback antes de guardar tokens.
/// </summary>
public sealed class OnboardingStateProtector
{
    private readonly byte[] _key;

    public OnboardingStateProtector(string secret) => _key = Encoding.UTF8.GetBytes(secret);

    public string Protect(Guid schoolId, TimeSpan ttl)
    {
        var exp = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        var payload = $"{schoolId:N}.{exp}";
        var sig = Sign(payload);
        return $"{Base64Url(Encoding.UTF8.GetBytes(payload))}.{Base64Url(sig)}";
    }

    public bool TryUnprotect(string state, out Guid schoolId)
    {
        schoolId = default;
        var parts = state.Split('.');
        if (parts.Length != 2)
            return false;

        byte[] payloadBytes, sig;
        try
        {
            payloadBytes = FromBase64Url(parts[0]);
            sig = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var payload = Encoding.UTF8.GetString(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(sig, Sign(payload)))
            return false;

        var seg = payload.Split('.');
        if (seg.Length != 2 || !long.TryParse(seg[1], out var exp))
            return false;
        if (DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow)
            return false;

        return Guid.TryParseExact(seg[0], "N", out schoolId);
    }

    private byte[] Sign(string payload)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        b64 = (b64.Length % 4) switch { 2 => b64 + "==", 3 => b64 + "=", _ => b64 };
        return Convert.FromBase64String(b64);
    }
}
