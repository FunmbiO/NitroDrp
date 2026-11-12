using System.Net;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace NitroDrp.Tests;

/// <summary>
/// The server is the real gate, whatever the page shows. This takes every route the API has and tries it as someone
/// with no credentials, and with wrong ones. Only the short list of public routes may answer with success.
/// If a new route is added without a check, this fails and says which.
/// </summary>
public class AnonymousAccessTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    /// <summary>The only routes meant for anyone, and why: liveness, what the page needs before sign-in, and starting a sign-in.</summary>
    private static readonly HashSet<string> Public =
    [
        "GET /api/health",
        "GET /api/config",
        "GET /api/auth/status",
        "POST /api/auth/login/begin",   // starts a passkey prompt; finishing it needs the device's signature
        "POST /api/auth/logout",        // clears a cookie
    ];

    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    private static int _ip;

    private static string Fill(string pattern) => pattern
        .Replace("{token}", "AbCdEfGh1234").Replace("{fileId}", "0123456789abcdef").Replace("{id}", "AbC-123").TrimEnd('/');

    private IEnumerable<(string Method, string Path)> AllRoutes() =>
        factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api") == true)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
                .Select(m => (m.ToUpperInvariant(), Fill(e.RoutePattern.RawText!))))
            .Distinct();

    private async Task<HttpStatusCode> Send(string method, string path, Action<HttpRequestMessage>? credentials)
    {
        var req = new HttpRequestMessage(new HttpMethod(method), path);
        if (method is "POST" or "PUT" or "PATCH") req.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        req.Headers.Add("X-Client-IP", $"10.55.{Interlocked.Increment(ref _ip) / 250}.{_ip % 250}"); // its own rate-limit bucket
        credentials?.Invoke(req);
        return (await _http.SendAsync(req)).StatusCode;
    }

    [Fact]
    public void The_sweep_really_finds_the_routes()
    {
        var routes = AllRoutes().Select(r => $"{r.Method} {r.Path}").ToList();
        Assert.True(routes.Count >= 20, $"only {routes.Count} routes found: the sweep isn't covering the API: {string.Join(", ", routes)}");
        Assert.Contains("POST /api/shares", routes);
        Assert.Contains("GET /api/shares", routes);
        Assert.Contains("DELETE /api/shares/AbCdEfGh1234", routes);
        Assert.Contains("POST /api/auth/register/begin", routes);
    }

    public static IEnumerable<object[]> Visitors() =>
    [
        ["nobody", 0],
        ["a wrong access key", 1],
        ["a wrong owner key", 2],
        ["a forged sign-in cookie", 3],
        ["a forged cookie with the right header", 4],
    ];

    private static void Credentials(int kind, HttpRequestMessage r)
    {
        switch (kind)
        {
            case 1: r.Headers.Add("X-Api-Key", "not-the-access-key-at-all-123"); break;
            case 2: r.Headers.Add("X-Owner-Key", "not-an-owner-key"); break;
            case 3: r.Headers.Add("Cookie", "nd_session=v1.Zm9yZ2Vk.Zm9yZ2Vk"); break;
            case 4: r.Headers.Add("Cookie", "nd_session=v1.Zm9yZ2Vk.Zm9yZ2Vk"); r.Headers.Add("X-Requested-With", "nitrodrp"); break;
        }
    }

    [Theory]
    [MemberData(nameof(Visitors))]
    public async Task Every_route_refuses_a_visitor_with_no_valid_credentials_unless_it_is_public(string who, int kind)
    {
        var leaks = new List<string>();
        foreach (var (method, path) in AllRoutes())
        {
            var status = await Send(method, path, kind == 0 ? null : r => Credentials(kind, r));
            var key = $"{method} {path}";
            var isPublic = Public.Contains(key.Replace("AbCdEfGh1234", "{token}"));
            if ((int)status is >= 200 and < 300 && !isPublic) leaks.Add($"{key} -> {(int)status}");
            Assert.True((int)status < 500, $"{key} gave {(int)status} for {who}");
        }
        Assert.True(leaks.Count == 0, $"{who} got through on: {string.Join("; ", leaks)}");
    }

    [Fact]
    public async Task What_anonymous_visitors_can_read_is_only_the_public_list()
    {
        foreach (var path in new[] { "/api/shares", "/api/auth/passkeys" })
            Assert.Equal(HttpStatusCode.Unauthorized, await Send("GET", path, null));
        foreach (var path in new[] { "/api/auth/register/begin", "/api/shares" })
            Assert.Equal(HttpStatusCode.Unauthorized, await Send("POST", path, null));
        Assert.Equal(HttpStatusCode.Unauthorized, await Send("DELETE", "/api/auth/passkeys/AbC-123", null));
    }
}
