namespace NitroDrp.Api.Storage;

/// <summary>Opens a stored object as a forward-only stream. Used only for zip downloads.</summary>
public interface IObjectReader
{
    Task<Stream> OpenAsync(string storageKey, CancellationToken ct);
}

/// <summary>Reads straight from S3 (local S3 emulator, or AWS without CloudFront).</summary>
public sealed class S3ObjectReader(IFileStorage storage) : IObjectReader
{
    public Task<Stream> OpenAsync(string storageKey, CancellationToken ct) => storage.OpenReadAsync(storageKey, ct);
}

/// <summary>
/// Reads through CloudFront with a short-lived signed URL, so zipping draws on CloudFront's free
/// transfer allowance instead of paying S3 internet egress to your home server.
/// </summary>
public sealed class CloudFrontObjectReader(HttpClient http, IDownloadLinkSigner signer) : IObjectReader
{
    public async Task<Stream> OpenAsync(string storageKey, CancellationToken ct)
    {
        var url = signer.Sign(storageKey, "file", TimeSpan.FromMinutes(5));
        var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        try
        {
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStreamAsync(ct);
        }
        catch
        {
            resp.Dispose();
            throw;
        }
    }
}
