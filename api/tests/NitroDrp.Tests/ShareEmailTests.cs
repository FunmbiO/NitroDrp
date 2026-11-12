using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NitroDrp.Api;
using NitroDrp.Api.Services;
using NitroDrp.Api.Storage;

namespace NitroDrp.Tests;

public class ShareEmailTests
{
    private const int Part = 5 * 1024 * 1024;
    private readonly InMemoryShareStore _store = new();
    private readonly FakeStorage _storage = new();
    private readonly FakeEmailSender _mail = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly ShareService _svc;

    public ShareEmailTests() => _svc = Service(emailEnabled: true);

    private ShareService Service(bool emailEnabled) => new(
        _store, _storage, new S3DownloadLinkSigner(_storage), new NoEdgeCache(), _mail, ZipLinks.FromApiKey("test-api-key-aaaaaaaa", _clock),
        Options.Create(new NitroDrpOptions
        {
            SenderName = "Jaded Media",
            PublicBaseUrl = "https://drop.example.com/",
            S3 = { PartSizeBytes = Part },
            Email = { Enabled = emailEnabled, FromAddress = "transfers@example.com", MaxRecipientsPerRequest = 3, MaxRecipientsPerShare = 5 },
            Limits = { AllowedExpiryDays = [1, 7], BlockedExtensions = [] },
        }),
        _clock, NullLogger<ShareService>.Instance);

    /// <summary>The whole sender flow: create, upload, finalize.</summary>
    private async Task<CreateShareResponse> Publish(string? password = null, bool finalize = true)
    {
        var created = await _svc.CreateAsync(
            new CreateShareRequest([new("Gallery.zip", 10, null), new("Highlights.mp4", 20, null)], 7, password, null), default);
        if (!finalize) return created;
        foreach (var f in created.Files)
        {
            _storage.DeclaredSizes[_store.Shares[created.Token].Files.Single(x => x.Id == f.Id).StorageKey] = f.Size;
            await _svc.CompleteFileAsync(created.Token, created.OwnerKey, f.Id, new CompleteFileRequest([new(1, "e")]), default);
        }
        await _svc.FinalizeAsync(created.Token, created.OwnerKey, default);
        return created;
    }

    private static async Task<string> CodeOf(Task t)
    {
        try { await t; return "ok"; }
        catch (ServiceException e) { return e.Code; }
    }

    private Task<EmailShareResponse> Send(CreateShareResponse c, params string[] to) =>
        _svc.SendEmailsAsync(c.Token, c.OwnerKey, new EmailShareRequest([.. to], null, null, null), default);

    [Fact]
    public async Task Sends_the_link_to_each_recipient_from_the_brand_with_replies_to_the_sender()
    {
        var c = await Publish();
        var res = await _svc.SendEmailsAsync(c.Token, c.OwnerKey,
            new EmailShareRequest(["a@example.com", "b@example.com"], "Here you go", "Amina", "amina@mail.example"), default);

        Assert.All(res.Results, r => Assert.True(r.Sent));
        Assert.Equal(2, _mail.Sent.Count);
        Assert.Equal(["a@example.com", "b@example.com"], _mail.Sent.Select(m => m.To));
        var m = _mail.Sent[0];
        Assert.Equal("Jaded Media", m.FromName);
        Assert.Equal("transfers@example.com", m.FromAddress);
        Assert.Equal("amina@mail.example", m.ReplyTo);
        Assert.Equal("Amina sent you 2 files", m.Subject);
        Assert.Contains($"https://drop.example.com/d/{c.Token}", m.TextBody); // no double slash from the trailing /
        Assert.Contains("Here you go", m.TextBody);
        Assert.Equal(3, res.Remaining);
    }

    [Fact]
    public async Task Without_a_name_the_brand_is_the_sender()
    {
        var c = await Publish();
        await Send(c, "a@example.com");
        Assert.Equal("Jaded Media sent you 2 files", _mail.Sent.Single().Subject);
    }

    [Fact]
    public async Task Only_the_owner_can_send_and_nothing_goes_out_otherwise()
    {
        var c = await Publish();
        var ex = await Assert.ThrowsAsync<ServiceException>(() =>
            _svc.SendEmailsAsync(c.Token, "wrong", new EmailShareRequest(["a@example.com"], null, null, null), default));
        Assert.Equal(403, ex.Status);
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task A_link_must_be_live_and_unexpired()
    {
        var unfinished = await Publish(finalize: false);
        Assert.Equal("not_ready", await CodeOf(Send(unfinished, "a@example.com")));

        var c = await Publish();
        _clock.Advance(TimeSpan.FromDays(8));
        Assert.Equal("expired", await CodeOf(Send(c, "a@example.com")));
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task Switched_off_means_not_found_and_no_mail()
    {
        var off = Service(emailEnabled: false);
        Assert.Equal("email_disabled", await CodeOf(off.SendEmailsAsync("x", "y", new EmailShareRequest(["a@example.com"], null, null, null), default)));
        Assert.False(off.GetConfig().EmailEnabled);
        Assert.True(_svc.GetConfig().EmailEnabled);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    [InlineData("Bob <b@example.com>")]
    [InlineData("a@example.com,b@example.com")]
    [InlineData("a@example.com\r\nBcc: victim@example.com")]
    [InlineData("a b@example.com")]
    [InlineData("a@example.")]
    public async Task Anything_that_is_not_a_plain_address_is_refused_before_any_mail_or_count(string bad)
    {
        var c = await Publish();
        Assert.Equal("bad_email", await CodeOf(Send(c, "ok@example.com", bad)));
        Assert.Empty(_mail.Sent);
        Assert.Equal(0, _store.Shares[c.Token].EmailsSent);
    }

    [Fact]
    public async Task Needs_at_least_one_recipient_and_no_more_than_the_per_request_limit()
    {
        var c = await Publish();
        Assert.Equal("no_recipients", await CodeOf(Send(c)));
        Assert.Equal("no_recipients", await CodeOf(Send(c, "  ", "")));
        Assert.Equal("too_many_recipients", await CodeOf(Send(c, "a@example.com", "b@example.com", "c@example.com", "d@example.com")));
        Assert.Empty(_mail.Sent);
    }

    [Fact]
    public async Task Duplicates_in_any_case_are_sent_once()
    {
        var c = await Publish();
        var res = await Send(c, "A@Example.com", "a@example.com", " a@example.com ");
        Assert.Single(res.Results);
        Assert.Single(_mail.Sent);
        Assert.Equal(1, _store.Shares[c.Token].EmailsSent);
    }

    [Fact]
    public async Task A_long_message_is_refused()
    {
        var c = await Publish();
        var ex = await Assert.ThrowsAsync<ServiceException>(() =>
            _svc.SendEmailsAsync(c.Token, c.OwnerKey, new EmailShareRequest(["a@example.com"], new string('x', 1001), null, null), default));
        Assert.Equal("message_too_long", ex.Code);
    }

    [Fact]
    public async Task One_link_can_only_ever_email_the_lifetime_cap()
    {
        var c = await Publish();
        await Send(c, "a@example.com", "b@example.com", "c@example.com");                    // 3 of 5
        Assert.Equal("email_limit", await CodeOf(Send(c, "d@example.com", "e@example.com", "f@example.com"))); // would be 6
        Assert.Equal(3, _mail.Sent.Count);                                                  // the refused call sent nothing
        var last = await Send(c, "d@example.com", "e@example.com");                          // 5 of 5 still fits
        Assert.Equal(0, last.Remaining);
        Assert.Equal("email_limit", await CodeOf(Send(c, "g@example.com")));
    }

    [Fact]
    public async Task A_refused_address_is_reported_and_does_not_stop_the_others()
    {
        _mail.Refuses.Add("unverified@example.com");
        var c = await Publish();
        var res = await Send(c, "good@example.com", "unverified@example.com", "also-good@example.com");

        Assert.Equal([true, false, true], res.Results.Select(r => r.Sent));
        Assert.NotNull(res.Results[1].Error);
        Assert.Equal(["good@example.com", "also-good@example.com"], _mail.Sent.Select(m => m.To));
    }

    [Fact]
    public async Task The_password_is_never_in_the_email_and_file_names_stay_private()
    {
        var c = await Publish(password: "hunter2-secret");
        await Send(c, "a@example.com");
        var m = _mail.Sent.Single();
        foreach (var body in new[] { m.TextBody, m.HtmlBody, m.Subject })
        {
            Assert.DoesNotContain("hunter2", body);
            Assert.DoesNotContain("Gallery.zip", body);
        }
        Assert.Contains("separately", m.TextBody);
    }

    [Fact]
    public async Task A_bad_reply_to_address_is_refused_and_a_name_is_cleaned()
    {
        var c = await Publish();
        var ex = await Assert.ThrowsAsync<ServiceException>(() =>
            _svc.SendEmailsAsync(c.Token, c.OwnerKey, new EmailShareRequest(["a@example.com"], null, "Amina", "nope"), default));
        Assert.Equal("bad_email", ex.Code);

        await _svc.SendEmailsAsync(c.Token, c.OwnerKey, new EmailShareRequest(["a@example.com"], null, "Amina\r\nBcc: x@y.z", null), default);
        Assert.DoesNotContain('\n', _mail.Sent.Single().Subject);
    }
}
