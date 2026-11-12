using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace NitroDrp.Tests;

/// <summary>A server that never configured email: the page is told so, and the endpoint refuses.</summary>
public class EmailDisabledApiTests
{
    private static HttpClient Client() => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseSetting("NitroDrp:ApiKey", "test-api-key-1234567890");
        b.UseSetting("NitroDrp:S3:Bucket", "b");
    }).CreateClient();

    [Fact]
    public async Task Config_says_email_is_off_and_sending_is_refused()
    {
        var http = Client();
        var cfg = await http.GetFromJsonAsync<JsonElement>("/api/config");
        Assert.False(cfg.GetProperty("emailEnabled").GetBoolean());

        var res = await http.PostAsJsonAsync("/api/shares/anything/email", new { recipients = new[] { "a@example.com" } });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("email_disabled", (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
