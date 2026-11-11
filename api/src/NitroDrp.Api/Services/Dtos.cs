namespace NitroDrp.Api.Services;

public sealed record NewFile(string Name, long Size, string? ContentType);
public sealed record CreateShareRequest(List<NewFile> Files, int ExpiresInDays, string? Password, int? DownloadLimit);

public sealed record CreatedFile(string Id, string Name, long Size, int PartCount);
public sealed record CreateShareResponse(
    string Token, string OwnerKey, DateTimeOffset ExpiresAt, int PartSize, List<CreatedFile> Files);

public sealed record PresignRequest(List<int> PartNumbers);
public sealed record PresignedPart(int PartNumber, string Url);
public sealed record PresignResponse(List<PresignedPart> Urls);

public sealed record UploadedPart(int PartNumber, string ETag);
public sealed record CompleteFileRequest(List<UploadedPart> Parts);

public sealed record PasswordRequest(string? Password);
public sealed record DownloadRequest(string? Password, string? FileId);

public sealed record ShareFileView(string Id, string Name, long Size);
public sealed record ShareView(
    string Token,
    string SenderName,
    DateTimeOffset ExpiresAt,
    bool PasswordRequired,
    int? DownloadsRemaining,
    long TotalSize,
    int FileCount,
    IReadOnlyList<ShareFileView>? Files);

public sealed record DownloadLink(string Id, string Name, long Size, string Url);
public sealed record DownloadResponse(IReadOnlyList<DownloadLink> Files);

public sealed record UploadFileStatus(string Id, string Name, long Size, int PartCount, bool Complete, IReadOnlyList<UploadedPart> Parts);
public sealed record UploadStatusResponse(
    string Token, DateTimeOffset ExpiresAt, int PartSize, bool Finalized, IReadOnlyList<UploadFileStatus> Files);
