using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using NitroDrp.Api;
using NitroDrp.Api.Notifications;

namespace NitroDrp.Tests;

/// <summary>The real sender, against a fake push service: what leaves the server is encrypted, signed with our key, and addressed to the device.</summary>
public class WebPushTransportTests
{
    private sealed class Capture(HttpStatusCode reply) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body)> Calls { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request, request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct)));
            return new HttpResponseMessage(reply);
        }
    }

    private static (string Public, string Private) VapidPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(true);
        return (WebEncoders.Base64UrlEncode([4, .. p.Q.X!, .. p.Q.Y!]), WebEncoders.Base64UrlEncode(p.D!));
    }

    private static PushSubscriptionRecord Device(string endpoint = "https://fcm.googleapis.com/fcm/send/abc")
    {
        using var ec = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = ec.ExportParameters(false);
        return new("id", endpoint, WebEncoders.Base64UrlEncode([4, .. p.Q.X!, .. p.Q.Y!]), WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(16)),
            "Phone", NotificationKinds.All.ToArray(), DateTimeOffset.UnixEpoch, null);
    }

    private static (WebPushTransport T, Capture C, (string Public, string Private) Keys) Make(HttpStatusCode reply)
    {
        var keys = VapidPair();
        var cap = new Capture(reply);
        var t = new WebPushTransport(new PushOptions { PublicKey = keys.Public, PrivateKey = keys.Private, Subject = "mailto:owner@example.com" },
            new HttpClient(cap), NullLogger<WebPushTransport>.Instance);
        return (t, cap, keys);
    }

    private static ECDiffieHellman SenderKey(string privateB64, string publicB64)
    {
        var pub = WebEncoders.Base64UrlDecode(publicB64);
        return ECDiffieHellman.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            D = WebEncoders.Base64UrlDecode(privateB64),
            Q = new ECPoint { X = pub[1..33], Y = pub[33..65] },
        });
    }

    [Fact]
    public void Encryption_matches_the_worked_example_in_RFC_8291()
    {
        // RFC 8291 appendix A: fixed keys and salt, and the exact bytes a correct implementation produces.
        using var sender = SenderKey("yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw", "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8");
        var body = WebPushProtocol.Encrypt(Encoding.UTF8.GetBytes("When I grow up, I want to be a watermelon"),
            WebEncoders.Base64UrlDecode("BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4"),
            WebEncoders.Base64UrlDecode("BTBZMqHH6r4Tts7J_aSIgg"), sender, WebEncoders.Base64UrlDecode("DGv6ra1nlYgDCS1FRnbzlw"));
        Assert.Equal("DGv6ra1nlYgDCS1FRnbzlwAAEABBBP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A_yl95bQpu6cVPTpK4Mqgkf1CXztLVBSt2Ks3oZwbuwXPXLWyouBWLVWGNWQexSgSxsj_Qulcy4a-fN",
            WebEncoders.Base64UrlEncode(body));
    }

    /// <summary>What the browser does with a push: the reverse of Encrypt, from its own private key and secret.</summary>
    private static string Decrypt(byte[] body, ECDiffieHellman uaPrivate, byte[] uaPublic, byte[] authSecret)
    {
        var salt = body[..16];
        var idLen = body[20];
        var asPublic = body[21..(21 + idLen)];
        var cipher = body[(21 + idLen)..];
        using var sender = ECDiffieHellman.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = asPublic[1..33], Y = asPublic[33..65] } });
        var shared = uaPrivate.DeriveRawSecretAgreement(sender.PublicKey);
        var prkKey = HKDF.Extract(HashAlgorithmName.SHA256, shared, authSecret);
        var ikm = HKDF.Expand(HashAlgorithmName.SHA256, prkKey, 32, [.. "WebPush: info\0"u8, .. uaPublic, .. asPublic]);
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var cek = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());
        var plain = new byte[cipher.Length - 16];
        using (var gcm = new AesGcm(cek, 16)) gcm.Decrypt(nonce, cipher[..^16], cipher[^16..], plain);
        Assert.Equal(0x02, plain[^1]);
        return Encoding.UTF8.GetString(plain[..^1]);
    }

    [Fact]
    public async Task A_push_is_encrypted_for_the_device_and_signed_with_the_servers_key()
    {
        var (t, cap, keys) = Make(HttpStatusCode.Created);
        using var deviceKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var dp = deviceKey.ExportParameters(false);
        byte[] devicePublic = [4, .. dp.Q.X!, .. dp.Q.Y!];
        var authSecret = RandomNumberGenerator.GetBytes(16);
        var sub = new PushSubscriptionRecord("id", "https://web.push.apple.com/abc", WebEncoders.Base64UrlEncode(devicePublic), WebEncoders.Base64UrlEncode(authSecret),
            "iPhone", NotificationKinds.All.ToArray(), DateTimeOffset.UnixEpoch, null);
        const string plaintext = """{"title":"Link downloaded","body":"Someone downloaded one of your links."}""";
        Assert.Equal(PushResult.Sent, await t.SendAsync(sub, plaintext, default));

        var (req, body) = Assert.Single(cap.Calls);
        Assert.Equal("https://web.push.apple.com/abc", req.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Contains("aes128gcm", req.Content!.Headers.ContentEncoding); // Apple accepts nothing older
        Assert.Equal("86400", req.Headers.GetValues("TTL").Single());
        Assert.DoesNotContain("Someone downloaded", Encoding.UTF8.GetString(body)); // the push service can't read it
        Assert.Equal(plaintext, Decrypt(body, deviceKey, devicePublic, authSecret)); // the device can

        // Authorization: vapid t=<jwt>, k=<our public key>
        var auth = req.Headers.GetValues("Authorization").Single();
        Assert.StartsWith("vapid t=", auth);
        var parts = auth["vapid ".Length..].Split(", ");
        Assert.Equal(keys.Public, parts.Single(p => p.StartsWith("k=")).Substring(2));
        var jwt = parts.Single(p => p.StartsWith("t=")).Substring(2).Split('.');
        var claims = JsonDocument.Parse(WebEncoders.Base64UrlDecode(jwt[1])).RootElement;
        Assert.Equal("https://web.push.apple.com", claims.GetProperty("aud").GetString()); // the push service, nothing broader
        Assert.Equal("mailto:owner@example.com", claims.GetProperty("sub").GetString());
        var exp = claims.GetProperty("exp").GetInt64();
        Assert.InRange(exp, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds()); // services reject longer than a day

        // The signature really is by the private key that matches the public key we handed to browsers.
        var pub = WebEncoders.Base64UrlDecode(keys.Public);
        using var verify = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = pub[1..33], Y = pub[33..65] } });
        Assert.True(verify.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), WebEncoders.Base64UrlDecode(jwt[2]), HashAlgorithmName.SHA256));
    }

    [Fact]
    public void Encryption_refuses_bad_input_and_is_fresh_each_time()
    {
        using var k = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var p = k.ExportParameters(false);
        byte[] pub = [4, .. p.Q.X!, .. p.Q.Y!];
        var auth = new byte[16];
        Assert.NotEqual(WebPushProtocol.Encrypt([1, 2, 3], pub, auth), WebPushProtocol.Encrypt([1, 2, 3], pub, auth)); // new sender key and salt each time
        Assert.Throws<ArgumentException>(() => WebPushProtocol.Encrypt(new byte[WebPushProtocol.MaxPlaintext + 1], pub, auth));
        Assert.Throws<ArgumentException>(() => WebPushProtocol.Encrypt([1], pub[..64], auth));
        Assert.Throws<ArgumentException>(() => WebPushProtocol.Encrypt([1], pub, new byte[8]));
    }

    [Fact]
    public void Key_pairs_are_checked_for_shape_and_for_matching()
    {
        var (pub, priv) = VapidPair();
        var (pub2, _) = VapidPair();
        Assert.True(WebPushProtocol.IsValidKeyPair(pub, priv));
        Assert.False(WebPushProtocol.IsValidKeyPair(pub2, priv)); // each half is fine, but they don't belong together
        Assert.False(WebPushProtocol.IsValidKeyPair(pub, "short"));
        Assert.False(WebPushProtocol.IsValidKeyPair("***", priv));
        Assert.False(WebPushProtocol.IsValidKeyPair(null, priv));
        Assert.False(WebPushProtocol.IsValidKeyPair(pub, ""));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, PushResult.Gone)]
    [InlineData(HttpStatusCode.Gone, PushResult.Gone)]
    [InlineData(HttpStatusCode.InternalServerError, PushResult.Failed)]
    [InlineData(HttpStatusCode.TooManyRequests, PushResult.Failed)]
    [InlineData(HttpStatusCode.BadRequest, PushResult.Failed)]
    public async Task The_push_services_answer_decides_what_happens_to_the_device(HttpStatusCode reply, PushResult expected)
    {
        var (t, _, _) = Make(reply);
        Assert.Equal(expected, await t.SendAsync(Device(), "{}", default));
    }

    [Fact]
    public async Task A_network_failure_is_reported_not_thrown()
    {
        var keys = VapidPair();
        var t = new WebPushTransport(new PushOptions { PublicKey = keys.Public, PrivateKey = keys.Private, Subject = "mailto:o@example.com" },
            new HttpClient(new ThrowingHandler()), NullLogger<WebPushTransport>.Instance);
        Assert.Equal(PushResult.Failed, await t.SendAsync(Device(), "{}", default));
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => throw new HttpRequestException("down");
    }

    [Fact]
    public void Off_means_off()
    {
        var t = new NoPushTransport();
        Assert.False(t.Enabled);
        Assert.Null(t.PublicKey);
    }

    [Fact]
    public async Task The_keys_the_shell_tool_makes_are_accepted()
    {
        // deploy/make-vapid.sh output has the same shape as the pair above: 65-byte public starting 0x04, 32-byte private.
        var (pub, priv) = VapidPair();
        Assert.Equal(65, WebEncoders.Base64UrlDecode(pub).Length);
        Assert.Equal(32, WebEncoders.Base64UrlDecode(priv).Length);
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../deploy/make-vapid.sh"));
        if (!File.Exists(script)) return; // running from a copy without the repository
        var psi = new System.Diagnostics.ProcessStartInfo("bash", script) { RedirectStandardOutput = true };
        using var proc = System.Diagnostics.Process.Start(psi)!;
        var lines = (await proc.StandardOutput.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        await proc.WaitForExitAsync();
        var madePub = lines.Single(l => l.StartsWith("PUSH_PUBLIC_KEY=")).Substring(16);
        var madePriv = lines.Single(l => l.StartsWith("PUSH_PRIVATE_KEY=")).Substring(17);

        var cap = new Capture(HttpStatusCode.Created);
        var t = new WebPushTransport(new PushOptions { PublicKey = madePub, PrivateKey = madePriv, Subject = "mailto:o@example.com" }, new HttpClient(cap), NullLogger<WebPushTransport>.Instance);
        Assert.Equal(PushResult.Sent, await t.SendAsync(Device(), "{}", default));

        // The key made by the script signs, and its public half verifies: the pair matches.
        var jwt = cap.Calls[0].Request.Headers.GetValues("Authorization").Single()["vapid ".Length..].Split(", ").Single(p => p.StartsWith("t=")).Substring(2).Split('.');
        var pb = WebEncoders.Base64UrlDecode(madePub);
        using var verify = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = pb[1..33], Y = pb[33..65] } });
        Assert.True(verify.VerifyData(Encoding.ASCII.GetBytes($"{jwt[0]}.{jwt[1]}"), WebEncoders.Base64UrlDecode(jwt[2]), HashAlgorithmName.SHA256));
    }
}
