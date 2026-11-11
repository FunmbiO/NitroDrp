using NitroDrp.Api.Services;

namespace NitroDrp.Api.Storage;

public sealed record StoredPart(int PartNumber, string ETag, long Size);

/// <summary>Where file bytes live. Browsers upload/download directly via presigned URLs.</summary>
public interface IFileStorage
{
    Task<string> BeginMultipartAsync(string key, string contentType, CancellationToken ct);
    IReadOnlyList<PresignedPart> PresignParts(string key, string uploadId, IEnumerable<int> partNumbers);
    Task CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken ct);
    /// <summary>Parts S3 already holds for an in-progress upload, or null if the upload no longer exists (aborted or expired).</summary>
    Task<IReadOnlyList<StoredPart>?> ListPartsAsync(string key, string uploadId, CancellationToken ct);

    Task AbortMultipartAsync(string key, string uploadId, CancellationToken ct);

    /// <summary>Size of the stored object, or null if it doesn't exist.</summary>
    Task<long?> GetObjectSizeAsync(string key, CancellationToken ct);

    string PresignDownload(string key, string fileName, TimeSpan ttl);
    Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct);
}
