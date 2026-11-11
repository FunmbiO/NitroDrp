using NitroDrp.Api.Services;

namespace NitroDrp.Tests;

public class SecurityTests
{
    [Fact]
    public void Tokens_are_12_base62_chars_and_unique()
    {
        var tokens = Enumerable.Range(0, 5000).Select(_ => Ids.NewToken()).ToList();
        Assert.All(tokens, t => { Assert.Equal(12, t.Length); Assert.Matches("^[0-9A-Za-z]+$", t); });
        Assert.Equal(tokens.Count, tokens.Distinct().Count());
    }

    [Fact]
    public void Password_hash_verifies_only_the_right_password()
    {
        var hash = PasswordHasher.Hash("correct horse", iterations: 1_000);
        Assert.True(PasswordHasher.Verify("correct horse", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
        Assert.False(PasswordHasher.Verify("correct horse", "garbage"));
    }

    [Fact]
    public void Password_hashes_are_salted()
    {
        Assert.NotEqual(PasswordHasher.Hash("same", 1_000), PasswordHasher.Hash("same", 1_000));
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\Users\me\wedding.zip", "wedding.zip")]
    [InlineData("a<b>c?.jpg", "a_b_c_.jpg")]
    [InlineData("   ", "")]
    public void File_names_are_sanitized(string input, string expected) =>
        Assert.Equal(expected, FileNames.Sanitize(input));

    [Fact]
    public void Long_file_names_keep_their_extension()
    {
        var name = FileNames.Sanitize(new string('a', 400) + ".mp4");
        Assert.Equal(200, name.Length);
        Assert.EndsWith(".mp4", name);
    }
}
