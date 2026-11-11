using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NitroDrp.Api.Storage;

namespace NitroDrp.Tests;

/// <summary>The real ASP.NET pipeline (routing, rate limiting, error handler, streaming) over in-memory storage.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "test-api-key-1234567890";
    public InMemoryShareStore Store { get; } = new();
    public FakeStorage Storage { get; } = new();
    public RecordingEdgeCache Edge { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("NitroDrp:ApiKey", ApiKey);
        builder.UseSetting("NitroDrp:S3:Bucket", "test-bucket");
        builder.UseSetting("NitroDrp:SenderName", "Jaded Media");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IShareStore>();
            services.RemoveAll<IFileStorage>();
            services.RemoveAll<IDownloadLinkSigner>();
            services.RemoveAll<IObjectReader>();
            services.RemoveAll<IEdgeCache>();
            services.AddSingleton<IEdgeCache>(Edge);
            services.AddSingleton<IShareStore>(Store);
            services.AddSingleton<IFileStorage>(Storage);
            services.AddSingleton<IDownloadLinkSigner>(new S3DownloadLinkSigner(Storage));
            services.AddSingleton<IObjectReader>(new S3ObjectReader(Storage));
        });
    }
}

public class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _http = factory.CreateClient();

    private record Created(string Token, string OwnerKey, List<CreatedFile> Files);
    private record CreatedFile(string Id, string Name, long Size, int PartCount);

    /// <summary>Create, upload (one part per file), finalize. Returns the token.</summary>
    private async Task<string> PublishAsync(int? limit = null, string? password = null, params (string name, string content)[] files)
    {
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/shares")
        {
            Content = JsonContent.Create(new
            {
                files = files.Select(f => new { name = f.name, size = f.content.Length }),
                expiresInDays = 7,
                downloadLimit = limit,
                password,
            }),
        };
        create.Headers.Add("X-Api-Key", ApiFactory.ApiKey);
        var res = await _http.SendAsync(create);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var created = (await res.Content.ReadFromJsonAsync<Created>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;

        foreach (var (f, (_, content)) in created.Files.Zip(files))
        {
            var key = factory.Store.Shares[created.Token].Files.Single(x => x.Id == f.Id).StorageKey;
            factory.Storage.Content[key] = System.Text.Encoding.UTF8.GetBytes(content);
            factory.Storage.DeclaredSizes[key] = content.Length;
            Assert.Equal(HttpStatusCode.NoContent, (await Owner(created, HttpMethod.Post, $"files/{f.Id}/complete",
                new { parts = new[] { new { partNumber = 1, eTag = "e" } } })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NoContent, (await Owner(created, HttpMethod.Post, "finalize", null)).StatusCode);
        return created.Token;
    }

    private Task<HttpResponseMessage> Owner(Created c, HttpMethod method, string path, object? body)
    {
        var req = new HttpRequestMessage(method, $"/api/shares/{c.Token}/{path}") { Content = body is null ? null : JsonContent.Create(body) };
        req.Headers.Add("X-Owner-Key", c.OwnerKey);
        return _http.SendAsync(req);
    }

    private async Task<string> ZipUrlAsync(string token, string? password = null)
    {
        var res = await _http.PostAsJsonAsync($"/api/shares/{token}/zip-link", new { password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("url").GetString()!;
    }

    [Fact]
    public async Task Health_and_wrong_api_key()
    {
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/health")).StatusCode);
        var res = await _http.PostAsJsonAsync("/api/shares", new { files = new[] { new { name = "a", size = 1 } }, expiresInDays = 7 });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("bad_api_key", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Zip_streams_every_file_with_its_content_and_a_download_filename()
    {
        var token = await PublishAsync(files: [("a.txt", "alpha"), ("b.txt", "bravo!"), ("a.txt", "dup")]);
        var res = await _http.GetAsync(await ZipUrlAsync(token));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("application/zip", res.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment; filename*=UTF-8''Jaded%20Media%20files.zip", string.Join("", res.Content.Headers.GetValues("Content-Disposition")));

        using var zip = new ZipArchive(await res.Content.ReadAsStreamAsync(), ZipArchiveMode.Read);
        Assert.Equal(["a.txt", "b.txt", "a (2).txt"], zip.Entries.Select(e => e.FullName));
        using var r = new StreamReader(zip.GetEntry("b.txt")!.Open());
        Assert.Equal("bravo!", await r.ReadToEndAsync());
    }

    [Fact]
    public async Task Zip_counts_as_one_download_and_the_issued_link_still_works_after_the_limit_is_hit()
    {
        var token = await PublishAsync(limit: 1, files: [("a.txt", "alpha")]);
        var url = await ZipUrlAsync(token);

        // limit now reached: a second link is refused, but the one already issued downloads fine
        var second = await _http.PostAsJsonAsync($"/api/shares/{token}/zip-link", new { password = (string?)null });
        Assert.Equal(HttpStatusCode.Gone, second.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Zip_needs_the_password_and_a_valid_signature()
    {
        var token = await PublishAsync(password: "hunter2", files: [("s.txt", "secret")]);

        var noPw = await _http.PostAsJsonAsync($"/api/shares/{token}/zip-link", new { password = (string?)null });
        Assert.Equal(HttpStatusCode.Unauthorized, noPw.StatusCode);

        var url = await ZipUrlAsync(token, "hunter2");
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync(url)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync(url[..^2] + "xx")).StatusCode);          // tampered signature
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync($"/api/shares/{token}/zip")).StatusCode); // no signature at all
    }

    [Fact]
    public async Task A_zip_link_for_one_share_does_not_work_for_another()
    {
        var a = await PublishAsync(files: [("a.txt", "a")]);
        var b = await PublishAsync(files: [("b.txt", "b")]);
        var urlForA = await ZipUrlAsync(a);
        Assert.Equal(HttpStatusCode.Forbidden, (await _http.GetAsync(urlForA.Replace(a, b))).StatusCode);
    }

    [Fact]
    public async Task Deleting_over_http_removes_files_metadata_and_purges_the_edge()
    {
        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/shares")
        {
            Content = JsonContent.Create(new { files = new[] { new { name = "z.txt", size = 3 } }, expiresInDays = 1 }),
        };
        create.Headers.Add("X-Api-Key", ApiFactory.ApiKey);
        var created = (await (await _http.SendAsync(create)).Content.ReadFromJsonAsync<Created>(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!;
        var key = factory.Store.Shares[created.Token].Files[0].StorageKey;
        factory.Storage.DeclaredSizes[key] = 3;
        await Owner(created, HttpMethod.Post, $"files/{created.Files[0].Id}/complete", new { parts = new[] { new { partNumber = 1, eTag = "e" } } });
        await Owner(created, HttpMethod.Post, "finalize", null);

        Assert.Equal(HttpStatusCode.Forbidden, (await _http.DeleteAsync($"/api/shares/{created.Token}")).StatusCode); // no owner key
        Assert.True(factory.Storage.Objects.ContainsKey(key));

        Assert.Equal(HttpStatusCode.NoContent, (await Owner(created, HttpMethod.Delete, "", null)).StatusCode);
        Assert.False(factory.Storage.Objects.ContainsKey(key));
        Assert.Contains($"/d1/{created.Token}/*", factory.Edge.Paths);
        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"/api/shares/{created.Token}")).StatusCode);
    }
}
