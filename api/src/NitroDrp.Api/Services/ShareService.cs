using Microsoft.Extensions.Options;
using System.Net.Mail;
using NitroDrp.Api.Domain;
using NitroDrp.Api.Email;
using NitroDrp.Api.Storage;

namespace NitroDrp.Api.Services;

/// <summary>All business rules live here so they can be tested without HTTP, S3 or DynamoDB.</summary>
public sealed class ShareService(
    IShareStore store, IFileStorage storage, IDownloadLinkSigner links, IEdgeCache edge, IEmailSender email, ZipLinks zipLinks, IOptions<NitroDrpOptions> options, TimeProvider clock, ILogger<ShareService> log)
{
    private const int MaxS3Parts = 10_000;
    private const int MaxPartsPerPresign = 100;
    private const int MaxDownloadLimit = 10_000;
    private readonly NitroDrpOptions _o = options.Value;

    public async Task<CreateShareResponse> CreateAsync(CreateShareRequest req, CancellationToken ct)
    {
        var lim = _o.Limits;
        if (req.Files is null || req.Files.Count == 0)
            throw ServiceException.BadRequest("no_files", "Add at least one file.");
        if (req.Files.Count > lim.MaxFiles)
            throw ServiceException.BadRequest("too_many_files", $"A link can hold at most {lim.MaxFiles} files.");
        if (!lim.AllowedExpiryDays.Contains(req.ExpiresInDays))
            throw ServiceException.BadRequest("bad_expiry", "Choose one of the allowed expiry times.");
        var downloadLimit = req.DownloadLimit ?? 0; // no limit given means unlimited (stored as 0)
        if (req.DownloadLimit is { } asked && asked is < 1 or > MaxDownloadLimit)
            throw ServiceException.BadRequest("bad_download_limit", $"Download limit must be between 1 and {MaxDownloadLimit}.");
        if (req.Password is { Length: > 256 })
            throw ServiceException.BadRequest("bad_password", "Password is too long.");

        var partSize = _o.S3.PartSizeBytes;
        var token = Ids.NewToken();
        var now = clock.GetUtcNow();
        var files = new List<ShareFile>();
        long total = 0;

        foreach (var (f, i) in req.Files.Select((f, i) => (f, i)))
        {
            if (f is null) throw ServiceException.BadRequest("bad_filename", "A file has no usable name.");
            var name = FileNames.Sanitize(f.Name);
            if (name.Length == 0)
                throw ServiceException.BadRequest("bad_filename", "A file has no usable name.");
            var ext = Path.GetExtension(name).TrimStart('.');
            if (lim.BlockedExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
                throw ServiceException.BadRequest("blocked_type", $"'.{ext}' files can't be sent.");
            if (f.Size <= 0 || f.Size > lim.MaxFileBytes || PartCount(f.Size, partSize) > MaxS3Parts)
                throw ServiceException.BadRequest("bad_size", $"'{name}' is empty or larger than the {lim.MaxFileBytes / (1024 * 1024 * 1024)} GB limit.");
            total += f.Size;

            var id = Ids.NewFileId();
            files.Add(new ShareFile
            {
                Id = id,
                Order = i,
                Name = name,
                Size = f.Size,
                ContentType = NormalizeContentType(f.ContentType),
                StorageKey = $"d{req.ExpiresInDays}/{token}/{id}",
            });
        }
        if (total > lim.MaxTotalBytes)
            throw ServiceException.BadRequest("too_large", "That's more than one link can carry.");

        var ownerKey = Ids.NewOwnerKey();
        var share = new Share
        {
            Token = token,
            SenderName = _o.SenderName,
            CreatedAt = now,
            ExpiresAt = now.AddDays(req.ExpiresInDays),
            PasswordHash = string.IsNullOrEmpty(req.Password) ? null : PasswordHasher.Hash(req.Password),
            DownloadLimit = downloadLimit,
            OwnerKeyHash = Secrets.Sha256(ownerKey),
            Files = files,
        };

        foreach (var f in files)
            f.UploadId = await storage.BeginMultipartAsync(f.StorageKey, f.ContentType, ct);
        await store.CreateAsync(share, ct);

        return new CreateShareResponse(
            token, ownerKey, share.ExpiresAt, partSize,
            files.Select(f => new CreatedFile(f.Id, f.Name, f.Size, PartCount(f.Size, partSize))).ToList());
    }

    /// <summary>
    /// What the uploader needs to resume: per file, whether it's complete and which parts S3 already holds.
    /// Parts come from S3 itself (ListParts), so a crashed tab or a lost response can't leave this out of sync.
    /// </summary>
    public async Task<UploadStatusResponse> GetUploadStatusAsync(string token, OwnerAuth owner, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), owner);
        var partSize = _o.S3.PartSizeBytes;
        var files = new List<UploadFileStatus>();

        foreach (var f in share.Files.OrderBy(f => f.Order))
        {
            IReadOnlyList<UploadedPart> parts = [];
            if (f is { Status: FileStatus.Uploading, UploadId: not null })
            {
                var stored = await storage.ListPartsAsync(f.StorageKey, f.UploadId, ct)
                    ?? throw ServiceException.Gone("upload_expired", "This upload expired before it finished. Start it again.");
                parts = stored.Select(p => new UploadedPart(p.PartNumber, p.ETag)).ToList();
            }
            files.Add(new UploadFileStatus(f.Id, f.Name, f.Size, PartCount(f.Size, partSize), f.Status == FileStatus.Complete, parts));
        }
        return new UploadStatusResponse(token, share.ExpiresAt, partSize, share.Status == ShareStatus.Active, files);
    }

    public async Task<PresignResponse> PresignAsync(string token, OwnerAuth owner, string fileId, PresignRequest req, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), owner);
        var file = RequireFile(share, fileId);
        if (share.Status != ShareStatus.Pending || file.Status != FileStatus.Uploading || file.UploadId is null)
            throw ServiceException.Conflict("not_uploading", "This file is no longer accepting uploads.");

        var count = PartCount(file.Size, _o.S3.PartSizeBytes);
        if (req.PartNumbers is null || req.PartNumbers.Count is 0 or > MaxPartsPerPresign
            || req.PartNumbers.Any(n => n < 1 || n > count))
            throw ServiceException.BadRequest("bad_parts", "Invalid part numbers.");

        return new PresignResponse(storage.PresignParts(file.StorageKey, file.UploadId, req.PartNumbers.Distinct()).ToList());
    }

    public async Task CompleteFileAsync(string token, OwnerAuth owner, string fileId, CompleteFileRequest req, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), owner);
        var file = RequireFile(share, fileId);
        if (file.Status == FileStatus.Complete) return; // idempotent retry
        if (share.Status != ShareStatus.Pending || file.UploadId is null)
            throw ServiceException.Conflict("not_uploading", "This file is no longer accepting uploads.");

        var count = PartCount(file.Size, _o.S3.PartSizeBytes);
        var numbers = req.Parts?.Select(p => p.PartNumber).Order().ToList() ?? [];
        if (!numbers.SequenceEqual(Enumerable.Range(1, count)) || req.Parts!.Any(p => string.IsNullOrWhiteSpace(p.ETag)))
            throw ServiceException.BadRequest("bad_parts", "Every part must be uploaded exactly once.");

        await storage.CompleteMultipartAsync(file.StorageKey, file.UploadId, req.Parts!, ct);
        file.Status = FileStatus.Complete;
        file.UploadId = null;
        await store.UpdateFileAsync(token, file, ct);
    }

    public async Task FinalizeAsync(string token, OwnerAuth owner, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), owner);
        if (share.Status == ShareStatus.Active)
        {
            await store.PutSummaryAsync(Summarize(share), ct); // idempotent: repairs the list if a first attempt died after activating
            return;
        }
        if (share.Files.Any(f => f.Status != FileStatus.Complete))
            throw ServiceException.Conflict("incomplete", "Some files haven't finished uploading.");

        foreach (var f in share.Files)
        {
            var size = await storage.GetObjectSizeAsync(f.StorageKey, ct);
            if (size != f.Size)
                throw ServiceException.Conflict("size_mismatch", $"'{f.Name}' arrived with the wrong size.");
        }
        await store.ActivateAsync(token, ct);
        await store.PutSummaryAsync(Summarize(share), ct);
    }

    /// <summary>Public metadata. For password-protected links the file list stays hidden until unlocked.</summary>
    public async Task<ShareView> GetViewAsync(string token, CancellationToken ct) =>
        ToView(await LoadActiveAsync(token, ct), unlocked: false);

    public async Task<ShareView> UnlockAsync(string token, string? password, CancellationToken ct)
    {
        var share = await LoadActiveAsync(token, ct);
        RequirePassword(share, password);
        return ToView(share, unlocked: true);
    }

    public async Task<DownloadResponse> DownloadAsync(string token, DownloadRequest req, CancellationToken ct)
    {
        var share = await LoadActiveAsync(token, ct);
        RequirePassword(share, req.Password);

        var targets = req.FileId is null ? share.Files : share.Files.Where(f => f.Id == req.FileId).ToList();
        if (targets.Count == 0) throw ServiceException.NotFound("file_not_found", "That file isn't part of this link.");

        // One download action (a single file, or "download all") counts as one download.
        if (!await store.TryRegisterDownloadAsync(token, clock.GetUtcNow(), ct))
            throw ServiceException.Gone("limit_reached", "This link has reached its download limit.");

        var ttl = TimeSpan.FromMinutes(_o.S3.DownloadUrlMinutes);
        return new DownloadResponse(targets
            .Select(f => new DownloadLink(f.Id, f.Name, f.Size, links.Sign(f.StorageKey, f.Name, ttl)))
            .ToList());
    }

    /// <summary>Checks the password and counts the download, then returns a short-lived signed URL for the zip stream.</summary>
    public async Task<ZipLinkResponse> CreateZipLinkAsync(string token, string? password, CancellationToken ct)
    {
        var share = await LoadActiveAsync(token, ct);
        RequirePassword(share, password);
        if (!await store.TryRegisterDownloadAsync(token, clock.GetUtcNow(), ct))
            throw ServiceException.Gone("limit_reached", "This link has reached its download limit.");
        return new ZipLinkResponse(zipLinks.Create(token, TimeSpan.FromMinutes(_o.S3.DownloadUrlMinutes)));
    }

    /// <summary>
    /// Validates a signed zip URL and returns what to stream. Doesn't count a download (the link-creating call
    /// already did) and ignores the download limit for that reason, but still honours expiry.
    /// </summary>
    public async Task<ZipPlan> PrepareZipAsync(string token, long? exp, string? sig, CancellationToken ct)
    {
        if (exp is null || !zipLinks.IsValid(token, exp.Value, sig))
            throw ServiceException.Forbidden("bad_zip_link", "This zip link is invalid or has expired. Start the download again.");
        var share = await store.GetAsync(token, ct);
        if (share is null || share.Status != ShareStatus.Active) throw ServiceException.NotFound();
        if (clock.GetUtcNow() >= share.ExpiresAt) throw ServiceException.Gone("expired", "This link has expired.");

        var files = share.Files.OrderBy(f => f.Order).ToList();
        var names = ZipStreamer.UniqueNames(files.Select(f => f.Name));
        var fileName = FileNames.Sanitize($"{share.SenderName} files.zip");
        return new ZipPlan(fileName.Length > 0 ? fileName : "files.zip", files.Select((f, i) => new ZipEntryPlan(names[i], f.StorageKey)).ToList());
    }

    public ConfigResponse GetConfig() =>
        new(_o.Email.Enabled, _o.Email.MaxRecipientsPerRequest, _o.Email.MaxRecipientsPerShare,
            new UploadLimits(_o.Limits.MaxFiles, _o.Limits.MaxFileBytes, _o.Limits.MaxTotalBytes, _o.Limits.BlockedExtensions, _o.Limits.AllowedExpiryDays));

    /// <summary>
    /// Emails the download link. Only the link's owner can, only once it is live, never more than the lifetime cap,
    /// and one bad address doesn't stop the others. The password is never included.
    /// </summary>
    public async Task<EmailShareResponse> SendEmailsAsync(string token, OwnerAuth owner, EmailShareRequest req, CancellationToken ct)
    {
        var cfg = _o.Email;
        if (!cfg.Enabled) throw ServiceException.NotFound("email_disabled", "Email isn't set up on this server.");

        var share = RequireOwner(await store.GetAsync(token, ct), owner);
        if (share.Status != ShareStatus.Active)
            throw ServiceException.Conflict("not_ready", "Finish uploading before emailing the link.");
        if (clock.GetUtcNow() >= share.ExpiresAt)
            throw ServiceException.Gone("expired", "This link has expired.");

        var sentBefore = share.EmailsSent; // read before reserving: some stores hand back the live object
        var recipients = ParseRecipients(req.Recipients, cfg.MaxRecipientsPerRequest);
        var message = TextSafety.Multiline(req.Message) is { Length: > 0 } cleaned ? cleaned : null;
        if (message is { Length: > 1000 })
            throw ServiceException.BadRequest("message_too_long", "Keep the message under 1,000 characters.");
        var replyTo = ParseOptionalAddress(req.SenderEmail);
        var senderName = ShareEmailComposer.OneLine(req.SenderName ?? "", 60) is { Length: > 0 } typed ? typed : _o.SenderName;

        if (!await store.TryReserveEmailsAsync(token, recipients.Count, cfg.MaxRecipientsPerShare, ct))
            throw ServiceException.Conflict("email_limit", $"This link can email at most {cfg.MaxRecipientsPerShare} people in total.");

        var composed = ShareEmailComposer.Compose(new ShareEmailContext(
            senderName, _o.SenderName, $"{_o.PublicBaseUrl!.TrimEnd('/')}/d/{token}", share.Files.Count, share.Files.Sum(f => f.Size),
            share.ExpiresAt, share.PasswordHash is not null, share.Files.OrderBy(f => f.Order).Select(f => f.Name).ToList(), message));

        var results = new List<EmailResult>();
        foreach (var to in recipients)
        {
            try
            {
                await email.SendAsync(new EmailMessage(to, _o.SenderName, cfg.FromAddress!, replyTo, composed.Subject, composed.Text, composed.Html), ct);
                results.Add(new EmailResult(to, true, null));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Log the kind of failure, not the address: addresses are other people's personal data.
                log.LogWarning("Email to a recipient failed: {Type}", e.GetType().Name);
                results.Add(new EmailResult(to, false, "The email service refused this address, or couldn't send it."));
            }
        }
        log.LogInformation("Emailed a share's link to {Count} recipients ({Sent} sent)", recipients.Count, results.Count(r => r.Sent));
        return new EmailShareResponse(results, Math.Max(0, cfg.MaxRecipientsPerShare - sentBefore - recipients.Count));
    }

    private static List<string> ParseRecipients(List<string>? raw, int max)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in raw ?? [])
        {
            var a = entry?.Trim() ?? "";
            if (a.Length == 0) continue;
            if (!IsPlainAddress(a)) throw ServiceException.BadRequest("bad_email", $"'{ShareEmailComposer.OneLine(a, 80)}' isn't a valid email address.");
            if (seen.Add(a)) list.Add(a);
        }
        if (list.Count == 0) throw ServiceException.BadRequest("no_recipients", "Add at least one email address.");
        if (list.Count > max) throw ServiceException.BadRequest("too_many_recipients", $"Send to at most {max} people at a time.");
        return list;
    }

    private static string? ParseOptionalAddress(string? raw)
    {
        var a = raw?.Trim();
        if (string.IsNullOrEmpty(a)) return null;
        return IsPlainAddress(a) ? a : throw ServiceException.BadRequest("bad_email", "Your email address isn't valid.");
    }

    /// <summary>A bare address only: no display name, no list, no angle brackets, with a dotted domain.</summary>
    private static bool IsPlainAddress(string a) =>
        a.Length <= 254
        && !a.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is ',' or ';' or '<' or '>' or '(' or ')' or '"')
        && MailAddress.TryCreate(a, out var m) && m.Address == a && m.Host.Contains('.') && !m.Host.EndsWith('.');

    public async Task DeleteAsync(string token, OwnerAuth owner, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), owner);
        foreach (var f in share.Files.Where(f => f is { Status: FileStatus.Uploading, UploadId: not null }))
            await storage.AbortMultipartAsync(f.StorageKey, f.UploadId!, ct);
        var done = share.Files.Where(f => f.Status == FileStatus.Complete).Select(f => f.StorageKey).ToList();
        await storage.DeleteAsync(done, ct);
        await store.DeleteAsync(token, ct);
        await store.RemoveSummaryAsync(Summarize(share), ct);
        if (done.Count > 0) await InvalidateEdgeAsync(done, ct);
    }

    /// <summary>Best effort: the files are already gone, and cached copies lapse on their own (short TTL, expiring signed URLs).</summary>
    private async Task InvalidateEdgeAsync(IReadOnlyCollection<string> keys, CancellationToken ct)
    {
        try
        {
            await edge.InvalidateAsync(EdgePaths(keys), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.LogWarning(e, "CDN invalidation failed after deleting a share; cached copies will expire on their own.");
        }
    }

    /// <summary>One wildcard per share folder ("d7/token/abc" -> "/d7/token/*"), so a delete costs a single invalidation path.</summary>
    public static IReadOnlyCollection<string> EdgePaths(IEnumerable<string> storageKeys) =>
        storageKeys.Select(k => "/" + k[..k.LastIndexOf('/')] + "/*").Distinct().ToList();

    public static int PartCount(long size, int partSize) => (int)((size + partSize - 1) / partSize);

    private async Task<Share> LoadActiveAsync(string token, CancellationToken ct)
    {
        var share = await store.GetAsync(token, ct);
        if (share is null || share.Status != ShareStatus.Active) throw ServiceException.NotFound();
        if (clock.GetUtcNow() >= share.ExpiresAt)
            throw ServiceException.Gone("expired", "This link has expired.");
        if (share.DownloadLimit > 0 && share.DownloadCount >= share.DownloadLimit)
            throw ServiceException.Gone("limit_reached", "This link has reached its download limit.");
        return share;
    }

    private static void RequirePassword(Share share, string? password)
    {
        if (share.PasswordHash is null) return;
        if (string.IsNullOrEmpty(password))
            throw ServiceException.Unauthorized("password_required", "This link needs a password.");
        // Real passwords are capped at 256 characters when set, so anything longer can't match; skip the slow hash.
        if (password.Length > 256 || !PasswordHasher.Verify(password, share.PasswordHash))
            throw ServiceException.Unauthorized("wrong_password", "That password isn't right.");
    }

    private static Share RequireOwner(Share? share, OwnerAuth owner)
    {
        if (share is null) throw ServiceException.NotFound();
        if (owner.SignedInOwner) return share;
        if (string.IsNullOrEmpty(owner.Key) || !Secrets.FixedTimeEquals(Secrets.Sha256(owner.Key), share.OwnerKeyHash))
            throw ServiceException.Forbidden("bad_owner_key", "You can't change this link.");
        return share;
    }

    private static ShareSummary Summarize(Share s) => new(
        s.Token, s.CreatedAt, s.ExpiresAt, s.Files.Count, s.Files.Sum(f => f.Size), s.PasswordHash is not null, s.DownloadLimit);

    /// <summary>The owner's live links, newest first, the same on every device they sign in on.</summary>
    public async Task<IReadOnlyList<ShareSummary>> ListMineAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return (await store.ListSummariesAsync(100, ct)).Where(s => s.ExpiresAt > now).ToList();
    }

    private static ShareFile RequireFile(Share share, string fileId) =>
        share.Files.FirstOrDefault(f => f.Id == fileId)
        ?? throw ServiceException.NotFound("file_not_found", "That file isn't part of this link.");

    private static ShareView ToView(Share s, bool unlocked)
    {
        var showFiles = s.PasswordHash is null || unlocked;
        return new ShareView(
            s.Token, s.SenderName, s.ExpiresAt, s.PasswordHash is not null,
            s.DownloadLimit > 0 ? Math.Max(0, s.DownloadLimit - s.DownloadCount) : null,
            s.Files.Sum(f => f.Size), s.Files.Count,
            showFiles ? s.Files.OrderBy(f => f.Order).Select(f => new ShareFileView(f.Id, f.Name, f.Size)).ToList() : null);
    }

    private static string NormalizeContentType(string? ct) =>
        string.IsNullOrWhiteSpace(ct) || ct.Length > 100 || ct.Any(char.IsControl) ? "application/octet-stream" : ct;
}
