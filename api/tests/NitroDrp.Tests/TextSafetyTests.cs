using NitroDrp.Api.Services;

namespace NitroDrp.Tests;

public class TextSafetyTests
{
    [Fact]
    public void Single_line_text_loses_control_and_direction_tricks_but_keeps_real_text()
    {
        Assert.Equal("a b", TextSafety.SingleLine("a\r\nb"));
        Assert.Equal("photogpj.exe", TextSafety.SingleLine("photo\u202Egpj.exe")); // the override that reverses what follows
        Assert.Equal("ab", TextSafety.SingleLine("a\u200Bb\u2066\u2069"));
        Assert.Equal("naïve café 日本語 مرحبا", TextSafety.SingleLine("naïve café 日本語 مرحبا"));
        Assert.Equal("", TextSafety.SingleLine(null));
        Assert.Equal("", TextSafety.SingleLine("\u202E\u200B \t"));
    }

    [Fact]
    public void Emoji_made_of_several_parts_survive()
    {
        const string family = "👨\u200D👩\u200D👧"; // joined with zero-width joiners, which are legitimate
        Assert.Equal(family, TextSafety.SingleLine(family));
        Assert.Equal($"hi {family}", TextSafety.Multiline($"hi {family}"));
    }

    [Fact]
    public void Messages_keep_their_line_breaks_in_one_form_and_drop_everything_else_hidden()
    {
        Assert.Equal("one\ntwo\nthree", TextSafety.Multiline("one\r\ntwo\rthree"));
        Assert.Equal("a b", TextSafety.Multiline("a\tb"));
        Assert.Equal("ab", TextSafety.Multiline("a\0b\u0007\u202E"));
        Assert.Equal("hello", TextSafety.Multiline("  hello \n\n"));
        Assert.Equal("", TextSafety.Multiline("\u202E\0"));
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("C:\\Users\\me\\a.txt", "a.txt")]
    [InlineData("a/b/c.txt", "c.txt")]
    [InlineData("report<final>.pdf", "report_final_.pdf")]
    [InlineData("a:b|c?.txt", "a_b_c_.txt")]
    [InlineData("  spaced.txt  ", "spaced.txt")]
    [InlineData("...hidden", "hidden")]
    [InlineData("photo\u202Etxt.exe", "phototxt.exe")]
    [InlineData("zero\u200Bwidth.txt", "zerowidth.txt")]
    [InlineData("a\r\nSet-Cookie: x=1", "a__Set-Cookie_ x=1")]
    public void File_names_are_reduced_to_a_plain_name(string raw, string expected) => Assert.Equal(expected, FileNames.Sanitize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("...")]
    [InlineData("/")]
    [InlineData("\u202E\u200B")]
    public void Names_with_nothing_left_come_back_empty_so_they_are_refused(string raw) => Assert.Equal("", FileNames.Sanitize(raw));

    [Fact]
    public void A_very_long_name_is_shortened_but_keeps_its_extension()
    {
        var name = FileNames.Sanitize(new string('a', 5000) + ".pdf");
        Assert.True(name.Length <= 200);
        Assert.EndsWith(".pdf", name);
    }

    [Theory]
    [InlineData("AbC123xyz789", true)]
    [InlineData("12345678", true)]
    [InlineData("short", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("has space 12345", false)]
    [InlineData("../../../etc/pass", false)]
    [InlineData("AbC123xyz789'; DROP", false)]
    [InlineData("ünïcödé1234567", false)]
    public void Only_tokens_shaped_like_ours_are_accepted(string? s, bool expected) => Assert.Equal(expected, Ids.IsToken(s));

    [Fact]
    public void Tokens_we_generate_always_pass_the_shape_check_and_have_no_oddities()
    {
        for (var i = 0; i < 500; i++) Assert.True(Ids.IsToken(Ids.NewToken()));
        for (var i = 0; i < 500; i++) Assert.True(Ids.IsFileId(Ids.NewFileId()));
        Assert.False(Ids.IsFileId("ABCDEF0123456789")); // upper-case hex isn't what we make
        Assert.True(Ids.IsCredentialId("AbC-_123"));
        Assert.False(Ids.IsCredentialId("a/b"));
        Assert.False(Ids.IsCredentialId(new string('a', 2000)));
    }
}
