using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NitroDrp.Api;
using NitroDrp.Api.Services;
using NitroDrp.Api.Storage;

namespace NitroDrp.Tests;

/// <summary>The owner's list of live links, and signed-in access to them.</summary>
public class ShareListTests
{
    private readonly InMemoryShareStore _store = new();
    private readonly FakeStorage _storage = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
    private readonly ShareService _svc;

    public ShareListTests() => _svc = new(
        _store, _storage, new S3DownloadLinkSigner(_storage), new NoEdgeCache(), new FakeEmailSender(), ZipLinks.FromApiKey("test-api-key-aaaaaaaa", _clock),
        Options.Create(new NitroDrpOptions { S3 = { PartSizeBytes = 5 * 1024 * 1024 }, Limits = { AllowedExpiryDays = [1, 7], BlockedExtensions = [] } }),
        _clock, NullLogger<ShareService>.Instance);

    private async Task<CreateShareResponse> Make(string name, int days = 7, string? password = null, bool finalize = true)
    {
        var c = await _svc.CreateAsync(new CreateShareRequest([new(name, 10, null), new("b.txt", 5, null)], days, password, null), default);
        if (!finalize) return c;
        foreach (var f in c.Files)
        {
            _storage.DeclaredSizes[_store.Shares[c.Token].Files.Single(x => x.Id == f.Id).StorageKey] = f.Size;
            await _svc.CompleteFileAsync(c.Token, c.OwnerKey, f.Id, new CompleteFileRequest([new(1, "e")]), default);
        }
        await _svc.FinalizeAsync(c.Token, c.OwnerKey, default);
        return c;
    }

    [Fact]
    public async Task Only_finished_links_are_listed_newest_first_with_their_details()
    {
        var first = await Make("a.zip", password: "pw");
        _clock.Advance(TimeSpan.FromMinutes(5));
        var second = await Make("c.zip");
        await Make("draft.zip", finalize: false);

        var list = await _svc.ListMineAsync(default);
        Assert.Equal([second.Token, first.Token], list.Select(s => s.Token));
        Assert.Equal(2, list[1].FileCount);
        Assert.Equal(15, list[1].TotalSize);
        Assert.True(list[1].PasswordProtected);
        Assert.False(list[0].PasswordProtected);
    }

    [Fact]
    public async Task Finalizing_twice_does_not_list_a_link_twice()
    {
        var c = await Make("a.zip");
        await _svc.FinalizeAsync(c.Token, c.OwnerKey, default);
        Assert.Single(await _svc.ListMineAsync(default));
    }

    [Fact]
    public async Task Expired_links_drop_off_the_list()
    {
        await Make("short.zip", days: 1);
        var long7 = await Make("long.zip", days: 7);
        _clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal([long7.Token], (await _svc.ListMineAsync(default)).Select(s => s.Token));
    }

    [Fact]
    public async Task Deleting_a_link_removes_it_from_the_list()
    {
        var c = await Make("a.zip");
        await _svc.DeleteAsync(c.Token, c.OwnerKey, default);
        Assert.Empty(await _svc.ListMineAsync(default));
    }

    [Fact]
    public async Task The_signed_in_owner_can_act_without_the_owner_key_but_a_stranger_cannot()
    {
        var c = await Make("a.zip");
        await _svc.DeleteAsync(c.Token, OwnerAuth.SignedIn, default); // no key needed

        var d = await Make("b.zip");
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.DeleteAsync(d.Token, "wrong-key", default));
        Assert.Equal(403, ex.Status);
        var none = await Assert.ThrowsAsync<ServiceException>(() => _svc.DeleteAsync(d.Token, (string?)null, default));
        Assert.Equal(403, none.Status);
    }
}
