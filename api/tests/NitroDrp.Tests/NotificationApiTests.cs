using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using NitroDrp.Api.Notifications;

namespace NitroDrp.Tests;

public class NotificationApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static int _ip;
    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private HttpRequestMessage Req(HttpMethod m, string path, object? body = null, bool owner = true)
    {
        var r = new HttpRequestMessage(m, path);
        r.Headers.Add("X-Client-IP", $"10.77.{Interlocked.Increment(ref _ip) / 250}.{_ip % 250}");
        if (owner) r.Headers.Add("X-Api-Key", ApiFactory.ApiKey);
        if (body is not null) r.Content = JsonContent.Create(body);
        return r;
    }

    private static string K65() { var b = new byte[65]; b[0] = 4; return WebEncoders.Base64UrlEncode(b); }
    private static string A16() => WebEncoders.Base64UrlEncode(new byte[16]);

    private object Sub(string endpoint = "https://fcm.googleapis.com/fcm/send/one", object? kinds = null, string label = "Pixel") =>
        new { endpoint, p256dh = K65(), auth = A16(), label, kinds };

    private async Task<JsonElement> View()
    {
        var res = await _http.SendAsync(Req(HttpMethod.Get, "/api/notifications"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("no-store", res.Headers.CacheControl?.ToString());
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task Reset()
    {
        await factory.Notes.ClearEventsAsync(default);
        foreach (var s in await factory.Notes.ListSubscriptionsAsync(default)) await factory.Notes.RemoveSubscriptionAsync(s.Id, default);
        factory.Push.Sent.Clear();
        factory.Push.Respond = _ => PushResult.Sent;
    }

    [Fact]
    public async Task Everything_refuses_anonymous_callers()
    {
        foreach (var (m, p) in new[] { ("GET", "/api/notifications"), ("GET", "/api/notifications/unread"), ("POST", "/api/notifications/read"),
                     ("DELETE", "/api/notifications"), ("POST", "/api/notifications/devices"), ("PUT", "/api/notifications/digest") })
        {
            var res = await _http.SendAsync(Req(new HttpMethod(m), p, m is "POST" or "PUT" ? Sub() : null, owner: false));
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        }
        var wrong = Req(HttpMethod.Get, "/api/notifications", owner: false);
        wrong.Headers.Add("X-Api-Key", "not-the-key-at-all-1234");
        Assert.Equal(HttpStatusCode.Unauthorized, (await _http.SendAsync(wrong)).StatusCode);
    }

    [Fact]
    public async Task Feed_lists_newest_first_with_unread_and_marks_read()
    {
        await Reset();
        var t = new DateTimeOffset(2026, 11, 11, 10, 0, 0, TimeSpan.Zero);
        await factory.Notes.AddEventAsync(new("a", NotificationKinds.Downloaded, "tok1", 1, t), default);
        await factory.Notes.AddEventAsync(new("b", NotificationKinds.LimitReached, "tok1", 1, t.AddMinutes(5)), default);

        var v = await View();
        Assert.Equal(2, v.GetProperty("unread").GetInt32());
        Assert.Equal("limit_reached", v.GetProperty("items")[0].GetProperty("kind").GetString());
        Assert.Equal("Download limit reached", v.GetProperty("items")[0].GetProperty("title").GetString());
        var unread = await _http.SendAsync(Req(HttpMethod.Get, "/api/notifications/unread"));
        Assert.Equal(2, JsonDocument.Parse(await unread.Content.ReadAsStringAsync()).RootElement.GetProperty("unread").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/read"))).StatusCode);
        Assert.Equal(0, (await View()).GetProperty("unread").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(Req(HttpMethod.Delete, "/api/notifications"))).StatusCode);
        Assert.Equal(0, (await View()).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Config_is_reported_for_the_page()
    {
        await Reset();
        var v = await View();
        Assert.True(v.GetProperty("pushAvailable").GetBoolean());
        Assert.Equal("test-public-key", v.GetProperty("publicKey").GetString());
        Assert.Equal(4, v.GetProperty("kinds").GetArrayLength());
        Assert.False(v.GetProperty("digest").GetProperty("available").GetBoolean()); // no summary address configured in tests
    }

    [Fact]
    public async Task A_device_can_subscribe_change_kinds_test_and_leave()
    {
        await Reset();
        var add = await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/devices", Sub(kinds: new[] { "downloaded", "bogus" })));
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        var dev = JsonDocument.Parse(await add.Content.ReadAsStringAsync()).RootElement;
        var id = dev.GetProperty("id").GetString()!;
        Assert.Equal(["downloaded"], dev.GetProperty("kinds").EnumerateArray().Select(k => k.GetString()));

        // The same browser subscribing again is still one device.
        await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/devices", Sub()));
        Assert.Equal(1, (await View()).GetProperty("devices").GetArrayLength());

        var put = await _http.SendAsync(Req(HttpMethod.Put, $"/api/notifications/devices/{id}", new { kinds = new[] { "email_failed" } }));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(["email_failed"], (await factory.Notes.ListSubscriptionsAsync(default)).Single().Kinds);

        var test = await _http.SendAsync(Req(HttpMethod.Post, $"/api/notifications/devices/{id}/test"));
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        Assert.Single(factory.Push.Sent);

        Assert.Equal(HttpStatusCode.NoContent, (await _http.SendAsync(Req(HttpMethod.Delete, $"/api/notifications/devices/{id}"))).StatusCode);
        Assert.Equal(0, (await View()).GetProperty("devices").GetArrayLength());
    }

    [Theory]
    [InlineData("http://fcm.googleapis.com/x")]
    [InlineData("https://169.254.169.254/latest")]
    [InlineData("https://localhost/x")]
    [InlineData("https://evil.example.com/x")]
    public async Task A_subscription_pointing_anywhere_but_a_push_service_is_refused(string endpoint)
    {
        await Reset();
        var res = await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/devices", Sub(endpoint)));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Empty(await factory.Notes.ListSubscriptionsAsync(default));
    }

    [Fact]
    public async Task Bad_keys_and_odd_labels_are_handled()
    {
        await Reset();
        var bad = await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/devices",
            new { endpoint = "https://fcm.googleapis.com/x", p256dh = "AAAA", auth = A16() }));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/devices", Sub(label: new string('x', 200) + "\r\nSet-Cookie: a")));
        var label = (await factory.Notes.ListSubscriptionsAsync(default)).Single().Label;
        Assert.True(label.Length <= 40);
        Assert.DoesNotContain('\n', label);
    }

    [Fact]
    public async Task The_number_of_devices_is_capped()
    {
        await Reset();
        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/devices", Sub($"https://fcm.googleapis.com/d{i}")))).StatusCode);
        var over = await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/devices", Sub("https://fcm.googleapis.com/d11")));
        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        // An existing device can still refresh its subscription.
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/devices", Sub("https://fcm.googleapis.com/d3")))).StatusCode);
    }

    [Fact]
    public async Task Unknown_or_malformed_device_ids_are_404()
    {
        await Reset();
        foreach (var id in new[] { "0123456789abcdef0123456789abcdef", "ABC", new string('g', 32) })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Req(HttpMethod.Put, $"/api/notifications/devices/{id}", new { kinds = new[] { "downloaded" } }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await _http.SendAsync(Req(HttpMethod.Post, $"/api/notifications/devices/{id}/test"))).StatusCode);
        }
    }

    [Fact]
    public async Task A_dead_device_is_removed_when_its_test_finds_it_gone()
    {
        await Reset();
        var id = JsonDocument.Parse(await (await _http.SendAsync(Req(HttpMethod.Post, "/api/notifications/devices", Sub()))).Content.ReadAsStringAsync()).RootElement.GetProperty("id").GetString();
        factory.Push.Respond = _ => PushResult.Gone;
        var res = await _http.SendAsync(Req(HttpMethod.Post, $"/api/notifications/devices/{id}/test"));
        Assert.True(JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement.GetProperty("gone").GetBoolean());
        Assert.Empty(await factory.Notes.ListSubscriptionsAsync(default));
    }

    [Fact]
    public async Task A_download_shows_up_in_the_feed_end_to_end()
    {
        await Reset();
        // Make a live link with a download limit of one, download it, and the owner hears twice.
        var create = Req(HttpMethod.Post, "/api/shares", new { files = new[] { new { name = "a.txt", size = 3 } }, expiresInDays = 7, downloadLimit = 1 });
        var created = JsonDocument.Parse(await (await _http.SendAsync(create)).Content.ReadAsStringAsync()).RootElement;
        var token = created.GetProperty("token").GetString()!;
        var fileId = created.GetProperty("files")[0].GetProperty("id").GetString()!;
        var ownerKey = created.GetProperty("ownerKey").GetString()!;
        factory.Storage.Content[factory.Store.Shares[token].Files.Single().StorageKey] = "abc"u8.ToArray();
        factory.Storage.DeclaredSizes[factory.Store.Shares[token].Files.Single().StorageKey] = 3;
        foreach (var (path, body) in new (string, object?)[] { ($"/api/shares/{token}/files/{fileId}/complete", new { parts = new[] { new { partNumber = 1, eTag = "e" } } }), ($"/api/shares/{token}/finalize", null) })
        {
            var r = Req(HttpMethod.Post, path, body, owner: false);
            r.Headers.Add("X-Owner-Key", ownerKey);
            Assert.True((await _http.SendAsync(r)).IsSuccessStatusCode, path);
        }
        var dl = Req(HttpMethod.Post, $"/api/shares/{token}/download", new { }, owner: false);
        Assert.Equal(HttpStatusCode.OK, (await _http.SendAsync(dl)).StatusCode);

        // The queue is drained by a background worker.
        for (var i = 0; i < 100 && (await factory.Notes.ListEventsAsync(10, default)).Count < 2; i++) await Task.Delay(50);
        var kinds = (await View()).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("kind").GetString()).Order().ToList();
        Assert.Equal(["downloaded", "limit_reached"], kinds);
    }
}
