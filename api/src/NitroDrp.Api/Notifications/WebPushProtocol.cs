using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace NitroDrp.Api.Notifications;

/// <summary>
/// The two standards a push message needs, on .NET's own cryptography (no library):
/// RFC 8291 (encrypt the message so only the device can read it, "aes128gcm") and RFC 8292 (VAPID: prove to the
/// push service which server is sending). Apple's push service accepts only these current forms.
/// </summary>
public static class WebPushProtocol
{
    private const int RecordSize = 4096;
    public const int MaxPlaintext = RecordSize - 17 - 1; // tag (16) and the delimiter byte (1) share the record

    /// <summary>
    /// Encrypts <paramref name="plaintext"/> for one device. <paramref name="uaPublic"/> is the browser's 65-byte public key,
    /// <paramref name="authSecret"/> its 16-byte secret. The sender key and salt are made fresh each time; tests may pass fixed ones.
    /// </summary>
    public static byte[] Encrypt(byte[] plaintext, byte[] uaPublic, byte[] authSecret, ECDiffieHellman? senderKey = null, byte[]? salt = null)
    {
        if (plaintext.Length > MaxPlaintext) throw new ArgumentException("Message too long for one push record.", nameof(plaintext));
        if (uaPublic.Length != 65 || uaPublic[0] != 4) throw new ArgumentException("Bad device key.", nameof(uaPublic));
        if (authSecret.Length != 16) throw new ArgumentException("Bad device secret.", nameof(authSecret));

        using var ownKey = senderKey is null ? ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256) : null;
        var sender = senderKey ?? ownKey!;
        var q = sender.ExportParameters(false).Q;
        byte[] asPublic = [4, .. q.X!, .. q.Y!];
        salt ??= RandomNumberGenerator.GetBytes(16);

        using var ua = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = uaPublic[1..33], Y = uaPublic[33..65] } });
        var shared = sender.DeriveRawSecretAgreement(ua.PublicKey);

        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, shared, authSecret);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, [.. "WebPush: info\0"u8, .. uaPublic, .. asPublic]);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        byte[] record = [.. plaintext, 0x02]; // 0x02 marks the last (here the only) record
        var cipher = new byte[record.Length];
        var tag = new byte[16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Encrypt(nonce, record, cipher, tag);

        // header: salt (16), record size (4, big-endian), key id length (1), key id (the sender's public key)
        return [.. salt, 0, 0, (byte)(RecordSize >> 8), (byte)(RecordSize & 0xff), (byte)asPublic.Length, .. asPublic, .. cipher, .. tag];
    }

    /// <summary>The value of the Authorization header: "vapid t=&lt;signed token&gt;, k=&lt;our public key&gt;".</summary>
    public static string VapidAuthorization(string audience, string subject, string publicKey, string privateKey, DateTimeOffset now)
    {
        var pub = WebEncoders.Base64UrlDecode(publicKey);
        var d = WebEncoders.Base64UrlDecode(privateKey);
        if (pub.Length != 65 || pub[0] != 4 || d.Length != 32) throw new ArgumentException("Bad VAPID key pair.");

        var header = WebEncoders.Base64UrlEncode("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { aud = audience, exp = now.AddHours(12).ToUnixTimeSeconds(), sub = subject }));
        var signingInput = $"{header}.{claims}";

        using var ec = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = d,
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] },
        });
        var signature = ec.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256); // raw r||s, as JWT wants
        return $"vapid t={signingInput}.{WebEncoders.Base64UrlEncode(signature)}, k={publicKey}";
    }

    /// <summary>Does this look like a usable key pair (right sizes, and the private half really belongs to the public half)?</summary>
    public static bool IsValidKeyPair(string? publicKey, string? privateKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey)) return false;
            var pub = WebEncoders.Base64UrlDecode(publicKey);
            var d = WebEncoders.Base64UrlDecode(privateKey);
            if (pub.Length != 65 || pub[0] != 4 || d.Length != 32) return false;
            using var ec = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, D = d, Q = new ECPoint { X = pub[1..33], Y = pub[33..65] } });
            ec.ExportParameters(true); // throws if Q is not the point for D (or not on the curve)
            var probe = "probe"u8.ToArray();
            using var verifyOnly = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = pub[1..33], Y = pub[33..65] } });
            return verifyOnly.VerifyData(probe, ec.SignData(probe, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256);
        }
        catch (Exception e) when (e is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }
}
