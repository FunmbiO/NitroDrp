using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NitroDrp.Tests;

/// <summary>Enrolling a device and signing in, through the real HTTP pipeline and the real passkey verification.</summary>
public class PasskeyApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // Cookies are carried by hand: the test server is plain http, and the real cookie is Secure.
    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static int _next;

    private sealed class Device(HttpClient http, string? cookie = null)
    {
        public string? Cookie = cookie;
        private readonly string _ip = $"10.9.{Interlocked.Increment(ref _next) / 250}.{Interlocked.Increment(ref _next) % 250}"; // each device is its own client, so they don't share one rate limit

        public async Task<HttpResponseMessage> Send(HttpMethod method, string path, object? body = null, bool apiKey = false, bool csrf = true)
        {
            var req = new HttpRequestMessage(method, path);
            if (body is not null) req.Content = JsonContent.Create(body);
            if (Cookie is not null) req.Headers.Add("Cookie", Cookie);
            req.Headers.Add("X-Client-IP", _ip);
            if (csrf) req.Headers.Add("X-Requested-With", "nitrodrp");
            if (apiKey) req.Headers.Add("X-Api-Key", ApiFactory.ApiKey);
            var res = await http.SendAsync(req);
            if (res.Headers.TryGetValues("Set-Cookie", out var set))
            {
                var pair = set.First().Split(';')[0];
                Cookie = pair.EndsWith('=') ? null : pair; // an expired cookie clears it
            }
            return res;
        }
    }

    private Device NewDevice() => new(_http);
    private static async Task<JsonElement> Json(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task<string> Code(HttpResponseMessage r) => (await Json(r)).GetProperty("code").GetString()!;

    /// <summary>Enrol a new passkey, proving ownership with the access key (or an existing sign-in).</summary>
    private async Task<(SoftAuthenticator Phone, Device Device, JsonElement Saved)> Enrol(string label = "My phone", Device? device = null, bool apiKey = true)
    {
        device ??= NewDevice();
        var phone = new SoftAuthenticator();
        var begin = await device.Send(HttpMethod.Post, "/api/auth/register/begin", apiKey: apiKey);
        Assert.Equal(HttpStatusCode.OK, begin.StatusCode);
        var b = await Json(begin);
        var done = await device.Send(HttpMethod.Post, "/api/auth/register/finish",
            new { flowId = b.GetProperty("flowId").GetString(), label, credential = phone.Register(b.GetProperty("options")) }, apiKey: apiKey);
        Assert.Equal(HttpStatusCode.OK, done.StatusCode);
        return (phone, device, await Json(done));
    }

    private async Task<HttpResponseMessage> SignIn(SoftAuthenticator phone, Device device, Action<byte[]>? tamper = null)
    {
        var begin = await Json(await device.Send(HttpMethod.Post, "/api/auth/login/begin"));
        return await device.Send(HttpMethod.Post, "/api/auth/login/finish",
            new { flowId = begin.GetProperty("flowId").GetString(), credential = phone.Login(begin.GetProperty("options"), tamper) });
    }

    private static async Task<bool> SignedIn(Device d) => (await Json(await d.Send(HttpMethod.Get, "/api/auth/status"))).GetProperty("signedIn").GetBoolean();

    [Fact]
    public async Task Status_says_passkeys_are_available_and_nobody_is_signed_in()
    {
        var s = await Json(await NewDevice().Send(HttpMethod.Get, "/api/auth/status"));
        Assert.True(s.GetProperty("passkeysEnabled").GetBoolean());
        Assert.False(s.GetProperty("signedIn").GetBoolean());
    }

    [Fact]
    public async Task Adding_a_device_needs_the_access_key_or_an_existing_sign_in()
    {
        var res = await NewDevice().Send(HttpMethod.Post, "/api/auth/register/begin");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("sign_in_required", await Code(res));

        var wrong = NewDevice();
        var bad = await wrong.Send(HttpMethod.Post, "/api/auth/register/begin", apiKey: false);
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Enrolling_with_the_access_key_also_signs_this_device_in()
    {
        var (_, device, saved) = await Enrol("Amina's iPhone");
        Assert.Equal("Amina's iPhone", saved.GetProperty("label").GetString());
        Assert.True(await SignedIn(device));
        Assert.Contains(factory.Passkeys.Keys.Values, k => k.Label == "Amina's iPhone");
    }

    [Fact]
    public async Task A_passkey_signs_in_a_fresh_device_with_no_access_key()
    {
        var (phone, _, _) = await Enrol();
        var other = NewDevice(); // a browser that has never seen the access key
        Assert.False(await SignedIn(other));

        var res = await SignIn(phone, other);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(await SignedIn(other));
    }

    [Fact]
    public async Task Signing_in_can_repeat_and_records_when_the_passkey_was_used()
    {
        var (phone, _, _) = await Enrol();
        Assert.Equal(HttpStatusCode.OK, (await SignIn(phone, NewDevice())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SignIn(phone, NewDevice())).StatusCode);
        Assert.NotNull(factory.Passkeys.Keys[phone.CredentialId].LastUsedAt);
        Assert.True(factory.Passkeys.Keys[phone.CredentialId].SignCount >= 2);
    }

    [Fact]
    public async Task A_forged_signature_is_refused()
    {
        var (phone, _, _) = await Enrol();
        var d = NewDevice();
        var res = await SignIn(phone, d, sig => sig[^1] ^= 0xFF);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("passkey_rejected", await Code(res));
        Assert.False(await SignedIn(d));
    }

    [Fact]
    public async Task A_passkey_the_server_does_not_know_is_refused()
    {
        var (phone, _, _) = await Enrol("Temp");
        factory.Passkeys.Keys.Remove(phone.CredentialId); // as if it had never been enrolled
        var res = await SignIn(phone, NewDevice());
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task A_sign_in_attempt_cannot_be_replayed()
    {
        var (phone, _, _) = await Enrol();
        var d = NewDevice();
        var begin = await Json(await d.Send(HttpMethod.Post, "/api/auth/login/begin"));
        var body = new { flowId = begin.GetProperty("flowId").GetString(), credential = phone.Login(begin.GetProperty("options")) };
        Assert.Equal(HttpStatusCode.OK, (await d.Send(HttpMethod.Post, "/api/auth/login/finish", body)).StatusCode);

        var again = await NewDevice().Send(HttpMethod.Post, "/api/auth/login/finish", body);
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal("flow_expired", await Code(again));
    }

    [Fact]
    public async Task A_passkey_made_for_a_different_site_is_refused()
    {
        var phishing = new SoftAuthenticator(origin: "https://evil.example.net", rpId: "drop.example.com");
        var d = NewDevice();
        var begin = await Json(await d.Send(HttpMethod.Post, "/api/auth/register/begin", apiKey: true));
        var done = await d.Send(HttpMethod.Post, "/api/auth/register/finish",
            new { flowId = begin.GetProperty("flowId").GetString(), label = "x", credential = phishing.Register(begin.GetProperty("options")) }, apiKey: true);
        Assert.NotEqual(HttpStatusCode.OK, done.StatusCode);
    }

    [Fact]
    public async Task Removing_a_device_signs_it_out_and_it_cannot_sign_back_in()
    {
        var (phone, _, _) = await Enrol("Old laptop");
        var other = NewDevice();
        await SignIn(phone, other);
        Assert.True(await SignedIn(other));

        var (_, manager, _) = await Enrol("Phone"); // a second, signed-in device does the removing
        var del = await manager.Send(HttpMethod.Delete, $"/api/auth/passkeys/{phone.CredentialId}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        Assert.False(await SignedIn(other));
        Assert.Equal(HttpStatusCode.Unauthorized, (await SignIn(phone, NewDevice())).StatusCode);
    }

    [Fact]
    public async Task Device_management_needs_a_real_sign_in_not_just_the_access_key()
    {
        var res = await NewDevice().Send(HttpMethod.Get, "/api/auth/passkeys", apiKey: true);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);

        var (_, device, _) = await Enrol("Mine");
        var list = await Json(await device.Send(HttpMethod.Get, "/api/auth/passkeys"));
        Assert.Contains(list.EnumerateArray(), k => k.GetProperty("label").GetString() == "Mine" && k.GetProperty("current").GetBoolean());
    }

    [Fact]
    public async Task Logging_out_clears_the_sign_in()
    {
        var (_, device, _) = await Enrol();
        Assert.True(await SignedIn(device));
        Assert.Equal(HttpStatusCode.NoContent, (await device.Send(HttpMethod.Post, "/api/auth/logout")).StatusCode);
        Assert.False(await SignedIn(device));
    }

    [Fact]
    public async Task Labels_are_cleaned_and_limited_in_length()
    {
        var (_, _, saved) = await Enrol("  Phone\r\nwith\tcontrols " + new string('x', 100));
        var label = saved.GetProperty("label").GetString()!;
        Assert.True(label.Length <= 40);
        Assert.DoesNotContain('\n', label);
    }

    [Fact]
    public async Task With_passkeys_switched_off_the_endpoints_say_so()
    {
        using var off = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("NitroDrp:ApiKey", ApiFactory.ApiKey);
            b.UseSetting("NitroDrp:S3:Bucket", "b");
        });
        var http = off.CreateClient();
        var status = await http.GetFromJsonAsync<JsonElement>("/api/auth/status");
        Assert.False(status.GetProperty("passkeysEnabled").GetBoolean());
        var res = await http.PostAsync("/api/auth/login/begin", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
