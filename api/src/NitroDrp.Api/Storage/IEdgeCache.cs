namespace NitroDrp.Api.Storage;

/// <summary>
/// Drops cached copies at the CDN edge. After "Delete link now" the S3 objects are gone, but CloudFront
/// may still hold a cached copy for up to the cache TTL; this closes that window.
/// </summary>
public interface IEdgeCache
{
    /// <summary>Paths are CloudFront-style, e.g. "/d7/abc123/*".</summary>
    Task InvalidateAsync(IReadOnlyCollection<string> paths, CancellationToken ct);
}

/// <summary>Used when there is no CDN (local MinIO, or AWS without CloudFront).</summary>
public sealed class NoEdgeCache : IEdgeCache
{
    public Task InvalidateAsync(IReadOnlyCollection<string> paths, CancellationToken ct) => Task.CompletedTask;
}
