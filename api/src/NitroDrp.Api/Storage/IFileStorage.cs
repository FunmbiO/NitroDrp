using NitroDrp.Api.Services;

namespace NitroDrp.Api.Storage;

/// <summary>Where file bytes live. Browsers upload/download directly via presigned URLs.</summary>
public interface IFileStorage
{
    Task<string> BeginMultipartAsync(string key, string contentType, CancellationToken ct);
    IReadOnlyList<PresignedPart> PresignParts(string key, string uploadId, IEnumerable<int> partNumbers);
    Task CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken ct);
    Task AbortMultipartAsync(string key, string uploadId, CancellationToken ct);

    /// <summary>Size of the stored object, or null if it doesn't exist.</summary>
    Task<long?> GetObjectSizeAsync(string key, CancellationToken ct);

    string PresignDownload(string key, string fileName, TimeSpan ttl);
    Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct);
}
