using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using NitroDrp.Api.Email;

namespace NitroDrp.Api.Notifications;

/// <summary>What the rest of the app calls when something happens. Never throws and never waits on the network.</summary>
public interface INotifier
{
    void Publish(string kind, string? token, int count = 1);
}

public sealed class NoNotifier : INotifier
{
    public void Publish(string kind, string? token, int count = 1) { }
}

/// <summary>Short, generic text for each kind. Never a file name or an address: pushes pass through other companies' servers.</summary>
public static class NotificationText
{
    public static (string Title, string Body) For(string kind, int count) => kind switch
    {
        NotificationKinds.Downloaded => ("Link downloaded", count > 1 ? $"One of your links was downloaded ({count} times)." : "Someone downloaded one of your links."),
        NotificationKinds.LimitReached => ("Download limit reached", "One of your links has used up its downloads."),
        NotificationKinds.ExpiringSoon => ("Link expiring soon", "One of your links ends soon."),
        NotificationKinds.EmailFailed => ("Email didn't send", "An email with one of your links could not be delivered."),
        _ => ("NitroDrp", "Something happened."),
    };
}

/// <summary>Records an event in the feed, then pushes it to the owner's devices that asked for that kind.</summary>
public sealed class NotificationService(INotificationStore store, IPushTransport push, IOptions<NitroDrpOptions> options, TimeProvider clock, ILogger<NotificationService> log)
{
    public async Task<NotificationEvent?> PublishAsync(string kind, string? token, int count, CancellationToken ct)
    {
        if (!NotificationKinds.IsKnown(kind)) return null;
        var at = clock.GetUtcNow();
        var e = new NotificationEvent(Guid.NewGuid().ToString("N"), kind, token, Math.Max(1, count), at);
        await store.AddEventAsync(e, ct);
        if (push.Enabled) await PushAsync(e, ct);
        return e;
    }

    /// <summary>The payload the service worker shows. Opens the notifications page when tapped.</summary>
    public static string Payload(NotificationEvent e)
    {
        var (title, body) = NotificationText.For(e.Kind, e.Count);
        return JsonSerializer.Serialize(new { title, body, kind = e.Kind, url = "/notifications", tag = e.Kind });
    }

    private async Task PushAsync(NotificationEvent e, CancellationToken ct)
    {
        var payload = Payload(e);
        foreach (var sub in await store.ListSubscriptionsAsync(ct))
        {
            if (!sub.Kinds.Contains(e.Kind)) continue;
            var result = await push.SendAsync(sub, payload, ct);
            if (result == PushResult.Gone) await store.RemoveSubscriptionAsync(sub.Id, ct);
            else if (result == PushResult.Sent) await store.TouchSubscriptionAsync(sub.Id, clock.GetUtcNow(), ct);
        }
    }

    /// <summary>A test push to one device, so the owner can check it works. Not stored in the feed.</summary>
    public async Task<PushResult?> SendTestAsync(string id, CancellationToken ct)
    {
        var sub = (await store.ListSubscriptionsAsync(ct)).FirstOrDefault(s => s.Id == id);
        if (sub is null || !push.Enabled) return null;
        var payload = JsonSerializer.Serialize(new { title = "NitroDrp", body = "Notifications are working on this device.", kind = "test", url = "/notifications", tag = "test" });
        var result = await push.SendAsync(sub, payload, ct);
        if (result == PushResult.Gone) await store.RemoveSubscriptionAsync(sub.Id, ct);
        return result;
    }

    /// <summary>The daily email: counts only, with a link to the app. Sends nothing on a quiet day.</summary>
    public async Task<bool> SendDigestAsync(IEmailSender email, DateTimeOffset now, CancellationToken ct)
    {
        var o = options.Value;
        var to = o.Notifications.DigestEmailTo;
        if (!o.Email.Enabled || string.IsNullOrWhiteSpace(to) || string.IsNullOrWhiteSpace(o.Email.FromAddress)) return false;
        if (now.Hour < Math.Clamp(o.Notifications.DigestHourUtc, 0, 23)) return false;
        var settings = await store.GetSettingsAsync(ct);
        if (!settings.DigestEnabled) return false;
        var date = now.UtcDateTime.ToString("yyyy-MM-dd");
        if (!await store.TryClaimDigestAsync(date, ct)) return false;

        var events = await store.ListEventsSinceAsync(now.AddHours(-24), ct);
        if (events.Count == 0) return false;
        var lines = NotificationKinds.All
            .Select(k => (Kind: k, N: events.Where(e => e.Kind == k).Sum(e => e.Count)))
            .Where(x => x.N > 0)
            .Select(x => $"{NotificationText.For(x.Kind, 1).Title}: {x.N}")
            .ToList();
        var link = string.IsNullOrWhiteSpace(o.PublicBaseUrl) ? "" : $"{o.PublicBaseUrl!.TrimEnd('/')}/notifications";
        var text = "Here's what happened to your links in the last day:\n\n" + string.Join("\n", lines) + (link.Length > 0 ? $"\n\nSee the details: {link}\n" : "\n");
        var html = "<p>Here's what happened to your links in the last day:</p><ul>" + string.Join("", lines.Select(l => $"<li>{WebUtility.HtmlEncode(l)}</li>")) + "</ul>"
                   + (link.Length > 0 ? $"<p><a href=\"{WebUtility.HtmlEncode(link)}\">See the details</a></p>" : "");
        try
        {
            await email.SendAsync(new EmailMessage(to!, o.SenderName, o.Email.FromAddress!, null, "Your NitroDrp daily summary", text, html), ct);
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning("The daily summary could not be sent: {Type}", e.GetType().Name);
            return false;
        }
    }
}

/// <summary>Hands events to a background worker so a download never waits on a push service.</summary>
public sealed class QueuedNotifier : INotifier
{
    public record Item(string Kind, string? Token, int Count);

    private readonly Channel<Item> _channel = Channel.CreateBounded<Item>(new BoundedChannelOptions(500) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<Item> Reader => _channel.Reader;

    public void Publish(string kind, string? token, int count = 1) => _channel.Writer.TryWrite(new Item(kind, token, count));
}

public sealed class NotificationWorker(QueuedNotifier queue, NotificationService service, INotificationStore store, Services.ShareService shares,
    IEmailSender email, IOptions<NitroDrpOptions> options, TimeProvider clock, ILogger<NotificationWorker> log) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stop) => Task.WhenAll(Drain(stop), Sweep(stop));

    private async Task Drain(CancellationToken stop)
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync(stop))
            {
                try { await service.PublishAsync(item.Kind, item.Token, item.Count, stop); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("A notification could not be recorded: {Type}", e.GetType().Name); }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task Sweep(CancellationToken stop)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
        try
        {
            do
            {
                try { await RunSweepAsync(stop); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning("A notification check failed: {Type}", e.GetType().Name); }
            } while (await timer.WaitForNextTickAsync(stop));
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Warns about links ending soon (once each), then sends the daily summary if it's due. Public for tests.</summary>
    public async Task RunSweepAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var window = TimeSpan.FromHours(Math.Max(1, options.Value.Notifications.ExpiringHours));
        foreach (var s in await shares.ListMineAsync(ct))
        {
            if (s.ExpiresAt - now > window) continue;
            if (await store.TryClaimExpiryNoticeAsync(s.Token, s.ExpiresAt.AddDays(1), ct))
                await service.PublishAsync(NotificationKinds.ExpiringSoon, s.Token, 1, ct);
        }
        await service.SendDigestAsync(email, now, ct);
    }
}
