using Microsoft.AspNetCore.Http;
using NitroDrp.Api.Auth;

namespace NitroDrp.Tests;

public class SessionTests
{
    private const string Key = "test-api-key-aaaaaaaa";
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
    private SessionTokens Tokens(string key = Key, int days = 90) => new(key, _clock, TimeSpan.FromDays(days));

    [Fact]
    public void A_fresh_token_validates_and_says_which_passkey_signed_in()
    {
        var t = Tokens();
        var s = t.Validate(t.Issue("cred-1"))!;
        Assert.Equal("cred-1", s.CredentialId);
        Assert.Equal(_clock.GetUtcNow().AddDays(90), s.ExpiresAt);
    }

    [Fact]
    public void A_token_stops_working_when_it_expires()
    {
        var t = Tokens(days: 90);
        var token = t.Issue("c");
        _clock.Advance(TimeSpan.FromDays(89));
        Assert.NotNull(t.Validate(token));
        _clock.Advance(TimeSpan.FromDays(2));
        Assert.Null(t.Validate(token));
    }

    [Fact]
    public void Tampering_with_any_part_is_rejected()
    {
        var t = Tokens();
        var parts = t.Issue("cred-1").Split('.');
        var other = t.Issue("cred-2").Split('.');
        Assert.Null(t.Validate($"{parts[0]}.{other[1]}.{parts[2]}")); // someone else's payload, my signature
        Assert.Null(t.Validate($"v2.{parts[1]}.{parts[2]}"));
        Assert.Null(t.Validate($"{parts[0]}.{parts[1]}.{parts[2][..^2]}AA"));
        foreach (var junk in new[] { null, "", "x", "a.b.c", "v1..", new string('a', 2000) }) Assert.Null(t.Validate(junk));
    }

    [Fact]
    public void A_token_from_a_different_api_key_is_rejected_so_changing_the_key_signs_everyone_out()
    {
        var token = Tokens(Key).Issue("c");
        Assert.Null(Tokens("a-different-api-key-1234").Validate(token));
    }

    // --- the request side ---

    private (Sessions Sessions, InMemoryPasskeyStore Keys, SessionTokens Tokens) Setup()
    {
        var keys = new InMemoryPasskeyStore();
        keys.AddAsync(new Passkey("cred-1", [1], 0, "Phone", _clock.GetUtcNow(), null), default).Wait();
        var tokens = Tokens();
        return (new Sessions(tokens, keys, _clock, secureCookie: true), keys, tokens);
    }

    private static HttpRequest Request(string method, string? cookie, bool csrf)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        if (cookie is not null) ctx.Request.Headers.Cookie = $"{Sessions.CookieName}={cookie}";
        if (csrf) ctx.Request.Headers[Sessions.CsrfHeader] = Sessions.CsrfValue;
        return ctx.Request;
    }

    [Fact]
    public async Task A_valid_cookie_signs_the_request_in()
    {
        var (s, _, t) = Setup();
        Assert.NotNull(await s.GetAsync(Request("GET", t.Issue("cred-1"), csrf: false), default));
        Assert.Null(await s.GetAsync(Request("GET", null, csrf: false), default));
        Assert.Null(await s.GetAsync(Request("GET", "garbage", csrf: false), default));
    }

    [Fact]
    public async Task A_request_that_changes_things_also_needs_the_csrf_header()
    {
        var (s, _, t) = Setup();
        var cookie = t.Issue("cred-1");
        Assert.Null(await s.GetAsync(Request("POST", cookie, csrf: false), default));
        Assert.Null(await s.GetAsync(Request("DELETE", cookie, csrf: false), default));
        Assert.NotNull(await s.GetAsync(Request("POST", cookie, csrf: true), default));
    }

    [Fact]
    public async Task Removing_a_passkey_signs_its_sessions_out()
    {
        var (s, keys, t) = Setup();
        var cookie = t.Issue("cred-1");
        Assert.NotNull(await s.GetAsync(Request("GET", cookie, false), default));

        await keys.RemoveAsync("cred-1", default);
        s.Forget("cred-1");
        Assert.Null(await s.GetAsync(Request("GET", cookie, false), default));
    }

    [Fact]
    public async Task Without_being_told_a_removed_passkey_is_noticed_within_a_minute()
    {
        var (s, keys, t) = Setup();
        var cookie = t.Issue("cred-1");
        Assert.NotNull(await s.GetAsync(Request("GET", cookie, false), default));
        await keys.RemoveAsync("cred-1", default);
        Assert.NotNull(await s.GetAsync(Request("GET", cookie, false), default)); // still inside the cached minute
        _clock.Advance(TimeSpan.FromSeconds(61));
        Assert.Null(await s.GetAsync(Request("GET", cookie, false), default));
    }

    [Fact]
    public void The_cookie_is_httponly_strict_secure_and_scoped_to_the_api()
    {
        var (s, _, _) = Setup();
        var ctx = new DefaultHttpContext();
        s.SignIn(ctx.Response, "cred-1");
        var header = ctx.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", header, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api", header, StringComparison.OrdinalIgnoreCase);
    }
}
