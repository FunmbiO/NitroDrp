using Microsoft.Extensions.Options;
using NitroDrp.Api.Domain;
using NitroDrp.Api.Storage;

namespace NitroDrp.Api.Services;

/// <summary>All business rules live here so they can be tested without HTTP, S3 or DynamoDB.</summary>
public sealed class ShareService(
    IShareStore store, IFileStorage storage, IDownloadLinkSigner links, IOptions<NitroDrpOptions> options, TimeProvider clock)
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
        var downloadLimit = req.DownloadLimit ?? 0;
        if (downloadLimit is < 0 or > MaxDownloadLimit)
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
    public async Task<UploadStatusResponse> GetUploadStatusAsync(string token, string? ownerKey, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), ownerKey);
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

    public async Task<PresignResponse> PresignAsync(string token, string? ownerKey, string fileId, PresignRequest req, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), ownerKey);
        var file = RequireFile(share, fileId);
        if (share.Status != ShareStatus.Pending || file.Status != FileStatus.Uploading || file.UploadId is null)
            throw ServiceException.Conflict("not_uploading", "This file is no longer accepting uploads.");

        var count = PartCount(file.Size, _o.S3.PartSizeBytes);
        if (req.PartNumbers is null || req.PartNumbers.Count is 0 or > MaxPartsPerPresign
            || req.PartNumbers.Any(n => n < 1 || n > count))
            throw ServiceException.BadRequest("bad_parts", "Invalid part numbers.");

        return new PresignResponse(storage.PresignParts(file.StorageKey, file.UploadId, req.PartNumbers.Distinct()).ToList());
    }

    public async Task CompleteFileAsync(string token, string? ownerKey, string fileId, CompleteFileRequest req, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), ownerKey);
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

    public async Task FinalizeAsync(string token, string? ownerKey, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), ownerKey);
        if (share.Status == ShareStatus.Active) return;
        if (share.Files.Any(f => f.Status != FileStatus.Complete))
            throw ServiceException.Conflict("incomplete", "Some files haven't finished uploading.");

        foreach (var f in share.Files)
        {
            var size = await storage.GetObjectSizeAsync(f.StorageKey, ct);
            if (size != f.Size)
                throw ServiceException.Conflict("size_mismatch", $"'{f.Name}' arrived with the wrong size.");
        }
        await store.ActivateAsync(token, ct);
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

    public async Task DeleteAsync(string token, string? ownerKey, CancellationToken ct)
    {
        var share = RequireOwner(await store.GetAsync(token, ct), ownerKey);
        foreach (var f in share.Files.Where(f => f is { Status: FileStatus.Uploading, UploadId: not null }))
            await storage.AbortMultipartAsync(f.StorageKey, f.UploadId!, ct);
        await storage.DeleteAsync(share.Files.Where(f => f.Status == FileStatus.Complete).Select(f => f.StorageKey), ct);
        await store.DeleteAsync(token, ct);
    }

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
        if (!PasswordHasher.Verify(password, share.PasswordHash))
            throw ServiceException.Unauthorized("wrong_password", "That password isn't right.");
    }

    private static Share RequireOwner(Share? share, string? ownerKey)
    {
        if (share is null) throw ServiceException.NotFound();
        if (string.IsNullOrEmpty(ownerKey) || !Secrets.FixedTimeEquals(Secrets.Sha256(ownerKey), share.OwnerKeyHash))
            throw ServiceException.Forbidden("bad_owner_key", "You can't change this link.");
        return share;
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
