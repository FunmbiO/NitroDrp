using NitroDrp.Api.Domain;
using NitroDrp.Api.Email;
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

    public Dictionary<string, ShareSummary> Summaries { get; } = [];
    public Task PutSummaryAsync(ShareSummary summary, CancellationToken ct) { lock (_gate) Summaries[summary.Token] = summary; return Task.CompletedTask; }
    public Task RemoveSummaryAsync(ShareSummary summary, CancellationToken ct) { lock (_gate) Summaries.Remove(summary.Token); return Task.CompletedTask; }
    public Task<IReadOnlyList<ShareSummary>> ListSummariesAsync(int limit, CancellationToken ct)
    { lock (_gate) return Task.FromResult<IReadOnlyList<ShareSummary>>(Summaries.Values.OrderByDescending(s => s.CreatedAt).Take(limit).ToList()); }

    public Task<bool> TryReserveEmailsAsync(string token, int count, int cap, CancellationToken ct)
    {
        lock (_gate)
        {
            if (!Shares.TryGetValue(token, out var s) || s.EmailsSent + count > cap) return Task.FromResult(false);
            s.EmailsSent += count;
            return Task.FromResult(true);
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

public sealed class FakeEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    /// <summary>Addresses the "email service" refuses, like SES in sandbox mode with an unverified recipient.</summary>
    public HashSet<string> Refuses { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        if (Refuses.Contains(message.To)) throw new InvalidOperationException("refused");
        lock (Sent) Sent.Add(message);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryPasskeyStore : NitroDrp.Api.Auth.IPasskeyStore
{
    public Dictionary<string, NitroDrp.Api.Auth.Passkey> Keys { get; } = [];
    private readonly object _gate = new();

    public Task AddAsync(NitroDrp.Api.Auth.Passkey key, CancellationToken ct)
    {
        lock (_gate) { if (!Keys.TryAdd(key.Id, key)) throw new InvalidOperationException("duplicate credential"); }
        return Task.CompletedTask;
    }
    public Task<NitroDrp.Api.Auth.Passkey?> GetAsync(string id, CancellationToken ct) { lock (_gate) return Task.FromResult(Keys.GetValueOrDefault(id)); }
    public Task<IReadOnlyList<NitroDrp.Api.Auth.Passkey>> ListAsync(CancellationToken ct)
    { lock (_gate) return Task.FromResult<IReadOnlyList<NitroDrp.Api.Auth.Passkey>>(Keys.Values.OrderBy(k => k.CreatedAt).ToList()); }
    public Task<bool> RemoveAsync(string id, CancellationToken ct) { lock (_gate) return Task.FromResult(Keys.Remove(id)); }
    public Task TouchAsync(string id, uint signCount, DateTimeOffset now, CancellationToken ct)
    {
        lock (_gate) { if (Keys.TryGetValue(id, out var k)) Keys[id] = k with { SignCount = signCount, LastUsedAt = now }; }
        return Task.CompletedTask;
    }
}

public sealed class InMemoryNotificationStore : NitroDrp.Api.Notifications.INotificationStore
{
    private readonly object _gate = new();
    public List<NitroDrp.Api.Notifications.NotificationEvent> Events { get; } = [];
    public Dictionary<string, NitroDrp.Api.Notifications.PushSubscriptionRecord> Subs { get; } = [];
    public HashSet<string> ExpiryNotices { get; } = [];
    private NitroDrp.Api.Notifications.NotificationSettings _settings = NitroDrp.Api.Notifications.NotificationSettings.Default;

    public Task AddEventAsync(NitroDrp.Api.Notifications.NotificationEvent e, CancellationToken ct) { lock (_gate) Events.Add(e); return Task.CompletedTask; }
    public Task<IReadOnlyList<NitroDrp.Api.Notifications.NotificationEvent>> ListEventsAsync(int limit, CancellationToken ct)
    { lock (_gate) return Task.FromResult<IReadOnlyList<NitroDrp.Api.Notifications.NotificationEvent>>(Events.OrderByDescending(e => e.At).Take(limit).ToList()); }
    public Task<IReadOnlyList<NitroDrp.Api.Notifications.NotificationEvent>> ListEventsSinceAsync(DateTimeOffset since, CancellationToken ct)
    { lock (_gate) return Task.FromResult<IReadOnlyList<NitroDrp.Api.Notifications.NotificationEvent>>(Events.Where(e => e.At >= since).OrderBy(e => e.At).ToList()); }
    public Task ClearEventsAsync(CancellationToken ct) { lock (_gate) Events.Clear(); return Task.CompletedTask; }
    public Task<NitroDrp.Api.Notifications.NotificationSettings> GetSettingsAsync(CancellationToken ct) { lock (_gate) return Task.FromResult(_settings); }
    public Task SetLastReadAsync(DateTimeOffset at, CancellationToken ct) { lock (_gate) _settings = _settings with { LastReadAt = at }; return Task.CompletedTask; }
    public Task SetDigestEnabledAsync(bool enabled, CancellationToken ct) { lock (_gate) _settings = _settings with { DigestEnabled = enabled }; return Task.CompletedTask; }
    public Task<bool> TryClaimDigestAsync(string date, CancellationToken ct)
    { lock (_gate) { if (_settings.LastDigestDate == date) return Task.FromResult(false); _settings = _settings with { LastDigestDate = date }; return Task.FromResult(true); } }
    public Task<bool> TryClaimExpiryNoticeAsync(string token, DateTimeOffset forgetAfter, CancellationToken ct) { lock (_gate) return Task.FromResult(ExpiryNotices.Add(token)); }
    public Task AddSubscriptionAsync(NitroDrp.Api.Notifications.PushSubscriptionRecord s, CancellationToken ct) { lock (_gate) Subs[s.Id] = s; return Task.CompletedTask; }
    public Task<IReadOnlyList<NitroDrp.Api.Notifications.PushSubscriptionRecord>> ListSubscriptionsAsync(CancellationToken ct)
    { lock (_gate) return Task.FromResult<IReadOnlyList<NitroDrp.Api.Notifications.PushSubscriptionRecord>>(Subs.Values.OrderBy(s => s.CreatedAt).ToList()); }
    public Task<bool> RemoveSubscriptionAsync(string id, CancellationToken ct) { lock (_gate) return Task.FromResult(Subs.Remove(id)); }
    public Task<bool> UpdateSubscriptionKindsAsync(string id, IReadOnlyList<string> kinds, CancellationToken ct)
    { lock (_gate) { if (!Subs.TryGetValue(id, out var s)) return Task.FromResult(false); Subs[id] = s with { Kinds = kinds }; return Task.FromResult(true); } }
    public Task TouchSubscriptionAsync(string id, DateTimeOffset at, CancellationToken ct)
    { lock (_gate) { if (Subs.TryGetValue(id, out var s)) Subs[id] = s with { LastSentAt = at }; } return Task.CompletedTask; }
}
