using System.Security.Cryptography;
using System.Text;

namespace NitroDrp.Api.Services;

public static class Ids
{
    private const string Base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    /// <summary>12 chars of base62 from a CSPRNG (~71 bits). GetInt32 avoids modulo bias.</summary>
    public static string NewToken(int length = 12)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = Base62[RandomNumberGenerator.GetInt32(Base62.Length)];
        return new string(chars);
    }

    /// <summary>Is this shaped like a link token? Anything else can't exist, so it is refused before any lookup.</summary>
    public static bool IsToken(string? s) => s is { Length: >= 8 and <= 32 } && s.All(char.IsAsciiLetterOrDigit);

    public static bool IsFileId(string? s) => s is { Length: >= 8 and <= 32 } && s.All(char.IsAsciiHexDigitLower);

    /// <summary>A passkey's credential id as it appears in an address: base64url.</summary>
    public static bool IsCredentialId(string? s) => s is { Length: >= 1 and <= 1024 } && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static string NewFileId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();

    /// <summary>256-bit secret, base64url.</summary>
    public static string NewOwnerKey() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public static class Secrets
{
    /// <summary>Hash for high-entropy secrets (owner key, API key). Not for passwords.</summary>
    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>Constant-time comparison of two secrets of any length.</summary>
    public static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}

/// <summary>PBKDF2-SHA256 for user-chosen share passwords. Format: v1.iterations.salt.hash</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static string Hash(string password, int iterations = Iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        return $"v1.{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iterations)) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class FileNames
{
    /// <summary>Strips path segments and characters that are unsafe in headers or file systems.</summary>
    public static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var n = raw.Replace('\\', '/');
        n = n[(n.LastIndexOf('/') + 1)..];
        var sb = new StringBuilder(n.Length);
        foreach (var c in n)
        {
            if (TextSafety.IsSpoofing(c)) continue;
            sb.Append(char.IsControl(c) || "<>:\"|?*".Contains(c) ? '_' : c);
        }
        var s = sb.ToString().Trim().Trim('.');
        if (s.Length > 200)
        {
            var ext = Path.GetExtension(s);
            s = ext.Length is > 0 and <= 20 ? s[..(200 - ext.Length)] + ext : s[..200];
        }
        return s;
    }
}
