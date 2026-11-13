using Microsoft.AspNetCore.WebUtilities;
using NitroDrp.Api.Notifications;

namespace NitroDrp.Tests;

public class PushPolicyTests
{
    private static string Key() { var b = new byte[65]; b[0] = 4; return WebEncoders.Base64UrlEncode(b); }
    private static string Auth() => WebEncoders.Base64UrlEncode(new byte[16]);

    [Theory]
    [InlineData("https://fcm.googleapis.com/fcm/send/abc")]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/abc")]
    [InlineData("https://web.push.apple.com/abc")]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=abc")]
    public void Real_push_services_are_accepted(string endpoint) =>
        Assert.Null(PushEndpointPolicy.Check(endpoint, Key(), Auth()));

    [Theory]
    [InlineData("http://fcm.googleapis.com/x")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://localhost/x")]
    [InlineData("https://127.0.0.1/x")]
    [InlineData("https://evil.example.com/x")]
    [InlineData("https://fcm.googleapis.com.evil.com/x")]
    [InlineData("https://notfcm.googleapis.com.attacker.io/x")]
    [InlineData("https://user:pw@fcm.googleapis.com/x")]
    [InlineData("https://fcm.googleapis.com:8443/x")]
    [InlineData("ftp://fcm.googleapis.com/x")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_refused(string? endpoint) =>
        Assert.NotNull(PushEndpointPolicy.Check(endpoint, Key(), Auth()));

    [Fact]
    public void Keys_must_have_the_right_shape()
    {
        const string ep = "https://fcm.googleapis.com/x";
        Assert.NotNull(PushEndpointPolicy.Check(ep, "", Auth()));
        Assert.NotNull(PushEndpointPolicy.Check(ep, Key(), ""));
        Assert.NotNull(PushEndpointPolicy.Check(ep, WebEncoders.Base64UrlEncode(new byte[65]), Auth())); // not 0x04
        Assert.NotNull(PushEndpointPolicy.Check(ep, WebEncoders.Base64UrlEncode(new byte[10]), Auth()));
        Assert.NotNull(PushEndpointPolicy.Check(ep, Key(), WebEncoders.Base64UrlEncode(new byte[8])));
        Assert.NotNull(PushEndpointPolicy.Check(ep, "***", Auth()));
    }

    [Fact]
    public void Device_id_is_stable_and_well_formed()
    {
        var a = PushEndpointPolicy.IdFor("https://fcm.googleapis.com/x");
        Assert.Equal(a, PushEndpointPolicy.IdFor("https://fcm.googleapis.com/x"));
        Assert.NotEqual(a, PushEndpointPolicy.IdFor("https://fcm.googleapis.com/y"));
        Assert.True(PushEndpointPolicy.IsDeviceId(a));
        Assert.False(PushEndpointPolicy.IsDeviceId("../x"));
        Assert.False(PushEndpointPolicy.IsDeviceId(null));
    }
}
