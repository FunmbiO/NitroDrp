using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using NitroDrp.Api.Auth;
using NitroDrp.Api.Email;
using NitroDrp.Api.Notifications;
using NitroDrp.Api.Services;

namespace NitroDrp.Api.Endpoints;

public sealed record NotificationItem(string Id, string Kind, string Title, string Body, int Count, DateTimeOffset At, string? Token, bool Unread);
public sealed record DeviceView(string Id, string Label, IReadOnlyList<string> Kinds, DateTimeOffset CreatedAt, DateTimeOffset? LastSentAt);
public sealed record DigestView(bool Available, bool Enabled, int HourUtc);
public sealed record NotificationsView(
    IReadOnlyList<NotificationItem> Items, int Unread, bool PushAvailable, string? PublicKey,
    IReadOnlyList<DeviceView> Devices, DigestView Digest, IReadOnlyList<string> Kinds);
public sealed record SubscribeRequest(string? Endpoint, string? P256dh, string? Auth, string? Label, List<string>? Kinds);
public sealed record KindsRequest(List<string>? Kinds);
public sealed record DigestRequest(bool Enabled);

/// <summary>Everything here is the owner's alone: signed in with a passkey, or holding the access key. Nothing is public.</summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/notifications");

        g.MapGet("/", async (HttpRequest http, HttpResponse res, Sessions sessions, INotificationStore store, IPushTransport push, IOptions<NitroDrpOptions> o, CancellationToken ct) =>
        {
            await RequireAdmin(http, sessions, o, ct);
            res.Headers.CacheControl = "no-store";
            var settings = await store.GetSettingsAsync(ct);
            var items = (await store.ListEventsAsync(100, ct)).Select(e => Item(e, settings)).ToList();
            var devices = (await store.ListSubscriptionsAsync(ct)).Select(Device).ToList();
            var n = o.Value.Notifications;
            var digestAvailable = o.Value.Email.Enabled && !string.IsNullOrWhiteSpace(n.DigestEmailTo);
            return Results.Ok(new NotificationsView(items, items.Count(i => i.Unread), push.Enabled, push.PublicKey, devices,
                new DigestView(digestAvailable, digestAvailable && settings.DigestEnabled, Math.Clamp(n.DigestHourUtc, 0, 23)), NotificationKinds.All));
        }).RequireRateLimiting("public");

        // Just the number, for the bell in the header.
        g.MapGet("/unread", async (HttpRequest http, HttpResponse res, Sessions sessions, INotificationStore store, IOptions<NitroDrpOptions> o, CancellationToken ct) =>
        {
            await RequireAdmin(http, sessions, o, ct);
            res.Headers.CacheControl = "no-store";
            var settings = await store.GetSettingsAsync(ct);
            var unread = (await store.ListEventsAsync(100, ct)).Count(e => IsUnread(e, settings));
            return Results.Ok(new { unread });
        }).RequireRateLimiting("public");

        g.MapPost("/read", async (HttpRequest http, Sessions sessions, INotificationStore store, IOptions<NitroDrpOptions> o, TimeProvider clock, CancellationToken ct) =>
        {
            await RequireAdmin(http, sessions, o, ct);
            await store.SetLastReadAsync(clock.GetUtcNow(), ct);
            return Results.NoContent();
        }).RequireRateLimiting("public");

        g.MapDelete("/", async (HttpRequest http, Sessions sessions, INotificationStore store, IOptions<NitroDrpOptions> o, CancellationToken ct) =>
        {
            await RequireAdmin(http, sessions, o, ct);
            await store.ClearEventsAsync(ct);
            return Results.NoContent();
        }).RequireRateLimiting("public");

        // Turn on push for this device. The browser made the subscription; the server checks it points at a real push service.
        g.MapPost("/devices", async ([FromBody] SubscribeRequest body, HttpRequest http, Sessions sessions, INotificationStore store, IPushTransport push, IOptions<NitroDrpOptions> o, TimeProvider clock, CancellationToken ct) =>
        {
            await RequireAdmin(http, sessions, o, ct);
            if (!push.Enabled) throw ServiceException.NotFound("push_disabled", "Push notifications aren't set up on this server.");
            if (PushEndpointPolicy.Check(body.Endpoint, body.P256dh, body.Auth) is { } why)
                throw ServiceException.BadRequest("bad_subscription", why);
            var id = PushEndpointPolicy.IdFor(body.Endpoint!);
            var existing = await store.ListSubscriptionsAsync(ct);
            if (existing.All(s => s.Id != id) && existing.Count >= Math.Max(1, o.Value.Notifications.MaxDevices))
                throw ServiceException.Conflict("too_many_devices", "Remove a device before adding another.");
            var label = TextSafety.SingleLine(body.Label);
            if (label.Length == 0) label = "This device";
            if (label.Length > 40) label = label[..40];
            var rec = new PushSubscriptionRecord(id, body.Endpoint!, body.P256dh!, body.Auth!, label, CleanKinds(body.Kinds), clock.GetUtcNow(), null);
            await store.AddSubscriptionAsync(rec, ct);
            return Results.Ok(Device(rec));
        }).RequireRateLimiting("public");

        g.MapPut("/devices/{id}", async (string id, [FromBody] KindsRequest body, HttpRequest http, Sessions sessions, INotificationStore store, IOptions<NitroDrpOptions> o, CancellationToken ct) =>
        {
            await RequireAdmin(http, sessions, o, ct);
            RequireDeviceId(id);
            var kinds = CleanKinds(body.Kinds);
            if (!await store.UpdateSubscriptionKindsAsync(id, kinds, ct)) throw DeviceNotFound();
            return Results.Ok(new { kinds });
        }).RequireRateLimiting("public");

        g.MapDelete("/devices/{id}", async (string id, HttpRequest http, Sessions sessions, INotificationStore store, IOptions<NitroDrpOptions> o, CancellationToken ct) =>
        {
            await RequireAdmin(http, sessions, o, ct);
            RequireDeviceId(id);
            await store.RemoveSubscriptionAsync(id, ct);
            return Results.NoContent();
        }).RequireRateLimiting("public");

        g.MapPost("/devices/{id}/test", async (string id, HttpRequest http, Sessions sessions, NotificationService svc, IOptions<NitroDrpOptions> o, CancellationToken ct) =>
        {
            await RequireAdmin(http, sessions, o, ct);
            RequireDeviceId(id);
            var result = await svc.SendTestAsync(id, ct) ?? throw DeviceNotFound();
            return Results.Ok(new { sent = result == PushResult.Sent, gone = result == PushResult.Gone });
        }).RequireRateLimiting("auth");

        g.MapPut("/digest", async ([FromBody] DigestRequest body, HttpRequest http, Sessions sessions, INotificationStore store, IOptions<NitroDrpOptions> o, CancellationToken ct) =>
        {
            await RequireAdmin(http, sessions, o, ct);
            if (!o.Value.Email.Enabled || string.IsNullOrWhiteSpace(o.Value.Notifications.DigestEmailTo))
                throw ServiceException.NotFound("digest_disabled", "The daily summary isn't set up on this server.");
            await store.SetDigestEnabledAsync(body.Enabled, ct);
            return Results.Ok(new { enabled = body.Enabled });
        }).RequireRateLimiting("public");
    }

    private static bool IsUnread(NotificationEvent e, NotificationSettings s) => s.LastReadAt is null || e.At > s.LastReadAt;

    private static NotificationItem Item(NotificationEvent e, NotificationSettings s)
    {
        var (title, body) = NotificationText.For(e.Kind, e.Count);
        return new(e.Id, e.Kind, title, body, e.Count, e.At, e.Token, IsUnread(e, s));
    }

    private static DeviceView Device(PushSubscriptionRecord s) => new(s.Id, s.Label, s.Kinds, s.CreatedAt, s.LastSentAt);

    private static List<string> CleanKinds(List<string>? kinds)
    {
        var clean = (kinds ?? []).Where(NotificationKinds.IsKnown).Distinct().ToList();
        return clean.Count > 0 ? clean : NotificationKinds.All.ToList();
    }

    private static void RequireDeviceId(string id)
    {
        if (!PushEndpointPolicy.IsDeviceId(id)) throw DeviceNotFound();
    }

    private static ServiceException DeviceNotFound() => ServiceException.NotFound("device_not_found", "That device isn't registered.");

    private static async Task RequireAdmin(HttpRequest http, Sessions sessions, IOptions<NitroDrpOptions> o, CancellationToken ct)
    {
        var caller = await AdminAccess.CheckAsync(http, sessions, o.Value.ApiKey, ct);
        if (!caller.IsAdmin) throw ServiceException.Unauthorized("sign_in_required", "Sign in first.");
    }
}
