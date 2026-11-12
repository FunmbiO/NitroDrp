using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using NitroDrp.Api.Email;

namespace NitroDrp.Tests;

public class EmailSetupTests
{
    [Theory]
    [InlineData("Jaded Media", "\"Jaded Media\" <transfers@jadedmedia.ca>")]
    [InlineData("O\"Brien \\ Co", "\"O\\\"Brien \\\\ Co\" <transfers@jadedmedia.ca>")]
    [InlineData("", "transfers@jadedmedia.ca")]
    [InlineData("   ", "transfers@jadedmedia.ca")]
    public void Display_names_are_quoted_and_escaped(string name, string expected) =>
        Assert.Equal(expected, EmailAddressFormat.Display(name, "transfers@jadedmedia.ca"));

    [Fact]
    public void Non_ascii_names_become_an_encoded_word_so_they_survive()
    {
        var v = EmailAddressFormat.Display("Réception Dupont", "a@b.ca");
        Assert.StartsWith("=?UTF-8?B?", v);
        Assert.EndsWith("?= <a@b.ca>", v);
        var b64 = v["=?UTF-8?B?".Length..v.IndexOf("?=", StringComparison.Ordinal)];
        Assert.Equal("Réception Dupont", System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
    }

    [Fact]
    public void A_name_cannot_smuggle_in_extra_headers()
    {
        var v = EmailAddressFormat.Display("Evil\r\nBcc: victim@example.com", "a@b.ca");
        Assert.DoesNotContain('\r', v);
        Assert.DoesNotContain('\n', v);
    }

    private static WebApplicationFactory<Program> Factory(Dictionary<string, string?> settings) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("NitroDrp:ApiKey", "test-api-key-1234567890");
            b.UseSetting("NitroDrp:S3:Bucket", "b");
            foreach (var (k, v) in settings) b.UseSetting(k, v);
        });

    [Fact]
    public void Starting_with_email_on_but_no_sender_address_fails_with_a_clear_message()
    {
        using var f = Factory(new() { ["NitroDrp:Email:Enabled"] = "true", ["NitroDrp:PublicBaseUrl"] = "https://drop.example.com" });
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("FromAddress", ex.ToString());
    }

    [Fact]
    public void Starting_with_email_on_but_no_public_url_fails_with_a_clear_message()
    {
        using var f = Factory(new() { ["NitroDrp:Email:Enabled"] = "true", ["NitroDrp:Email:FromAddress"] = "transfers@example.com" });
        var ex = Assert.ThrowsAny<Exception>(() => f.CreateClient());
        Assert.Contains("PublicBaseUrl", ex.ToString());
    }
}
