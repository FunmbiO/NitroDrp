using WebPush;

namespace NitroDrp.Api.Notifications;

public enum PushResult
{
    Sent,
    /// <summary>The push service says this subscription no longer exists (404 or 410): remove the device.</summary>
    Gone,
    Failed,
}

public interface IPushTransport
{
    /// <summary>False when push isn't set up on this server.</summary>
    bool Enabled { get; }

    /// <summary>The key the browser needs to subscribe (the "application server key"), or null when push is off.</summary>
    string? PublicKey { get; }

    Task<PushResult> SendAsync(PushSubscriptionRecord sub, string payloadJson, CancellationToken ct);
}

public sealed class NoPushTransport : IPushTransport
{
    public bool Enabled => false;
    public string? PublicKey => null;
    public Task<PushResult> SendAsync(PushSubscriptionRecord sub, string payloadJson, CancellationToken ct) => Task.FromResult(PushResult.Failed);
}

/// <summary>Sends encrypted Web Push messages (RFC 8030, 8291, 8292), signed with this server's VAPID key.</summary>
public sealed class WebPushTransport(PushOptions options, HttpClient http, ILogger<WebPushTransport> log) : IPushTransport
{
    private readonly VapidDetails _vapid = new(options.Subject!, options.PublicKey!, options.PrivateKey!);
    private readonly WebPushClient _client = new(http);

    public bool Enabled => true;
    public string? PublicKey => options.PublicKey;

    public async Task<PushResult> SendAsync(PushSubscriptionRecord sub, string payloadJson, CancellationToken ct)
    {
        try
        {
            await _client.SendNotificationAsync(new PushSubscription(sub.Endpoint, sub.P256dh, sub.Auth), payloadJson, _vapid, ct);
            return PushResult.Sent;
        }
        catch (WebPushException e) when (e.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone)
        {
            return PushResult.Gone;
        }
        catch (Exception e) when (e is WebPushException or HttpRequestException or TaskCanceledException)
        {
            // The kind of failure only: the address is the browser's private channel to this owner.
            log.LogWarning("A push could not be delivered: {Type}", e.GetType().Name);
            return PushResult.Failed;
        }
    }
}
