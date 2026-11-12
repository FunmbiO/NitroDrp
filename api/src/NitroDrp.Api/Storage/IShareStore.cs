using NitroDrp.Api.Domain;

namespace NitroDrp.Api.Storage;

/// <summary>Link metadata. Implemented on DynamoDB; in-memory fake in tests.</summary>
public interface IShareStore
{
    Task CreateAsync(Share share, CancellationToken ct);
    Task<Share?> GetAsync(string token, CancellationToken ct);
    Task UpdateFileAsync(string token, ShareFile file, CancellationToken ct);
    Task ActivateAsync(string token, CancellationToken ct);

    /// <summary>
    /// Atomically counts one download if the share is active, unexpired and under its limit.
    /// Returns false when any of those no longer holds (so concurrent requests can't exceed the limit).
    /// </summary>
    Task<bool> TryRegisterDownloadAsync(string token, DateTimeOffset now, CancellationToken ct);

    /// <summary>
    /// Atomically reserves `count` emails against the share's lifetime cap. Returns false, changing nothing, if that
    /// would go over `cap` (so concurrent requests can't exceed it, like the download limit).
    /// </summary>
    Task<bool> TryReserveEmailsAsync(string token, int count, int cap, CancellationToken ct);

    Task DeleteAsync(string token, CancellationToken ct);

    /// <summary>Adds (or refreshes) a link in the owner's list. Idempotent.</summary>
    Task PutSummaryAsync(ShareSummary summary, CancellationToken ct);

    Task RemoveSummaryAsync(ShareSummary summary, CancellationToken ct);

    /// <summary>The owner's links, newest first. May include links that have just expired; the caller filters.</summary>
    Task<IReadOnlyList<ShareSummary>> ListSummariesAsync(int limit, CancellationToken ct);
}
