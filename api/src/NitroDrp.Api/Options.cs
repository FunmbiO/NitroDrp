namespace NitroDrp.Api;

public sealed class NitroDrpOptions
{
    /// <summary>Shown to recipients: "[SenderName] sent you 3 files".</summary>
    public string SenderName { get; set; } = "Someone";

    /// <summary>Shared secret the sender UI sends in X-Api-Key to create shares.</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>
    /// The address visitors use, e.g. https://drop.example.com. Links in emails are built from it. Required when
    /// email is enabled.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    public AuthOptions Auth { get; set; } = new();
    public NotificationOptions Notifications { get; set; } = new();
    public EmailOptions Email { get; set; } = new();
    public S3Options S3 { get; set; } = new();
    public CloudFrontOptions CloudFront { get; set; } = new();
    public DynamoOptions Dynamo { get; set; } = new();
    public LimitOptions Limits { get; set; } = new();
}

/// <summary>
/// Optional: tell the owner when something happens: someone downloads a link, a link is used up or about to expire, an
/// email can't be sent. The feed inside the app is always there; push notifications to phones and a daily email
/// summary are extras.
/// </summary>
public sealed class NotificationOptions
{
    public PushOptions Push { get; set; } = new();

    /// <summary>Where the daily summary goes. Empty means no summary. Needs email to be set up too.</summary>
    public string? DigestEmailTo { get; set; }

    /// <summary>The hour (UTC, 0 to 23) the daily summary is sent.</summary>
    public int DigestHourUtc { get; set; } = 13;

    /// <summary>How long before a link ends that the owner is warned.</summary>
    public int ExpiringHours { get; set; } = 24;

    /// <summary>How many days the feed keeps events.</summary>
    public int RetentionDays { get; set; } = 30;

    public int MaxDevices { get; set; } = 10;
}

/// <summary>Push notifications to phones and laptops (Web Push). Needs a VAPID key pair: see docs/NOTIFICATIONS.md.</summary>
public sealed class PushOptions
{
    public string? PublicKey { get; set; }
    public string? PrivateKey { get; set; }

    /// <summary>Who the push services should contact about this server: mailto:you@example.com or https://your.site.</summary>
    public string? Subject { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(PrivateKey);
}

/// <summary>
/// Optional: sign in with a passkey (Face ID, fingerprint, a phone PIN, a security key) instead of typing the access
/// key on every device. Needs PublicBaseUrl, because a passkey is tied to the site's address.
/// </summary>
public sealed class AuthOptions
{
    public bool Passkeys { get; set; }

    /// <summary>The name shown in the device's passkey prompt.</summary>
    public string RpName { get; set; } = "NitroDrp";

    /// <summary>How long a sign-in lasts without use. Each time you open the app it's extended.</summary>
    public int SessionDays { get; set; } = 90;

    public int MaxPasskeys { get; set; } = 20;

    /// <summary>
    /// Extra web origins allowed to use passkeys, besides PublicBaseUrl. For local development only, for example the
    /// Vite dev server: http://localhost:5173.
    /// </summary>
    public string[] ExtraOrigins { get; set; } = [];
}

/// <summary>Optional: email the download link to people, in addition to copying it. Uses Amazon SES.</summary>
public sealed class EmailOptions
{
    public bool Enabled { get; set; }

    /// <summary>The sender, on a domain verified in SES, e.g. transfers@jadedmedia.ca.</summary>
    public string? FromAddress { get; set; }

    public string Region { get; set; } = "us-east-2";

    /// <summary>Set for an SES-compatible emulator (moto). Leave empty for real AWS.</summary>
    public string? ServiceUrl { get; set; }

    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? ConfigurationSet { get; set; }

    /// <summary>Dev only: register the sender's domain with the emulator at startup. In AWS, Terraform does it.</summary>
    public bool AutoCreateIdentity { get; set; }

    /// <summary>Most recipients in one request.</summary>
    public int MaxRecipientsPerRequest { get; set; } = 10;

    /// <summary>Most emails one link can ever send, so a single link can't be used to spam.</summary>
    public int MaxRecipientsPerShare { get; set; } = 20;
}

public sealed class S3Options
{
    public string Bucket { get; set; } = "";
    public string Region { get; set; } = "ca-central-1";

    /// <summary>Set for S3-compatible emulators (moto). Leave empty for real AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Endpoint the *browser* can reach, used only to sign presigned URLs. Needed when
    /// the API talks to the emulator at http://s3:5000 but browsers use http://localhost:9000.
    /// </summary>
    public string? PublicServiceUrl { get; set; }

    public bool ForcePathStyle { get; set; }

    /// <summary>Dev only: create the bucket at startup. In AWS the bucket is created by Terraform.</summary>
    public bool AutoCreateBucket { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    public int PartSizeBytes { get; set; } = 16 * 1024 * 1024;
    public int UploadUrlMinutes { get; set; } = 60;
    public int DownloadUrlMinutes { get; set; } = 15;
}

/// <summary>Optional. When Domain is set, downloads go through CloudFront signed URLs instead of S3 presigned URLs.</summary>
public sealed class CloudFrontOptions
{
    /// <summary>Distribution domain or alias, e.g. d111111abcdef8.cloudfront.net</summary>
    public string? Domain { get; set; }

    /// <summary>ID of the CloudFront public key (Terraform output cloudfront_key_pair_id).</summary>
    public string? KeyPairId { get; set; }

    /// <summary>Path to the matching RSA private key in PEM format.</summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>Distribution ID (Terraform output cloudfront_distribution_id). When set, deleting a link also invalidates the edge cache.</summary>
    public string? DistributionId { get; set; }

    public bool Enabled => !string.IsNullOrWhiteSpace(Domain);
}

public sealed class DynamoOptions
{
    public string Table { get; set; } = "nitrodrp";
    public string? ServiceUrl { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string Region { get; set; } = "ca-central-1";

    /// <summary>Dev only. In AWS the table is created by Terraform.</summary>
    public bool AutoCreateTable { get; set; }
}

public sealed class LimitOptions
{
    public int MaxFiles { get; set; } = 100;
    public long MaxFileBytes { get; set; } = 20L * 1024 * 1024 * 1024;
    public long MaxTotalBytes { get; set; } = 50L * 1024 * 1024 * 1024;
    /// <summary>Zips stream through this server, so cap how many run at once (home uplink).</summary>
    public int MaxConcurrentZips { get; set; } = 2;
    public int[] AllowedExpiryDays { get; set; } = [];
    public string[] BlockedExtensions { get; set; } = [];
}
