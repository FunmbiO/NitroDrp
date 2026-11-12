using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace NitroDrp.Api.Auth;

public sealed record SessionInfo(string CredentialId, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

/// <summary>
/// The value of the sign-in cookie: which passkey signed in, when, and when it lapses, signed with HMAC-SHA256.
/// The signing key is derived from the server's API key, so changing the API key signs everybody out, and no extra
/// secret needs to be set up. The token carries no secrets and can't be forged without the key.
/// </summary>
public sealed class SessionTokens(string apiKey, TimeProvider clock, TimeSpan lifetime)
{
    private const string Version = "v1";
    private readonly byte[] _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(apiKey), 32, salt: null, info: "nitrodrp-session-v1"u8.ToArray());

    public TimeSpan Lifetime => lifetime;

    public string Issue(string credentialId)
    {
        var now = clock.GetUtcNow();
        var payload = $"{credentialId}|{now.ToUnixTimeSeconds()}|{(now + lifetime).ToUnixTimeSeconds()}";
        var body = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        return $"{Version}.{body}.{WebEncoders.Base64UrlEncode(Mac(body))}";
    }

    /// <summary>Null for anything that isn't a current, untampered token we issued.</summary>
    public SessionInfo? Validate(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 1024) return null;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != Version) return null;
        try
        {
            if (!CryptographicOperations.FixedTimeEquals(Mac(parts[1]), WebEncoders.Base64UrlDecode(parts[2]))) return null;
            var fields = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[1])).Split('|');
            if (fields.Length != 3) return null;
            var issued = DateTimeOffset.FromUnixTimeSeconds(long.Parse(fields[1]));
            var expires = DateTimeOffset.FromUnixTimeSeconds(long.Parse(fields[2]));
            return clock.GetUtcNow() < expires && issued <= clock.GetUtcNow().AddMinutes(5) ? new SessionInfo(fields[0], issued, expires) : null;
        }
        catch (Exception e) when (e is FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    private byte[] Mac(string body) => HMACSHA256.HashData(_key, Encoding.ASCII.GetBytes(body));
}
