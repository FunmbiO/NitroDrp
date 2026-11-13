using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NitroDrp.Api;
using NitroDrp.Api.Notifications;
using NitroDrp.Api.Services;
using NitroDrp.Api.Storage;

namespace NitroDrp.Tests;

public class NotificationServiceTests
{
    private readonly InMemoryNotificationStore _store = new();
    private readonly FakePushTransport _push = new();
    private readonly FakeEmailSender _mail = new();
    private readonly InMemoryShareStore _shares = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 11, 14, 0, 0, TimeSpan.Zero));
    private readonly NitroDrpOptions _o = new()
    {
        SenderName = "Jaded Media",
        PublicBaseUrl = "https://drop.example.com",
        Email = { Enabled = true, FromAddress = "transfers@example.com" },
        Notifications = { DigestEmailTo = "owner@example.com", DigestHourUtc = 13, ExpiringHours = 24 },
    };

    private NotificationService Svc() => new(_store, _push, Options.Create(_o), _clock, NullLogger<NotificationService>.Instance);

    private static PushSubscriptionRecord Sub(string id, params string[] kinds) =>
        new(id, $"https://fcm.googleapis.com/{id}", "k", "a", "Phone", kinds.Length > 0 ? kinds : NotificationKinds.All.ToArray(), DateTimeOffset.UnixEpoch, null);

    [Fact]
    public async Task An_event_is_stored_and_pushed_only_to_devices_that_asked_for_it()
    {
        await _store.AddSubscriptionAsync(Sub("a", NotificationKinds.Downloaded), default);
        await _store.AddSubscriptionAsync(Sub("b", NotificationKinds.EmailFailed), default);
        var e = await Svc().PublishAsync(NotificationKinds.Downloaded, "tok", 1, default);

        Assert.NotNull(e);
        Assert.Single(await _store.ListEventsAsync(10, default));
        Assert.Equal(["https://fcm.googleapis.com/a"], _push.Sent.Select(s => s.Endpoint));
        Assert.NotNull((await _store.ListSubscriptionsAsync(default)).Single(s => s.Id == "a").LastSentAt);
    }

    [Fact]
    public async Task Pushes_carry_generic_text_and_no_link_token()
    {
        await _store.AddSubscriptionAsync(Sub("a"), default);
        await Svc().PublishAsync(NotificationKinds.LimitReached, "secret-token-123", 1, default);
        var payload = _push.Sent.Single().Payload;
        Assert.DoesNotContain("secret-token-123", payload);
        var doc = JsonDocument.Parse(payload).RootElement;
        Assert.Equal("Download limit reached", doc.GetProperty("title").GetString());
        Assert.Equal("/notifications", doc.GetProperty("url").GetString());
    }

    [Fact]
    public async Task A_device_the_push_service_says_is_gone_is_forgotten()
    {
        await _store.AddSubscriptionAsync(Sub("a"), default);
        await _store.AddSubscriptionAsync(Sub("b"), default);
        _push.Respond = s => s.Id == "a" ? PushResult.Gone : PushResult.Sent;
        await Svc().PublishAsync(NotificationKinds.Downloaded, "t", 1, default);
        Assert.Equal(["b"], (await _store.ListSubscriptionsAsync(default)).Select(s => s.Id));
    }

    [Fact]
    public async Task A_failed_push_keeps_the_device_and_the_event_stays_in_the_feed()
    {
        await _store.AddSubscriptionAsync(Sub("a"), default);
        _push.Respond = _ => PushResult.Failed;
        await Svc().PublishAsync(NotificationKinds.Downloaded, "t", 1, default);
        Assert.Single(await _store.ListSubscriptionsAsync(default));
        Assert.Single(await _store.ListEventsAsync(10, default));
    }

    [Fact]
    public async Task With_push_off_the_feed_still_works()
    {
        _push.Enabled = false;
        await _store.AddSubscriptionAsync(Sub("a"), default);
        await Svc().PublishAsync(NotificationKinds.Downloaded, "t", 1, default);
        Assert.Empty(_push.Sent);
        Assert.Single(await _store.ListEventsAsync(10, default));
    }

    [Fact]
    public async Task Unknown_kinds_are_ignored()
    {
        Assert.Null(await Svc().PublishAsync("made_up", "t", 1, default));
        Assert.Empty(await _store.ListEventsAsync(10, default));
    }

    [Fact]
    public async Task Test_push_goes_to_one_device_and_is_not_in_the_feed()
    {
        await _store.AddSubscriptionAsync(Sub("a"), default);
        await _store.AddSubscriptionAsync(Sub("b"), default);
        Assert.Equal(PushResult.Sent, await Svc().SendTestAsync("a", default));
        Assert.Single(_push.Sent);
        Assert.Empty(await _store.ListEventsAsync(10, default));
        Assert.Null(await Svc().SendTestAsync("zzz", default));
    }

    // ---- daily summary

    private async Task Events() {
        await _store.AddEventAsync(new("1", NotificationKinds.Downloaded, "t", 1, _clock.GetUtcNow().AddHours(-2)), default);
        await _store.AddEventAsync(new("2", NotificationKinds.Downloaded, "t", 1, _clock.GetUtcNow().AddHours(-1)), default);
        await _store.AddEventAsync(new("3", NotificationKinds.EmailFailed, "t", 2, _clock.GetUtcNow().AddHours(-1)), default);
    }

    [Fact]
    public async Task Summary_sends_counts_once_a_day_and_not_before_its_hour()
    {
        await Events();
        await _store.SetDigestEnabledAsync(true, default);
        _clock.Set(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
        Assert.False(await Svc().SendDigestAsync(_mail, _clock.GetUtcNow(), default)); // before 13:00

        _clock.Set(new DateTimeOffset(2026, 11, 11, 14, 0, 0, TimeSpan.Zero));
        Assert.True(await Svc().SendDigestAsync(_mail, _clock.GetUtcNow(), default));
        Assert.False(await Svc().SendDigestAsync(_mail, _clock.GetUtcNow(), default)); // already today
        var m = Assert.Single(_mail.Sent);
        Assert.Equal("owner@example.com", m.To);
        Assert.Contains("Link downloaded: 2", m.TextBody);
        Assert.Contains("Email didn't send: 2", m.TextBody);
        Assert.Contains("https://drop.example.com/notifications", m.TextBody);
    }

    [Fact]
    public async Task Summary_is_off_until_enabled_and_silent_on_a_quiet_day()
    {
        await Events();
        Assert.False(await Svc().SendDigestAsync(_mail, _clock.GetUtcNow(), default)); // not enabled
        await _store.SetDigestEnabledAsync(true, default);
        _clock.Set(_clock.GetUtcNow().AddDays(3)); // everything is older than a day
        Assert.False(await Svc().SendDigestAsync(_mail, _clock.GetUtcNow(), default));
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task Summary_needs_an_address_and_email_set_up()
    {
        await Events();
        await _store.SetDigestEnabledAsync(true, default);
        _o.Notifications.DigestEmailTo = null;
        Assert.False(await Svc().SendDigestAsync(_mail, _clock.GetUtcNow(), default));
        _o.Notifications.DigestEmailTo = "owner@example.com";
        _o.Email.Enabled = false;
        Assert.False(await Svc().SendDigestAsync(_mail, _clock.GetUtcNow(), default));
    }

    [Fact]
    public async Task Summary_never_contains_file_names()
    {
        await Events();
        await _store.SetDigestEnabledAsync(true, default);
        await Svc().SendDigestAsync(_mail, _clock.GetUtcNow(), default);
        Assert.DoesNotContain("Gallery", _mail.Sent.Single().TextBody + _mail.Sent.Single().HtmlBody);
    }

    // ---- sweep worker

    private NotificationWorker Worker()
    {
        var shareSvc = new ShareService(_shares, new FakeStorage(), new S3DownloadLinkSigner(new FakeStorage()), new NoEdgeCache(), _mail,
            ZipLinks.FromApiKey("test-api-key-aaaaaaaa", _clock), Options.Create(_o), _clock, NullLogger<ShareService>.Instance);
        return new NotificationWorker(new QueuedNotifier(), Svc(), _store, shareSvc, _mail, Options.Create(_o), _clock, NullLogger<NotificationWorker>.Instance);
    }

    [Fact]
    public async Task Sweep_warns_once_about_links_ending_soon_and_ignores_the_rest()
    {
        var now = _clock.GetUtcNow();
        _shares.Summaries["soon"] = new("soon", now.AddDays(-6), now.AddHours(5), 1, 10, false, 0);
        _shares.Summaries["later"] = new("later", now, now.AddDays(5), 1, 10, false, 0);
        _shares.Summaries["gone"] = new("gone", now.AddDays(-9), now.AddHours(-1), 1, 10, false, 0);
        var w = Worker();
        await w.RunSweepAsync(default);
        await w.RunSweepAsync(default);
        var e = Assert.Single(await _store.ListEventsAsync(10, default));
        Assert.Equal(NotificationKinds.ExpiringSoon, e.Kind);
        Assert.Equal("soon", e.Token);
    }

    [Fact]
    public async Task Queue_hands_events_to_the_worker_without_blocking()
    {
        var q = new QueuedNotifier();
        for (var i = 0; i < 600; i++) q.Publish(NotificationKinds.Downloaded, "t"); // more than the queue holds: drops old, never blocks
        Assert.True(q.Reader.TryRead(out var item));
        Assert.Equal(NotificationKinds.Downloaded, item!.Kind);
        await Task.CompletedTask;
    }
}
