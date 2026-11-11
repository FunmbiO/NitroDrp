namespace NitroDrp.Api.Domain;

public enum ShareStatus { Pending, Active }
public enum FileStatus { Uploading, Complete }

public sealed class Share
{
    public required string Token { get; init; }
    public required string SenderName { get; init; }
    public ShareStatus Status { get; set; } = ShareStatus.Pending;
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public string? PasswordHash { get; init; }

    /// <summary>0 means unlimited.</summary>
    public int DownloadLimit { get; init; }
    public int DownloadCount { get; set; }

    /// <summary>SHA-256 of the owner key handed to the uploader. Lets them upload, finalize and delete.</summary>
    public required string OwnerKeyHash { get; init; }

    public List<ShareFile> Files { get; init; } = [];
}

public sealed class ShareFile
{
    public required string Id { get; init; }
    public required int Order { get; init; }
    public required string Name { get; init; }
    public required long Size { get; init; }
    public required string ContentType { get; init; }
    public required string StorageKey { get; init; }
    public string? UploadId { get; set; }
    public FileStatus Status { get; set; } = FileStatus.Uploading;
}
