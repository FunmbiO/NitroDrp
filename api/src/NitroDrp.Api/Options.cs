namespace NitroDrp.Api;

public sealed class NitroDrpOptions
{
    /// <summary>Shown to recipients: "[SenderName] sent you 3 files".</summary>
    public string SenderName { get; set; } = "Someone";

    /// <summary>Shared secret the sender UI sends in X-Api-Key to create shares.</summary>
    public string ApiKey { get; set; } = "";

    public S3Options S3 { get; set; } = new();
    public DynamoOptions Dynamo { get; set; } = new();
    public LimitOptions Limits { get; set; } = new();
}

public sealed class S3Options
{
    public string Bucket { get; set; } = "";
    public string Region { get; set; } = "ca-central-1";

    /// <summary>Set for S3-compatible stores (MinIO). Leave empty for real AWS.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// Endpoint the *browser* can reach, used only to sign presigned URLs. Needed when
    /// the API talks to MinIO at http://minio:9000 but browsers use http://localhost:9000.
    /// </summary>
    public string? PublicServiceUrl { get; set; }

    public bool ForcePathStyle { get; set; }
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }

    public int PartSizeBytes { get; set; } = 16 * 1024 * 1024;
    public int UploadUrlMinutes { get; set; } = 60;
    public int DownloadUrlMinutes { get; set; } = 15;
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
    public int[] AllowedExpiryDays { get; set; } = [];
    public string[] BlockedExtensions { get; set; } = [];
}
