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

    Task DeleteAsync(string token, CancellationToken ct);
}
