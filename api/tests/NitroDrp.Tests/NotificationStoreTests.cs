using NitroDrp.Api.Notifications;

namespace NitroDrp.Tests;

/// <summary>The contract INotificationStore must keep. (The DynamoDB version is exercised end to end by the real-stack browser test.)</summary>
public class NotificationStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private static NotificationEvent Ev(string id, int minute, string kind = NotificationKinds.Downloaded) => new(id, kind, "AbCdEfGh1234", 1, T0.AddMinutes(minute));
    private static PushSubscriptionRecord Sub(string id, params string[] kinds) => new(id, $"https://fcm.googleapis.com/x/{id}", "p", "a", "Phone", kinds, T0, null);

    [Fact]
    public async Task Events_come_back_newest_first_and_can_be_limited()
    {
        var s = new InMemoryNotificationStore();
        foreach (var (id, m) in new[] { ("a", 1), ("c", 3), ("b", 2) }) await s.AddEventAsync(Ev(id, m), default);
        Assert.Equal(["c", "b", "a"], (await s.ListEventsAsync(10, default)).Select(e => e.Id));
        Assert.Equal(["c", "b"], (await s.ListEventsAsync(2, default)).Select(e => e.Id));
    }

    [Fact]
    public async Task Events_since_a_moment_include_that_moment_and_come_back_oldest_first()
    {
        var s = new InMemoryNotificationStore();
        foreach (var (id, m) in new[] { ("a", 1), ("b", 2), ("c", 3) }) await s.AddEventAsync(Ev(id, m), default);
        Assert.Equal(["b", "c"], (await s.ListEventsSinceAsync(T0.AddMinutes(2), default)).Select(e => e.Id));
    }

    [Fact]
    public async Task Clearing_removes_the_feed_but_not_settings_or_devices()
    {
        var s = new InMemoryNotificationStore();
        await s.AddEventAsync(Ev("a", 1), default);
        await s.AddSubscriptionAsync(Sub("d1", NotificationKinds.Downloaded), default);
        await s.SetDigestEnabledAsync(true, default);
        await s.ClearEventsAsync(default);
        Assert.Empty(await s.ListEventsAsync(10, default));
        Assert.Single(await s.ListSubscriptionsAsync(default));
        Assert.True((await s.GetSettingsAsync(default)).DigestEnabled);
    }

    [Fact]
    public async Task Settings_start_empty_and_remember_what_was_set()
    {
        var s = new InMemoryNotificationStore();
        var d = await s.GetSettingsAsync(default);
        Assert.Null(d.LastReadAt);
        Assert.False(d.DigestEnabled);
        await s.SetLastReadAsync(T0, default);
        await s.SetDigestEnabledAsync(true, default);
        var now = await s.GetSettingsAsync(default);
        Assert.Equal(T0, now.LastReadAt);
        Assert.True(now.DigestEnabled);
    }

    [Fact]
    public async Task The_daily_summary_can_be_claimed_once_per_date_even_by_racing_callers()
    {
        var s = new InMemoryNotificationStore();
        var wins = (await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => Task.Run(() => s.TryClaimDigestAsync("2026-11-13", default))))).Count(x => x);
        Assert.Equal(1, wins);
        Assert.False(await s.TryClaimDigestAsync("2026-11-13", default));
        Assert.True(await s.TryClaimDigestAsync("2026-11-14", default)); // a new day is a new claim
    }

    [Fact]
    public async Task An_expiry_warning_is_given_once_per_link()
    {
        var s = new InMemoryNotificationStore();
        Assert.True(await s.TryClaimExpiryNoticeAsync("AbCdEfGh1234", T0, default));
        Assert.False(await s.TryClaimExpiryNoticeAsync("AbCdEfGh1234", T0, default));
        Assert.True(await s.TryClaimExpiryNoticeAsync("ZzZzZzZz9999", T0, default));
    }

    [Fact]
    public async Task Subscribing_again_replaces_the_device_instead_of_duplicating_it()
    {
        var s = new InMemoryNotificationStore();
        await s.AddSubscriptionAsync(Sub("d1", NotificationKinds.Downloaded), default);
        await s.AddSubscriptionAsync(Sub("d1", NotificationKinds.EmailFailed), default);
        var list = await s.ListSubscriptionsAsync(default);
        Assert.Single(list);
        Assert.Equal([NotificationKinds.EmailFailed], list[0].Kinds);
    }

    [Fact]
    public async Task A_device_can_change_which_kinds_it_wants_and_be_removed_but_not_resurrected_by_a_late_touch()
    {
        var s = new InMemoryNotificationStore();
        await s.AddSubscriptionAsync(Sub("d1", NotificationKinds.Downloaded), default);
        Assert.True(await s.UpdateSubscriptionKindsAsync("d1", [NotificationKinds.LimitReached], default));
        Assert.False(await s.UpdateSubscriptionKindsAsync("nope", [], default));
        await s.TouchSubscriptionAsync("d1", T0.AddHours(1), default);
        Assert.Equal(T0.AddHours(1), (await s.ListSubscriptionsAsync(default))[0].LastSentAt);

        Assert.True(await s.RemoveSubscriptionAsync("d1", default));
        await s.TouchSubscriptionAsync("d1", T0.AddHours(2), default);
        Assert.Empty(await s.ListSubscriptionsAsync(default));
        Assert.False(await s.RemoveSubscriptionAsync("d1", default));
    }

    [Fact]
    public void Only_the_known_kinds_are_known()
    {
        foreach (var k in NotificationKinds.All) Assert.True(NotificationKinds.IsKnown(k));
        Assert.False(NotificationKinds.IsKnown("bogus"));
        Assert.False(NotificationKinds.IsKnown(null));
        Assert.False(NotificationKinds.IsKnown(""));
    }
}
