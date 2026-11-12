namespace NitroDrp.Api.Domain;

/// <summary>
/// What the owner's "Recent links" list shows for one live link. Kept as its own small record in the table so the
/// list is one query, the same on every device. (Live counters such as downloads used stay on the link itself.)
/// </summary>
public sealed record ShareSummary(
    string Token,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    int FileCount,
    long TotalSize,
    bool PasswordProtected,
    int DownloadLimit);
