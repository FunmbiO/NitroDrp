using NitroDrp.Api.Email;

namespace NitroDrp.Tests;

public class ShareEmailComposerTests
{
    private static readonly DateTimeOffset Expires = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static ShareEmailContext Ctx(
        string sender = "Amina", int files = 3, bool protectedLink = false, string? message = null, string[]? names = null) =>
        new(sender, "Jaded Media", "https://drop.example.com/d/abc123", files, 6_086_000_000, Expires, protectedLink,
            names ?? ["Gallery.zip", "Highlights.mp4", "Proofs.pdf"], message);

    [Fact]
    public void Subject_says_who_and_how_many()
    {
        Assert.Equal("Amina sent you 3 files", ShareEmailComposer.Compose(Ctx()).Subject);
        Assert.Equal("Amina sent you 1 file", ShareEmailComposer.Compose(Ctx(files: 1)).Subject);
    }

    [Fact]
    public void Both_versions_carry_the_link_size_and_expiry()
    {
        var e = ShareEmailComposer.Compose(Ctx());
        foreach (var body in new[] { e.Text, e.Html })
        {
            Assert.Contains("https://drop.example.com/d/abc123", body);
            Assert.Contains("6.1 GB", body);
            Assert.Contains("18 November 2026 (UTC)", body);
        }
    }

    [Fact]
    public void Everything_a_sender_types_is_html_encoded_never_executed()
    {
        var e = ShareEmailComposer.Compose(Ctx(
            sender: "<script>alert(1)</script>", message: "hi <b>there</b>\nsecond line & more", names: ["<img src=x onerror=alert(1)>.zip"]));
        Assert.DoesNotContain("<script", e.Html);
        Assert.DoesNotContain("<img", e.Html);
        Assert.DoesNotContain("<b>there", e.Html);
        Assert.Contains("&lt;script&gt;", e.Html);
        Assert.Contains("hi &lt;b&gt;there&lt;/b&gt;<br>second line &amp; more", e.Html);
    }

    [Fact]
    public void Line_breaks_in_a_name_cannot_inject_headers_through_the_subject()
    {
        var e = ShareEmailComposer.Compose(Ctx(sender: "Evil\r\nBcc: victim@example.com"));
        Assert.DoesNotContain('\r', e.Subject);
        Assert.DoesNotContain('\n', e.Subject);
    }

    [Fact]
    public void A_password_protected_link_hides_file_names_and_says_the_password_comes_separately()
    {
        var e = ShareEmailComposer.Compose(Ctx(protectedLink: true));
        foreach (var body in new[] { e.Text, e.Html })
        {
            Assert.DoesNotContain("Gallery.zip", body);
            Assert.DoesNotContain("Proofs.pdf", body);
            Assert.Contains("separately", body);
        }
    }

    [Fact]
    public void An_open_link_lists_the_files_and_summarises_long_lists()
    {
        var many = Enumerable.Range(1, 14).Select(i => $"photo-{i}.jpg").ToArray();
        var e = ShareEmailComposer.Compose(Ctx(files: 14, names: many));
        Assert.Contains("photo-10.jpg", e.Text);
        Assert.DoesNotContain("photo-11.jpg", e.Text);
        Assert.Contains("and 4 more", e.Text);
        Assert.DoesNotContain("separately", e.Text);
    }

    [Fact]
    public void The_message_shows_only_when_there_is_one()
    {
        Assert.DoesNotContain("border-left", ShareEmailComposer.Compose(Ctx(message: "  ")).Html);
        Assert.Contains("Here you go", ShareEmailComposer.Compose(Ctx(message: "Here you go")).Text);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(86_000_000, "86 MB")]
    [InlineData(1_800_000_000, "1.8 GB")]
    [InlineData(4_200_000_000, "4.2 GB")]
    public void Sizes_match_the_web_app(long bytes, string expected) =>
        Assert.Equal(expected, ShareEmailComposer.FormatBytes(bytes));

    [Fact]
    public void The_footer_names_the_brand_and_tells_people_they_can_ignore_it()
    {
        var e = ShareEmailComposer.Compose(Ctx());
        Assert.Contains("Jaded Media", e.Text);
        Assert.Contains("you can ignore it", e.Text);
    }
}
