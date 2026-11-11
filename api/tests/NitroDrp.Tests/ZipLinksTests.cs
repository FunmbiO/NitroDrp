using System.Web;
using NitroDrp.Api.Services;

namespace NitroDrp.Tests;

public class ZipLinksTests
{
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private ZipLinks Links(string key = "api-key-aaaaaaaaaaaa") => ZipLinks.FromApiKey(key, _clock);

    private static (long exp, string sig) Parse(string url)
    {
        var q = HttpUtility.ParseQueryString(new Uri("http://x" + url).Query);
        return (long.Parse(q["exp"]!), q["sig"]!);
    }

    [Fact]
    public void A_fresh_link_verifies_for_its_own_share_only()
    {
        var (exp, sig) = Parse(Links().Create("tok1", TimeSpan.FromMinutes(15)));
        Assert.True(Links().IsValid("tok1", exp, sig));
        Assert.False(Links().IsValid("tok2", exp, sig));
    }

    [Fact]
    public void Links_expire_and_cannot_be_extended()
    {
        var (exp, sig) = Parse(Links().Create("tok1", TimeSpan.FromMinutes(15)));
        Assert.False(Links().IsValid("tok1", exp + 3600, sig)); // edited expiry
        _clock.Advance(TimeSpan.FromMinutes(16));
        Assert.False(Links().IsValid("tok1", exp, sig));
    }

    [Fact]
    public void A_different_server_key_rejects_the_link_and_junk_is_rejected()
    {
        var (exp, sig) = Parse(Links().Create("tok1", TimeSpan.FromMinutes(15)));
        Assert.False(Links("some-other-key-bbbbbbbb").IsValid("tok1", exp, sig));
        Assert.False(Links().IsValid("tok1", exp, null));
        Assert.False(Links().IsValid("tok1", exp, "nope"));
    }
}
