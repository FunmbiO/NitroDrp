using System.Security.Cryptography;
using System.Text;

namespace NitroDrp.Api.Storage;

/// <summary>
/// CloudFront signed URLs using a canned policy (resource + expiry). CloudFront only supports
/// RSA SHA-1 (PKCS#1 v1.5) for these signatures. The distribution must have the matching public
/// key in a trusted key group. The signed resource includes the response-content-disposition query
/// string, so a link can't be edited to download under a different name.
/// </summary>
public sealed class CloudFrontDownloadLinkSigner(string domain, string keyPairId, RSA privateKey, TimeProvider clock) : IDownloadLinkSigner
{
    public static CloudFrontDownloadLinkSigner FromPem(string domain, string keyPairId, string pem, TimeProvider clock)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new CloudFrontDownloadLinkSigner(domain, keyPairId, rsa, clock);
    }

    public string Sign(string storageKey, string fileName, TimeSpan ttl)
    {
        var disposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fileName)}";
        var url = $"https://{domain}/{storageKey}?response-content-disposition={Uri.EscapeDataString(disposition)}";
        var expires = clock.GetUtcNow().Add(ttl).ToUnixTimeSeconds();

        // Exact string matters: CloudFront verifies the signature over this JSON with no extra whitespace.
        var policy = $"{{\"Statement\":[{{\"Resource\":\"{url}\",\"Condition\":{{\"DateLessThan\":{{\"AWS:EpochTime\":{expires}}}}}}}]}}";
        var signature = privateKey.SignData(Encoding.UTF8.GetBytes(policy), HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);

        return $"{url}&Expires={expires}&Signature={CloudFrontBase64(signature)}&Key-Pair-Id={keyPairId}";
    }

    /// <summary>CloudFront's URL-safe base64 variant: + - = _ / ~</summary>
    public static string CloudFrontBase64(byte[] data) =>
        Convert.ToBase64String(data).Replace('+', '-').Replace('=', '_').Replace('/', '~');

    public static byte[] FromCloudFrontBase64(string s) =>
        Convert.FromBase64String(s.Replace('-', '+').Replace('_', '=').Replace('~', '/'));
}
