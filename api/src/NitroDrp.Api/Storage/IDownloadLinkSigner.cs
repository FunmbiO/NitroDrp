namespace NitroDrp.Api.Storage;

/// <summary>Turns a stored object into a short-lived, browser-usable download URL.</summary>
public interface IDownloadLinkSigner
{
    string Sign(string storageKey, string fileName, TimeSpan ttl);
}

/// <summary>Direct S3 presigned GET. Used locally (S3 emulator) and as the fallback when CloudFront isn't configured.</summary>
public sealed class S3DownloadLinkSigner(IFileStorage storage) : IDownloadLinkSigner
{
    public string Sign(string storageKey, string fileName, TimeSpan ttl) => storage.PresignDownload(storageKey, fileName, ttl);
}
