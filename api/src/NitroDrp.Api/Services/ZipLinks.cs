using System.Security.Cryptography;
using System.Text;

namespace NitroDrp.Api.Services;

/// <summary>
/// Short-lived HMAC-signed zip URLs. A zip is downloaded by plain browser navigation (so it streams to
/// disk instead of through JavaScript), which can't carry a password header. The POST that counts the
/// download checks the password and hands back one of these; the GET only verifies the signature.
/// </summary>
public sealed class ZipLinks(byte[] key, TimeProvider clock)
{
    public static ZipLinks FromApiKey(string apiKey, TimeProvider clock) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes("nitrodrp-zip:" + apiKey)), clock);

    public string Create(string token, TimeSpan ttl)
    {
        var exp = clock.GetUtcNow().Add(ttl).ToUnixTimeSeconds();
        return $"/api/shares/{token}/zip?exp={exp}&sig={Sign(token, exp)}";
    }

    public bool IsValid(string token, long exp, string? sig) =>
        !string.IsNullOrEmpty(sig)
        && exp > clock.GetUtcNow().ToUnixTimeSeconds()
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(token, exp)), Encoding.UTF8.GetBytes(sig));

    private string Sign(string token, long exp) =>
        Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"zip.{token}.{exp}")))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
