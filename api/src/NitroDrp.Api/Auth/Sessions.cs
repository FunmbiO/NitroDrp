using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace NitroDrp.Api.Auth;

/// <summary>
/// Signed-in state for a request, kept in an HttpOnly cookie (script on the page can't read it, unlike a stored key).
/// A session stays valid only while its passkey still exists, so removing a device signs it out.
/// </summary>
public sealed class Sessions(SessionTokens tokens, IPasskeyStore keys, TimeProvider clock, bool secureCookie)
{
    public const string CookieName = "nd_session";

    /// <summary>
    /// Requests that change things must carry this header. Browsers don't let other sites add it, so even if a cookie
    /// were ever sent cross-site (it's SameSite=Strict, so it isn't), the request would be refused.
    /// </summary>
    public const string CsrfHeader = "X-Requested-With";
    public const string CsrfValue = "nitrodrp";

    private static readonly TimeSpan Recheck = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<string, (bool Exists, DateTimeOffset At)> _known = new();

    public async Task<SessionInfo?> GetAsync(HttpRequest req, CancellationToken ct)
    {
        if (!req.Cookies.TryGetValue(CookieName, out var cookie)) return null;
        if (!HttpMethods.IsGet(req.Method) && !HttpMethods.IsHead(req.Method)
            && req.Headers[CsrfHeader].ToString() != CsrfValue)
            return null;

        var session = tokens.Validate(cookie);
        if (session is null) return null;
        return await StillEnrolledAsync(session.CredentialId, ct) ? session : null;
    }

    public void SignIn(HttpResponse res, string credentialId) =>
        res.Cookies.Append(CookieName, tokens.Issue(credentialId), Options(clock.GetUtcNow() + tokens.Lifetime));

    public void SignOut(HttpResponse res) => res.Cookies.Delete(CookieName, Options(null));

    /// <summary>Call after removing a passkey so its sessions stop at once, not after the recheck interval.</summary>
    public void Forget(string credentialId) => _known.TryRemove(credentialId, out _);

    private async Task<bool> StillEnrolledAsync(string id, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (_known.TryGetValue(id, out var hit) && now - hit.At < Recheck) return hit.Exists;
        var exists = await keys.GetAsync(id, ct) is not null;
        _known[id] = (exists, now);
        return exists;
    }

    private CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = secureCookie,
        SameSite = SameSiteMode.Strict,
        Path = "/api",
        Expires = expires,
    };
}
