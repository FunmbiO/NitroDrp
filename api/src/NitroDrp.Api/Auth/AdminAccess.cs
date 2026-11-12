using Microsoft.AspNetCore.Http;
using NitroDrp.Api.Services;

namespace NitroDrp.Api.Auth;

/// <summary>"Is this the owner?" Either signed in with a passkey, or holding the access key (the recovery key and what scripts use).</summary>
public static class AdminAccess
{
    public const string ApiKeyHeader = "X-Api-Key";

    public static async Task<AdminCaller> CheckAsync(HttpRequest http, Sessions sessions, string apiKey, CancellationToken ct)
    {
        var session = await sessions.GetAsync(http, ct);
        if (session is not null) return new AdminCaller(session, ViaKey: false);
        var key = http.Headers[ApiKeyHeader].ToString();
        return key.Length > 0 && Secrets.FixedTimeEquals(key, apiKey) ? new AdminCaller(null, ViaKey: true) : new AdminCaller(null, ViaKey: false);
    }
}

public sealed record AdminCaller(SessionInfo? Session, bool ViaKey)
{
    public bool IsAdmin => Session is not null || ViaKey;
}
