namespace NitroDrp.Api.Notifications;

/// <summary>What can happen that's worth telling the owner about.</summary>
public static class NotificationKinds
{
    public const string Downloaded = "downloaded";        // someone downloaded from a link
    public const string LimitReached = "limit_reached";   // a link's last download was used
    public const string ExpiringSoon = "expiring_soon";   // a link ends within the warning window
    public const string EmailFailed = "email_failed";     // an email to a recipient couldn't be sent

    public static readonly string[] All = [Downloaded, LimitReached, ExpiringSoon, EmailFailed];
    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind);
}

/// <summary>
/// One thing that happened. It carries no file names and no addresses: only which link, and a count. That keeps the
/// feed, the push text and the daily email free of other people's details.
/// </summary>
public sealed record NotificationEvent(string Id, string Kind, string? Token, int Count, DateTimeOffset At);

public sealed record NotificationSettings(DateTimeOffset? LastReadAt, bool DigestEnabled, string? LastDigestDate)
{
    public static NotificationSettings Default { get; } = new(null, false, null);
}

/// <summary>A device that has agreed to receive push notifications, and which kinds it wants.</summary>
public sealed record PushSubscriptionRecord(
    string Id,
    string Endpoint,
    string P256dh,
    string Auth,
    string Label,
    IReadOnlyList<string> Kinds,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSentAt);

/// <summary>Where notifications, settings and push subscriptions are kept. DynamoDB in production, memory in tests.</summary>
public interface INotificationStore
{
    Task AddEventAsync(NotificationEvent e, CancellationToken ct);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<NotificationEvent>> ListEventsAsync(int limit, CancellationToken ct);

    /// <summary>Every event at or after `since`, oldest first. For the daily summary.</summary>
    Task<IReadOnlyList<NotificationEvent>> ListEventsSinceAsync(DateTimeOffset since, CancellationToken ct);

    Task ClearEventsAsync(CancellationToken ct);

    Task<NotificationSettings> GetSettingsAsync(CancellationToken ct);
    Task SetLastReadAsync(DateTimeOffset at, CancellationToken ct);
    Task SetDigestEnabledAsync(bool enabled, CancellationToken ct);

    /// <summary>Claims "the summary for this date has been sent". True for the first caller only, so it can't go out twice.</summary>
    Task<bool> TryClaimDigestAsync(string date, CancellationToken ct);

    /// <summary>Claims "the expiry warning for this link has been given". True for the first caller only.</summary>
    Task<bool> TryClaimExpiryNoticeAsync(string token, DateTimeOffset forgetAfter, CancellationToken ct);

    /// <summary>Adds a device, or replaces it if the same browser subscribes again.</summary>
    Task AddSubscriptionAsync(PushSubscriptionRecord s, CancellationToken ct);
    Task<IReadOnlyList<PushSubscriptionRecord>> ListSubscriptionsAsync(CancellationToken ct);
    Task<bool> RemoveSubscriptionAsync(string id, CancellationToken ct);
    Task<bool> UpdateSubscriptionKindsAsync(string id, IReadOnlyList<string> kinds, CancellationToken ct);
    Task TouchSubscriptionAsync(string id, DateTimeOffset at, CancellationToken ct);
}
