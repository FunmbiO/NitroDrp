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
public sealed class WebPushTransport(PushOptions options, HttpClient http, ILogger<WebPushTransport> log, TimeProvider? clock = null) : IPushTransport
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public bool Enabled => true;
    public string? PublicKey => options.PublicKey;

    public async Task<PushResult> SendAsync(PushSubscriptionRecord sub, string payloadJson, CancellationToken ct)
    {
        try
        {
            var uri = new Uri(sub.Endpoint);
            var body = WebPushProtocol.Encrypt(System.Text.Encoding.UTF8.GetBytes(payloadJson),
                Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(sub.P256dh), Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlDecode(sub.Auth));
            using var req = new HttpRequestMessage(HttpMethod.Post, uri) { Content = new ByteArrayContent(body) };
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            req.Content.Headers.ContentEncoding.Add("aes128gcm");
            req.Headers.TryAddWithoutValidation("Authorization",
                WebPushProtocol.VapidAuthorization(uri.GetLeftPart(UriPartial.Authority), options.Subject!, options.PublicKey!, options.PrivateKey!, _clock.GetUtcNow()));
            req.Headers.TryAddWithoutValidation("TTL", "86400"); // a day: a phone that was off still hears about it, an old message isn't worth showing later
            req.Headers.TryAddWithoutValidation("Urgency", "normal");

            using var res = await http.SendAsync(req, ct);
            if (res.IsSuccessStatusCode) return PushResult.Sent;
            if (res.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Gone) return PushResult.Gone;
            log.LogWarning("A push service refused a message: {Status}", (int)res.StatusCode);
            return PushResult.Failed;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or ArgumentException or FormatException or System.Security.Cryptography.CryptographicException)
        {
            // The kind of failure only: the address is the browser's private channel to this owner.
            log.LogWarning("A push could not be delivered: {Type}", e.GetType().Name);
            return PushResult.Failed;
        }
    }
}
