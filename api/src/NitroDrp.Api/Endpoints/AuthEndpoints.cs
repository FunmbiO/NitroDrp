using Fido2NetLib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using NitroDrp.Api.Auth;
using NitroDrp.Api.Services;

namespace NitroDrp.Api.Endpoints;

public sealed record AuthStatus(bool PasskeysEnabled, bool SignedIn, bool HasPasskeys);
public sealed record BeginRegistrationResponse(string FlowId, Fido2NetLib.CredentialCreateOptions Options);
public sealed record BeginLoginResponse(string FlowId, Fido2NetLib.AssertionOptions Options);
public sealed record FinishRegistrationRequest(string FlowId, string? Label, AuthenticatorAttestationRawResponse Credential);
public sealed record FinishLoginRequest(string FlowId, AuthenticatorAssertionRawResponse Credential);
public sealed record PasskeySummary(string Id, string Label, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt, bool Current);

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/auth");

        // Who am I, and is signing in with a passkey available? Also renews a sign-in that's been quiet for a day,
        // so a session lapses only after SessionDays with no use at all.
        auth.MapGet("/status", async (HttpRequest http, HttpResponse res, Sessions sessions, IPasskeyStore keys, IOptions<NitroDrpOptions> o, TimeProvider clock, CancellationToken ct) =>
        {
            var session = await sessions.GetAsync(http, ct);
            if (session is not null && clock.GetUtcNow() - session.IssuedAt > TimeSpan.FromDays(1))
                sessions.SignIn(res, session.CredentialId);
            res.Headers.CacheControl = "no-store";
            var has = o.Value.Auth.Passkeys && (await keys.ListAsync(ct)).Count > 0;
            return Results.Ok(new AuthStatus(o.Value.Auth.Passkeys, session is not null, has));
        }).RequireRateLimiting("public");

        // Adding a device needs to be the owner already: signed in on another device, or typing the access key.
        auth.MapPost("/register/begin", async (HttpRequest http, Sessions sessions, IOptions<NitroDrpOptions> o, IServiceProvider sp, CancellationToken ct) =>
        {
            var service = Passkeys(sp);
            await RequireAdmin(http, sessions, o, ct);
            var begun = await service.BeginRegistrationAsync(ct);
            return Results.Ok(new BeginRegistrationResponse(begun.FlowId, begun.Options));
        }).RequireRateLimiting("auth");

        auth.MapPost("/register/finish", async ([FromBody] FinishRegistrationRequest body, HttpRequest http, HttpResponse res, Sessions sessions, IOptions<NitroDrpOptions> o, IServiceProvider sp, CancellationToken ct) =>
        {
            var service = Passkeys(sp);
            var caller = await RequireAdmin(http, sessions, o, ct);
            var key = await service.FinishRegistrationAsync(body.FlowId, body.Credential, body.Label, ct);
            // Enrolling with the access key is also signing in on this device: no second prompt.
            if (caller.Session is null) sessions.SignIn(res, key.Id);
            return Results.Ok(new PasskeySummary(key.Id, key.Label, key.CreatedAt, key.LastUsedAt, Current: caller.Session is null));
        }).RequireRateLimiting("auth");

        auth.MapPost("/login/begin", (IServiceProvider sp) =>
        {
            var begun = Passkeys(sp).BeginLogin();
            return Results.Ok(new BeginLoginResponse(begun.FlowId, begun.Options));
        }).RequireRateLimiting("auth");

        auth.MapPost("/login/finish", async ([FromBody] FinishLoginRequest body, HttpResponse res, Sessions sessions, IServiceProvider sp, CancellationToken ct) =>
        {
            var id = await Passkeys(sp).FinishLoginAsync(body.FlowId, body.Credential, ct);
            sessions.SignIn(res, id);
            return Results.Ok(new { signedIn = true });
        }).RequireRateLimiting("auth");

        auth.MapPost("/logout", (HttpResponse res, Sessions sessions) =>
        {
            sessions.SignOut(res);
            return Results.NoContent();
        }).RequireRateLimiting("public");

        // Managing devices needs a real sign-in, not just the access key.
        auth.MapGet("/passkeys", async (HttpRequest http, Sessions sessions, IPasskeyStore keys, CancellationToken ct) =>
        {
            var session = await sessions.GetAsync(http, ct) ?? throw SignInRequired();
            var list = await keys.ListAsync(ct);
            return Results.Ok(list.Select(k => new PasskeySummary(k.Id, k.Label, k.CreatedAt, k.LastUsedAt, k.Id == session.CredentialId)));
        }).RequireRateLimiting("public");

        auth.MapDelete("/passkeys/{id}", async (string id, HttpRequest http, Sessions sessions, IPasskeyStore keys, CancellationToken ct) =>
        {
            _ = await sessions.GetAsync(http, ct) ?? throw SignInRequired();
            if (!Ids.IsCredentialId(id) || !await keys.RemoveAsync(id, ct)) throw ServiceException.NotFound();
            sessions.Forget(id); // that device is signed out now, not in a minute
            return Results.NoContent();
        }).RequireRateLimiting("auth");
    }

    private static PasskeyService Passkeys(IServiceProvider sp) =>
        sp.GetService<PasskeyService>() ?? throw ServiceException.NotFound("passkeys_disabled", "Passkeys aren't turned on for this server.");

    private static async Task<AdminCaller> RequireAdmin(HttpRequest http, Sessions sessions, IOptions<NitroDrpOptions> o, CancellationToken ct)
    {
        var caller = await AdminAccess.CheckAsync(http, sessions, o.Value.ApiKey, ct);
        return caller.IsAdmin ? caller : throw SignInRequired();
    }

    private static ServiceException SignInRequired() => ServiceException.Unauthorized("sign_in_required", "Sign in first.");
}
