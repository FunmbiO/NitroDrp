using System.Security.Cryptography;
using System.Text;
using System.Web;
using NitroDrp.Api.Storage;

namespace NitroDrp.Tests;

public class CloudFrontSignerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private readonly RSA _key = RSA.Create(2048);

    private string SignUrl(string key = "d7/abc/f1", string name = "Smith Wedding.zip", int minutes = 15) =>
        new CloudFrontDownloadLinkSigner("cdn.example.com", "K123", _key, new ManualClock(Now)).Sign(key, name, TimeSpan.FromMinutes(minutes));

    [Fact]
    public void Url_points_at_the_distribution_and_carries_signing_params()
    {
        var uri = new Uri(SignUrl());
        var q = HttpUtility.ParseQueryString(uri.Query);

        Assert.Equal("https", uri.Scheme);
        Assert.Equal("cdn.example.com", uri.Host);
        Assert.Equal("/d7/abc/f1", uri.AbsolutePath);
        Assert.Equal("K123", q["Key-Pair-Id"]);
        Assert.Equal(Now.AddMinutes(15).ToUnixTimeSeconds().ToString(), q["Expires"]);
    }

    [Fact]
    public void Signature_verifies_against_the_public_key_over_the_canned_policy()
    {
        var url = SignUrl();
        var q = HttpUtility.ParseQueryString(new Uri(url).Query);
        var resource = url[..url.IndexOf("&Expires=", StringComparison.Ordinal)];
        var policy = $"{{\"Statement\":[{{\"Resource\":\"{resource}\",\"Condition\":{{\"DateLessThan\":{{\"AWS:EpochTime\":{q["Expires"]}}}}}}}]}}";

        var sig = CloudFrontDownloadLinkSigner.FromCloudFrontBase64(q["Signature"]!);
        Assert.True(_key.VerifyData(Encoding.UTF8.GetBytes(policy), sig, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Tampering_with_the_filename_breaks_the_signature()
    {
        var url = SignUrl(name: "a.zip");
        var q = HttpUtility.ParseQueryString(new Uri(url).Query);
        var tampered = url[..url.IndexOf("&Expires=", StringComparison.Ordinal)].Replace("a.zip", "evil.exe");
        var policy = $"{{\"Statement\":[{{\"Resource\":\"{tampered}\",\"Condition\":{{\"DateLessThan\":{{\"AWS:EpochTime\":{q["Expires"]}}}}}}}]}}";

        var sig = CloudFrontDownloadLinkSigner.FromCloudFrontBase64(q["Signature"]!);
        Assert.False(_key.VerifyData(Encoding.UTF8.GetBytes(policy), sig, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void Filename_travels_as_an_attachment_disposition_even_with_spaces_and_unicode()
    {
        var q = HttpUtility.ParseQueryString(new Uri(SignUrl(name: "Réception 1.mp4")).Query);
        Assert.Equal("attachment; filename*=UTF-8''R%C3%A9ception%201.mp4", q["response-content-disposition"]);
    }

    [Fact]
    public void Signature_uses_cloudfront_safe_base64()
    {
        var sig = HttpUtility.ParseQueryString(new Uri(SignUrl()).Query)["Signature"]!;
        Assert.DoesNotContain('+', sig);
        Assert.DoesNotContain('/', sig);
        Assert.DoesNotContain('=', sig);
    }

    [Fact]
    public void FromPem_loads_pkcs8_and_pkcs1_keys()
    {
        foreach (var pem in new[] { _key.ExportPkcs8PrivateKeyPem(), _key.ExportRSAPrivateKeyPem() })
        {
            var signer = CloudFrontDownloadLinkSigner.FromPem("cdn.example.com", "K1", pem, new ManualClock(Now));
            Assert.Contains("Signature=", signer.Sign("d1/t/f", "a.zip", TimeSpan.FromMinutes(1)));
        }
    }
}
