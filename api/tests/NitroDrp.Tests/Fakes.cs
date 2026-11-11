using NitroDrp.Api.Domain;
using NitroDrp.Api.Services;
using NitroDrp.Api.Storage;

namespace NitroDrp.Tests;

public sealed class ManualClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}

public sealed class InMemoryShareStore : IShareStore
{
    public Dictionary<string, Share> Shares { get; } = [];
    private readonly object _gate = new();

    public Task CreateAsync(Share share, CancellationToken ct) { lock (_gate) Shares.Add(share.Token, share); return Task.CompletedTask; }
    public Task<Share?> GetAsync(string token, CancellationToken ct) { lock (_gate) return Task.FromResult(Shares.GetValueOrDefault(token)); }
    public Task UpdateFileAsync(string token, ShareFile file, CancellationToken ct) => Task.CompletedTask; // objects are shared by reference
    public Task ActivateAsync(string token, CancellationToken ct) { lock (_gate) Shares[token].Status = ShareStatus.Active; return Task.CompletedTask; }

    public Task<bool> TryRegisterDownloadAsync(string token, DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate)
        {
            var s = Shares[token];
            var ok = s.Status == ShareStatus.Active && s.ExpiresAt > now && (s.DownloadLimit == 0 || s.DownloadCount < s.DownloadLimit);
            if (ok) s.DownloadCount++;
            return Task.FromResult(ok);
        }
    }

    public Task DeleteAsync(string token, CancellationToken ct) { lock (_gate) Shares.Remove(token); return Task.CompletedTask; }
}

public sealed class FakeStorage : IFileStorage
{
    public Dictionary<string, long> Objects { get; } = [];
    public HashSet<string> OpenUploads { get; } = [];
    public Dictionary<string, long> DeclaredSizes { get; } = [];

    public Task<string> BeginMultipartAsync(string key, string contentType, CancellationToken ct)
    {
        OpenUploads.Add(key);
        return Task.FromResult("upload-" + key);
    }

    public IReadOnlyList<PresignedPart> PresignParts(string key, string uploadId, IEnumerable<int> partNumbers) =>
        partNumbers.Select(n => new PresignedPart(n, $"https://fake/{key}?part={n}")).ToList();

    public Task CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken ct)
    {
        OpenUploads.Remove(key);
        Objects[key] = DeclaredSizes.GetValueOrDefault(key, -1); // tests set the "real" size via DeclaredSizes
        return Task.CompletedTask;
    }

    /// <summary>Parts a test pretends S3 already has, keyed by storage key.</summary>
    public Dictionary<string, List<StoredPart>> Parts { get; } = [];

    public Task<IReadOnlyList<StoredPart>?> ListPartsAsync(string key, string uploadId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StoredPart>?>(OpenUploads.Contains(key) ? Parts.GetValueOrDefault(key, []) : null);

    public Task AbortMultipartAsync(string key, string uploadId, CancellationToken ct) { OpenUploads.Remove(key); return Task.CompletedTask; }
    public Task<long?> GetObjectSizeAsync(string key, CancellationToken ct) => Task.FromResult(Objects.TryGetValue(key, out var s) ? (long?)s : null);
    /// <summary>Content a test wants an object to have; falls back to zero bytes of the stored size.</summary>
    public Dictionary<string, byte[]> Content { get; } = [];

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
        Task.FromResult<Stream>(new MemoryStream(Content.TryGetValue(key, out var c) ? c : new byte[Math.Max(0, Objects.GetValueOrDefault(key))]));

    public string PresignDownload(string key, string fileName, TimeSpan ttl) => $"https://fake/{key}?dl={Uri.EscapeDataString(fileName)}";
    public Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct) { foreach (var k in keys) Objects.Remove(k); return Task.CompletedTask; }
}

public sealed class RecordingEdgeCache : IEdgeCache
{
    public List<string> Paths { get; } = [];
    public Exception? Throw { get; set; }

    public Task InvalidateAsync(IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        if (Throw is not null) throw Throw;
        Paths.AddRange(paths);
        return Task.CompletedTask;
    }
}
