using Microsoft.Extensions.Options;
using NitroDrp.Api;
using NitroDrp.Api.Domain;
using NitroDrp.Api.Services;
using NitroDrp.Api.Storage;

namespace NitroDrp.Tests;

public class ShareServiceTests
{
    private const int Part = 5 * 1024 * 1024;
    private readonly InMemoryShareStore _store = new();
    private readonly FakeStorage _storage = new();
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 11, 12, 0, 0, TimeSpan.Zero));
    private readonly ShareService _svc;

    public ShareServiceTests()
    {
        var o = new NitroDrpOptions
        {
            SenderName = "Jaded Media",
            S3 = { PartSizeBytes = Part },
            Limits = { AllowedExpiryDays = [1, 7, 30], BlockedExtensions = ["exe"], MaxFiles = 3, MaxFileBytes = 1L << 30, MaxTotalBytes = 2L << 30 },
        };
        _svc = new ShareService(_store, _storage, new S3DownloadLinkSigner(_storage), Options.Create(o), _clock);
    }

    private static CreateShareRequest Req(string? password = null, int? limit = null, int days = 7, params (string, long)[] files) =>
        new(files.Length > 0 ? files.Select(f => new NewFile(f.Item1, f.Item2, null)).ToList() : [new("a.zip", Part * 2 + 1, null)],
            days, password, limit);

    /// <summary>Runs the whole sender flow and returns the active share.</summary>
    private async Task<(CreateShareResponse created, Share share)> Publish(CreateShareRequest req)
    {
        var created = await _svc.CreateAsync(req, default);
        foreach (var f in created.Files)
        {
            var parts = Enumerable.Range(1, f.PartCount).Select(n => new UploadedPart(n, $"etag{n}")).ToList();
            _storage.DeclaredSizes[_store.Shares[created.Token].Files.Single(x => x.Id == f.Id).StorageKey] = f.Size;
            await _svc.CompleteFileAsync(created.Token, created.OwnerKey, f.Id, new CompleteFileRequest(parts), default);
        }
        await _svc.FinalizeAsync(created.Token, created.OwnerKey, default);
        return (created, _store.Shares[created.Token]);
    }

    [Fact]
    public async Task Create_returns_part_plan_and_expiry()
    {
        var created = await _svc.CreateAsync(Req(), default);
        Assert.Equal(3, created.Files[0].PartCount);
        Assert.Equal(_clock.GetUtcNow().AddDays(7), created.ExpiresAt);
        Assert.StartsWith("d7/", _store.Shares[created.Token].Files[0].StorageKey); // prefix drives the S3 lifecycle rule
        Assert.Equal("Jaded Media", _store.Shares[created.Token].SenderName);
    }

    [Fact]
    public async Task Owner_key_is_stored_hashed()
    {
        var created = await _svc.CreateAsync(Req(), default);
        Assert.DoesNotContain(created.OwnerKey, _store.Shares[created.Token].OwnerKeyHash);
    }

    [Theory]
    [InlineData(3, "bad_expiry")]
    [InlineData(0, "bad_expiry")]
    public async Task Create_rejects_unlisted_expiry(int days, string code)
    {
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.CreateAsync(Req(days: days), default));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public async Task Create_rejects_blocked_extension_and_bad_sizes_and_too_many_files()
    {
        Assert.Equal("blocked_type", (await Assert.ThrowsAsync<ServiceException>(() => _svc.CreateAsync(Req(files: ("virus.EXE", 10)), default))).Code);
        Assert.Equal("bad_size", (await Assert.ThrowsAsync<ServiceException>(() => _svc.CreateAsync(Req(files: ("a.zip", 0)), default))).Code);
        Assert.Equal("bad_size", (await Assert.ThrowsAsync<ServiceException>(() => _svc.CreateAsync(Req(files: ("a.zip", 2L << 30)), default))).Code);
        Assert.Equal("too_many_files", (await Assert.ThrowsAsync<ServiceException>(() =>
            _svc.CreateAsync(Req(files: [("1", 1), ("2", 1), ("3", 1), ("4", 1)]), default))).Code);
    }

    [Fact]
    public async Task Only_the_owner_can_presign()
    {
        var created = await _svc.CreateAsync(Req(), default);
        var fileId = created.Files[0].Id;
        var body = new PresignRequest([1]);

        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.PresignAsync(created.Token, "nope", fileId, body, default));
        Assert.Equal(403, ex.Status);
        var ok = await _svc.PresignAsync(created.Token, created.OwnerKey, fileId, body, default);
        Assert.Single(ok.Urls);
    }

    [Fact]
    public async Task Presign_rejects_out_of_range_parts()
    {
        var created = await _svc.CreateAsync(Req(), default);
        var ex = await Assert.ThrowsAsync<ServiceException>(() =>
            _svc.PresignAsync(created.Token, created.OwnerKey, created.Files[0].Id, new PresignRequest([4]), default));
        Assert.Equal("bad_parts", ex.Code);
    }

    [Fact]
    public async Task Complete_requires_every_part()
    {
        var created = await _svc.CreateAsync(Req(), default);
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.CompleteFileAsync(
            created.Token, created.OwnerKey, created.Files[0].Id,
            new CompleteFileRequest([new(1, "a"), new(2, "b")]), default));
        Assert.Equal("bad_parts", ex.Code);
    }

    [Fact]
    public async Task Finalize_fails_until_all_files_are_uploaded_and_sizes_match()
    {
        var created = await _svc.CreateAsync(Req(), default);
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.FinalizeAsync(created.Token, created.OwnerKey, default));
        Assert.Equal("incomplete", ex.Code);

        // upload "completes" but the stored object has the wrong size
        var f = created.Files[0];
        var parts = Enumerable.Range(1, f.PartCount).Select(n => new UploadedPart(n, "e")).ToList();
        _storage.DeclaredSizes[_store.Shares[created.Token].Files[0].StorageKey] = f.Size - 1;
        await _svc.CompleteFileAsync(created.Token, created.OwnerKey, f.Id, new CompleteFileRequest(parts), default);
        ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.FinalizeAsync(created.Token, created.OwnerKey, default));
        Assert.Equal("size_mismatch", ex.Code);
    }

    [Fact]
    public async Task Pending_shares_are_invisible_to_recipients()
    {
        var created = await _svc.CreateAsync(Req(), default);
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.GetViewAsync(created.Token, default));
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task Unknown_token_is_404()
    {
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.GetViewAsync("doesnotexist", default));
        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task Download_returns_links_and_counts_down()
    {
        var (created, _) = await Publish(Req(limit: 2, files: [("a.zip", 10), ("b.mp4", 20)]));
        var view = await _svc.GetViewAsync(created.Token, default);
        Assert.Equal(2, view.DownloadsRemaining);
        Assert.Equal(30, view.TotalSize);
        Assert.Equal(2, view.Files!.Count);

        var dl = await _svc.DownloadAsync(created.Token, new DownloadRequest(null, null), default);
        Assert.Equal(2, dl.Files.Count);
        Assert.Equal(1, (await _svc.GetViewAsync(created.Token, default)).DownloadsRemaining);
    }

    [Fact]
    public async Task Download_limit_is_enforced()
    {
        var (created, _) = await Publish(Req(limit: 1, files: ("a.zip", 10)));
        await _svc.DownloadAsync(created.Token, new DownloadRequest(null, null), default);
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.DownloadAsync(created.Token, new DownloadRequest(null, null), default));
        Assert.Equal("limit_reached", ex.Code);
        Assert.Equal(410, ex.Status);
    }

    [Fact]
    public async Task Concurrent_downloads_never_exceed_the_limit()
    {
        var (created, _) = await Publish(Req(limit: 3, files: ("a.zip", 10)));
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            try { await _svc.DownloadAsync(created.Token, new DownloadRequest(null, null), default); return true; }
            catch (ServiceException) { return false; }
        }));
        Assert.Equal(3, results.Count(r => r));
    }

    [Fact]
    public async Task Expired_links_are_gone()
    {
        var (created, _) = await Publish(Req(days: 1, files: ("a.zip", 10)));
        _clock.Advance(TimeSpan.FromDays(1));
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.DownloadAsync(created.Token, new DownloadRequest(null, null), default));
        Assert.Equal("expired", ex.Code);
    }

    [Fact]
    public async Task Password_hides_files_until_unlocked_and_gates_download()
    {
        var (created, _) = await Publish(Req(password: "hunter2", files: ("secret.pdf", 10)));

        var view = await _svc.GetViewAsync(created.Token, default);
        Assert.True(view.PasswordRequired);
        Assert.Null(view.Files);

        Assert.Equal("password_required", (await Assert.ThrowsAsync<ServiceException>(() =>
            _svc.DownloadAsync(created.Token, new DownloadRequest(null, null), default))).Code);
        Assert.Equal("wrong_password", (await Assert.ThrowsAsync<ServiceException>(() =>
            _svc.UnlockAsync(created.Token, "nope", default))).Code);

        var unlocked = await _svc.UnlockAsync(created.Token, "hunter2", default);
        Assert.Single(unlocked.Files!);
        var dl = await _svc.DownloadAsync(created.Token, new DownloadRequest("hunter2", null), default);
        Assert.Single(dl.Files);
    }

    [Fact]
    public async Task Failed_password_attempts_do_not_use_up_downloads()
    {
        var (created, share) = await Publish(Req(password: "pw", limit: 1, files: ("a.zip", 10)));
        await Assert.ThrowsAsync<ServiceException>(() => _svc.DownloadAsync(created.Token, new DownloadRequest("bad", null), default));
        Assert.Equal(0, share.DownloadCount);
    }

    [Fact]
    public async Task Delete_removes_objects_and_metadata()
    {
        var (created, share) = await Publish(Req(files: ("a.zip", 10)));
        Assert.Single(_storage.Objects);
        await _svc.DeleteAsync(created.Token, created.OwnerKey, default);
        Assert.Empty(_storage.Objects);
        Assert.False(_store.Shares.ContainsKey(created.Token));
    }

    [Fact]
    public async Task Delete_aborts_unfinished_uploads()
    {
        var created = await _svc.CreateAsync(Req(), default);
        Assert.Single(_storage.OpenUploads);
        await _svc.DeleteAsync(created.Token, created.OwnerKey, default);
        Assert.Empty(_storage.OpenUploads);
    }

    [Fact]
    public async Task Delete_needs_the_owner_key()
    {
        var (created, _) = await Publish(Req(files: ("a.zip", 10)));
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.DeleteAsync(created.Token, null, default));
        Assert.Equal(403, ex.Status);
        Assert.True(_store.Shares.ContainsKey(created.Token));
    }

    [Fact]
    public async Task Upload_status_reports_parts_already_in_storage()
    {
        var created = await _svc.CreateAsync(Req(), default); // 3 parts
        var key = _store.Shares[created.Token].Files[0].StorageKey;
        _storage.Parts[key] = [new(1, "\"e1\"", Part), new(2, "\"e2\"", Part)];

        var status = await _svc.GetUploadStatusAsync(created.Token, created.OwnerKey, default);
        var file = Assert.Single(status.Files);
        Assert.False(file.Complete);
        Assert.False(status.Finalized);
        Assert.Equal([1, 2], file.Parts.Select(p => p.PartNumber));
        Assert.Equal(Part, status.PartSize);
    }

    [Fact]
    public async Task Resumed_upload_can_finish_using_listed_parts_plus_new_ones()
    {
        var created = await _svc.CreateAsync(Req(), default);
        var f = created.Files[0];
        var key = _store.Shares[created.Token].Files[0].StorageKey;
        _storage.Parts[key] = [new(1, "e1", Part), new(2, "e2", Part)];

        var listed = (await _svc.GetUploadStatusAsync(created.Token, created.OwnerKey, default)).Files[0].Parts;
        var all = listed.Append(new UploadedPart(3, "e3")).ToList();
        _storage.DeclaredSizes[key] = f.Size;

        await _svc.CompleteFileAsync(created.Token, created.OwnerKey, f.Id, new CompleteFileRequest(all), default);
        await _svc.FinalizeAsync(created.Token, created.OwnerKey, default);
        Assert.Equal(ShareStatus.Active, _store.Shares[created.Token].Status);
    }

    [Fact]
    public async Task Upload_status_marks_finished_files_and_finalized_shares()
    {
        var (created, _) = await Publish(Req(files: ("a.zip", 10)));
        var status = await _svc.GetUploadStatusAsync(created.Token, created.OwnerKey, default);
        Assert.True(status.Finalized);
        Assert.True(status.Files[0].Complete);
    }

    [Fact]
    public async Task Upload_status_is_gone_when_the_multipart_upload_expired()
    {
        var created = await _svc.CreateAsync(Req(), default);
        _storage.OpenUploads.Clear(); // S3 aborted it (lifecycle rule)
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.GetUploadStatusAsync(created.Token, created.OwnerKey, default));
        Assert.Equal("upload_expired", ex.Code);
        Assert.Equal(410, ex.Status);
    }

    [Fact]
    public async Task Upload_status_needs_the_owner_key()
    {
        var created = await _svc.CreateAsync(Req(), default);
        var ex = await Assert.ThrowsAsync<ServiceException>(() => _svc.GetUploadStatusAsync(created.Token, "nope", default));
        Assert.Equal(403, ex.Status);
    }
}
