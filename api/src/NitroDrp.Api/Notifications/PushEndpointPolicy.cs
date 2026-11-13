using Microsoft.AspNetCore.WebUtilities;

namespace NitroDrp.Api.Notifications;

/// <summary>
/// A push subscription tells the server a web address to send notifications to. The server will contact whatever
/// address it's given, so it only accepts the addresses of real browser push services, over https. That way a
/// subscription can never be used to make the server call an internal address or some other site.
/// </summary>
public static class PushEndpointPolicy
{
    /// <summary>The push services Chrome and Android, Firefox, Safari and iOS, and Edge use. A host must be one of these or a subdomain.</summary>
    public static readonly string[] AllowedHosts =
    [
        "fcm.googleapis.com",
        "android.googleapis.com",
        "push.services.mozilla.com",
        "web.push.apple.com",
        "notify.windows.com",
    ];

    /// <summary>Null if the subscription is acceptable, otherwise why not (safe to show to the person).</summary>
    public static string? Check(string? endpoint, string? p256dh, string? auth)
    {
        if (string.IsNullOrWhiteSpace(endpoint) || endpoint.Length > 2048 || !Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            return "That isn't a valid push address.";
        if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || !uri.IsDefaultPort || uri.HostNameType != UriHostNameType.Dns)
            return "That push address isn't allowed.";
        var host = uri.IdnHost.ToLowerInvariant();
        if (!AllowedHosts.Any(h => host == h || host.EndsWith("." + h, StringComparison.Ordinal)))
            return "That push service isn't supported.";

        // The browser's public key is an uncompressed P-256 point (65 bytes starting 0x04); the auth secret is 16 bytes.
        if (!TryDecode(p256dh, 65, out var key) || key[0] != 0x04) return "That subscription's key isn't valid.";
        if (!TryDecode(auth, 16, out _)) return "That subscription's secret isn't valid.";
        return null;
    }

    private static bool TryDecode(string? s, int length, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(s) || s.Length > 200) return false;
        try
        {
            bytes = WebEncoders.Base64UrlDecode(s);
            return bytes.Length == length;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>The id a device is known by: a hash of its endpoint, so subscribing twice from the same browser is one device.</summary>
    public static string IdFor(string endpoint) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(endpoint)))[..32].ToLowerInvariant();

    public static bool IsDeviceId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigitLower);
}
