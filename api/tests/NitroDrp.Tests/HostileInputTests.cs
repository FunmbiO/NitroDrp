using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NitroDrp.Tests;

/// <summary>
/// Throws malformed, oversized and malicious input at the real HTTP pipeline. Whatever it sends, the server must
/// answer with a clean 4xx and a JSON error: never a 500, never a hang, never an echo of the attack.
/// </summary>
public class HostileInputTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _http = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
    private static int _ip;

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, string? json = null, bool apiKey = false, string? ownerKey = null, string contentType = "application/json")
    {
        var req = new HttpRequestMessage(method, path);
        if (json is not null) req.Content = new StringContent(json, Encoding.UTF8, contentType);
        if (apiKey) req.Headers.Add("X-Api-Key", ApiFactory.ApiKey);
        if (ownerKey is not null) req.Headers.TryAddWithoutValidation("X-Owner-Key", ownerKey);
        req.Headers.Add("X-Client-IP", $"10.77.{Interlocked.Increment(ref _ip) / 250}.{_ip % 250}"); // own rate-limit bucket per request
        return await _http.SendAsync(req);
    }

    private static async Task AssertCleanRefusal(HttpResponseMessage res, string what)
    {
        var body = await res.Content.ReadAsStringAsync();
        Assert.True((int)res.StatusCode is >= 400 and < 500, $"{what}: expected a 4xx, got {(int)res.StatusCode}: {body[..Math.Min(200, body.Length)]}");
        if (body.Length > 0 && res.Content.Headers.ContentType?.MediaType == "application/json")
            Assert.True(JsonDocument.Parse(body).RootElement.TryGetProperty("code", out _) || body.Contains("errors") || body.Contains("title"), $"{what}: not a JSON error: {body}");
    }

    // ---- broken bodies ----

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{")]
    [InlineData("{\"files\":")]
    [InlineData("not json")]
    [InlineData("\"a string\"")]
    [InlineData("{\"files\":\"nope\",\"expiresInDays\":7}")]
    [InlineData("{\"files\":[null],\"expiresInDays\":7}")]
    [InlineData("{\"files\":[{}],\"expiresInDays\":7}")]
    [InlineData("{\"files\":[{\"name\":null,\"size\":3}],\"expiresInDays\":7}")]
    [InlineData("{\"files\":[{\"name\":123,\"size\":3}],\"expiresInDays\":7}")]
    [InlineData("{\"files\":[{\"name\":\"a\",\"size\":\"big\"}],\"expiresInDays\":7}")]
    [InlineData("{\"files\":[{\"name\":\"a\",\"size\":1e30}],\"expiresInDays\":7}")]
    [InlineData("{\"files\":[{\"name\":\"a\",\"size\":-5}],\"expiresInDays\":7}")]
    [InlineData("{\"files\":[{\"name\":\"a\",\"size\":3}],\"expiresInDays\":\"7\"}")]
    [InlineData("{\"files\":[{\"name\":\"a\",\"size\":3}],\"expiresInDays\":7.5}")]
    [InlineData("{\"files\":[{\"name\":\"a\",\"size\":3}],\"expiresInDays\":7,\"downloadLimit\":-1}")]
    [InlineData("{\"files\":[{\"name\":\"a\",\"size\":3}],\"expiresInDays\":7,\"downloadLimit\":99999999999}")]
    [InlineData("{\"files\":[{\"name\":\"a\",\"size\":3}],\"expiresInDays\":7,\"downloadLimit\":\"x\"}")]
    [InlineData("{\"files\":[{\"name\":\"a\",\"size\":3}],\"expiresInDays\":7,\"password\":12345}")]
    public async Task Creating_a_link_from_garbage_is_a_clean_4xx(string body) =>
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/shares", body, apiKey: true), $"POST /api/shares {body}");

    [Fact]
    public async Task A_wrong_content_type_is_a_clean_4xx()
    {
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/shares", "files=1", apiKey: true, contentType: "application/x-www-form-urlencoded"), "form body");
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/shares", "<a/>", apiKey: true, contentType: "text/xml"), "xml body");
    }

    [Fact]
    public async Task An_oversized_body_is_refused_not_read_into_memory()
    {
        var huge = "{\"files\":[{\"name\":\"" + new string('a', 3_000_000) + "\",\"size\":3}],\"expiresInDays\":7}";
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/shares", huge, apiKey: true), "3 MB body");
    }

    [Fact]
    public async Task A_deeply_nested_body_is_refused()
    {
        var deep = new string('[', 5000) + new string(']', 5000);
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/shares", deep, apiKey: true), "deeply nested");
    }

    // ---- values that parse but are wrong ----

    private static string Files(string name, long size = 3, string? extra = null) =>
        $"{{\"files\":[{{\"name\":{JsonSerializer.Serialize(name)},\"size\":{size}}}],\"expiresInDays\":7{extra}}}";

    [Fact]
    public async Task Absurd_names_and_settings_are_refused_or_cleaned_but_never_crash()
    {
        foreach (var name in new[] { "", " ", "...", "/", "\\", "../../etc/passwd", "a\0b", "a\r\nSet-Cookie: x=1", new string('a', 5000), "con.txt", "<script>alert(1)</script>.txt", "photo‮gpj.exe" })
        {
            var res = await Send(HttpMethod.Post, "/api/shares", Files(name), apiKey: true);
            var code = (int)res.StatusCode;
            Assert.True(code is 201 or (>= 400 and < 500), $"name {JsonSerializer.Serialize(name)} gave {code}");
        }
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/shares", Files("a.txt", extra: ",\"password\":\"" + new string('p', 300) + "\""), apiKey: true), "300-char password");
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/shares", Files("a.txt", extra: ",\"downloadLimit\":0"), apiKey: true), "a download limit of 0");
    }

    [Fact]
    public async Task Stored_names_never_contain_path_pieces_control_characters_or_direction_tricks()
    {
        var before = factory.Store.Shares.Count;
        foreach (var name in new[] { "../../etc/passwd", "a\r\nX: y", "photo‮gpj.txt", "zero​width.txt", "evil<img src=x onerror=alert(1)>.txt" })
            Assert.Equal(HttpStatusCode.Created, (await Send(HttpMethod.Post, "/api/shares", Files(name), apiKey: true)).StatusCode);
        var stored = factory.Store.Shares.Values.Skip(before).SelectMany(s => s.Files).Select(f => f.Name).ToList();
        Assert.All(stored, n =>
        {
            Assert.DoesNotContain('/', n);
            Assert.DoesNotContain('\\', n);
            Assert.DoesNotContain(n, c => char.IsControl(c));
            Assert.DoesNotContain(n, c => NitroDrp.Api.Services.TextSafety.IsSpoofing(c)); // direction overrides, zero-width
            Assert.DoesNotContain('<', n);
        });
    }

    // ---- what's in the address ----

    [Theory]
    [InlineData("a")]
    [InlineData("%00")]
    [InlineData("..")]
    [InlineData("AAAAAAAAAAAA' OR '1'='1")]
    [InlineData("<script>")]
    public async Task Odd_tokens_are_a_clean_404_everywhere(string token)
    {
        var t = Uri.EscapeDataString(token);
        foreach (var (method, path, body) in new (HttpMethod, string, string?)[]
        {
            (HttpMethod.Get, $"/api/shares/{t}", null),
            (HttpMethod.Post, $"/api/shares/{t}/unlock", "{\"password\":\"x\"}"),
            (HttpMethod.Post, $"/api/shares/{t}/download", "{}"),
            (HttpMethod.Post, $"/api/shares/{t}/zip-link", "{}"),
            (HttpMethod.Get, $"/api/shares/{t}/zip?exp=1&sig=x", null),
            (HttpMethod.Get, $"/api/shares/{t}/upload", null),
            (HttpMethod.Delete, $"/api/shares/{t}", null),
        })
            await AssertCleanRefusal(await Send(method, path, body, ownerKey: "x"), $"{method} {path}");
    }

    [Fact]
    public async Task A_huge_token_is_refused_before_it_reaches_the_database()
    {
        var res = await Send(HttpMethod.Get, "/api/shares/" + new string('A', 3000));
        Assert.True((int)res.StatusCode is 404 or 414 or 400, $"got {(int)res.StatusCode}");
    }

    [Fact]
    public async Task Garbage_in_the_zip_link_is_refused()
    {
        foreach (var q in new[] { "exp=abc&sig=x", "exp=99999999999999999999&sig=x", "sig=" + new string('x', 5000), "" })
            await AssertCleanRefusal(await Send(HttpMethod.Get, $"/api/shares/AbCdEfGh1234/zip?{q}"), $"zip?{q[..Math.Min(30, q.Length)]}");
    }

    // ---- passwords ----

    [Fact]
    public async Task A_giant_unlock_password_is_refused_without_the_expensive_hash()
    {
        var token = await FinishedShare("pw");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var res = await Send(HttpMethod.Post, $"/api/shares/{token}/unlock", JsonSerializer.Serialize(new { password = new string('p', 200_000) }));
        sw.Stop();
        await AssertCleanRefusal(res, "200 KB password");
        Assert.True(sw.ElapsedMilliseconds < 400, $"took {sw.ElapsedMilliseconds} ms: the long password was hashed");
    }

    [Fact]
    public async Task Password_fields_of_the_wrong_type_are_a_clean_4xx()
    {
        var token = await FinishedShare("pw");
        foreach (var body in new[] { "{\"password\":123}", "{\"password\":[\"a\"]}", "{\"password\":{\"a\":1}}", "{\"password\":null}", "[]" })
            await AssertCleanRefusal(await Send(HttpMethod.Post, $"/api/shares/{token}/unlock", body), $"unlock {body}");
    }

    [Fact]
    public async Task A_weird_file_id_in_a_download_is_a_clean_404()
    {
        var token = await FinishedShare(null);
        foreach (var id in new[] { "x", "../x", new string('f', 5000), "%00", "' OR 1=1" })
            await AssertCleanRefusal(await Send(HttpMethod.Post, $"/api/shares/{token}/download", JsonSerializer.Serialize(new { fileId = id })), $"download fileId {id[..Math.Min(10, id.Length)]}");
    }

    // ---- email ----

    [Fact]
    public async Task Email_requests_full_of_attacks_are_refused_cleanly_and_send_nothing()
    {
        var (token, owner) = await Finished(null);
        var before = factory.Mail.Sent.Count;
        foreach (var body in new[]
        {
            "{\"recipients\":null}",
            "{\"recipients\":[null]}",
            "{\"recipients\":[1,2]}",
            "{\"recipients\":\"a@example.com\"}",
            "{\"recipients\":[\"a@example.com\\r\\nBcc: v@example.com\"]}",
            "{\"recipients\":[\"a@example.com,b@example.com\"]}",
            "{\"recipients\":[\"\\\"x y\\\"@example.com\"]}",
            "{\"recipients\":[\"a@example.com\"],\"senderEmail\":\"x@y.z\\nBcc: v@example.com\"}",
            "{\"recipients\":[\"a@example.com\"],\"message\":" + "\"" + new string('m', 5000) + "\"}",
            "{\"recipients\":[\"a@example.com\"],\"senderName\":123}",
        })
            await AssertCleanRefusal(await Send(HttpMethod.Post, $"/api/shares/{token}/email", body, ownerKey: owner), $"email {body[..Math.Min(70, body.Length)]}");
        Assert.Equal(before, factory.Mail.Sent.Count);
    }

    [Fact]
    public async Task Hostile_text_in_an_email_is_neutralised_in_both_parts()
    {
        var (token, owner) = await Finished(null);
        var res = await Send(HttpMethod.Post, $"/api/shares/{token}/email",
            JsonSerializer.Serialize(new { recipients = new[] { "a@example.com" }, senderName = "<b>Eve</b>\r\nBcc: v@example.com", message = "<script>alert(1)</script>\r\nBcc: v@example.com\0‮" }), ownerKey: owner);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var mail = factory.Mail.Sent[^1];
        Assert.DoesNotContain("<script>", mail.HtmlBody);
        Assert.DoesNotContain("<b>Eve", mail.HtmlBody);
        Assert.DoesNotContain('\n', mail.Subject);
        Assert.DoesNotContain('\r', mail.Subject);
        Assert.DoesNotContain('\0', mail.TextBody);
        Assert.DoesNotContain('‮', mail.TextBody + mail.HtmlBody);
    }

    // ---- headers ----

    [Fact]
    public async Task Absurd_key_headers_are_refused()
    {
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/shares", Files("a"), ownerKey: new string('k', 20_000)), "huge owner key");
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/shares") { Content = new StringContent(Files("a"), Encoding.UTF8, "application/json") };
        req.Headers.TryAddWithoutValidation("X-Api-Key", new string('k', 100_000));
        var res = await _http.SendAsync(req);
        Assert.True((int)res.StatusCode is >= 400 and < 500, $"got {(int)res.StatusCode}");
    }

    // ---- passkey endpoints ----

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"flowId\":null,\"credential\":null}")]
    [InlineData("{\"flowId\":\"x\",\"credential\":{}}")]
    [InlineData("{\"flowId\":\"x\",\"credential\":{\"id\":\"!!\",\"rawId\":\"!!\",\"type\":\"public-key\",\"response\":{}}}")]
    public async Task Garbage_sent_to_the_passkey_endpoints_is_a_clean_4xx(string body)
    {
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/auth/login/finish", body), $"login/finish {body}");
        await AssertCleanRefusal(await Send(HttpMethod.Post, "/api/auth/register/finish", body, apiKey: true), $"register/finish {body}");
    }

    [Fact]
    public async Task Passkey_labels_and_ids_with_attacks_are_handled()
    {
        foreach (var id in new[] { "x", "../x", new string('a', 5000), "%00" })
            Assert.True((int)(await Send(HttpMethod.Delete, $"/api/auth/passkeys/{Uri.EscapeDataString(id)}", apiKey: true)).StatusCode is >= 400 and < 500);
    }

    // ---- helpers ----

    private async Task<string> FinishedShare(string? password) => (await Finished(password)).Token;

    private async Task<(string Token, string Owner)> Finished(string? password)
    {
        var body = Files("a.txt", extra: password is null ? "" : $",\"password\":\"{password}\"");
        var created = await (await Send(HttpMethod.Post, "/api/shares", body, apiKey: true)).Content.ReadFromJsonAsync<JsonElement>();
        var token = created.GetProperty("token").GetString()!;
        var ownerKey = created.GetProperty("ownerKey").GetString()!;
        var fid = created.GetProperty("files")[0].GetProperty("id").GetString()!;
        factory.Storage.DeclaredSizes[factory.Store.Shares[token].Files[0].StorageKey] = 3;
        await Send(HttpMethod.Post, $"/api/shares/{token}/files/{fid}/complete", "{\"parts\":[{\"partNumber\":1,\"eTag\":\"e\"}]}", ownerKey: ownerKey);
        await Send(HttpMethod.Post, $"/api/shares/{token}/finalize", ownerKey: ownerKey);
        return (token, ownerKey);
    }
}
