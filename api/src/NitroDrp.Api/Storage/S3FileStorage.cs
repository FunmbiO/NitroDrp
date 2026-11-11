using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using NitroDrp.Api.Services;

namespace NitroDrp.Api.Storage;

public sealed class S3FileStorage : IFileStorage
{
    private readonly IAmazonS3 _s3;
    private readonly IAmazonS3 _signer;
    private readonly S3Options _o;
    private readonly Protocol _signProtocol;

    public S3FileStorage(IAmazonS3 s3, IAmazonS3 signer, S3Options options)
    {
        _s3 = s3;
        _signer = signer;
        _o = options;
        var signUrl = options.PublicServiceUrl ?? options.ServiceUrl;
        _signProtocol = signUrl is not null && signUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? Protocol.HTTP : Protocol.HTTPS;
    }

    public static IAmazonS3 CreateClient(S3Options o, string? serviceUrl)
    {
        var cfg = new AmazonS3Config { ForcePathStyle = o.ForcePathStyle };
        if (!string.IsNullOrWhiteSpace(serviceUrl))
        {
            cfg.ServiceURL = serviceUrl;
            cfg.AuthenticationRegion = o.Region;
        }
        else
        {
            cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(o.Region);
        }
        // Without explicit keys, fall back to the default AWS credential chain (env vars, profile, role).
        return !string.IsNullOrEmpty(o.AccessKey)
            ? new AmazonS3Client(new BasicAWSCredentials(o.AccessKey, o.SecretKey), cfg)
            : new AmazonS3Client(cfg);
    }

    /// <summary>Idempotent: a bucket we already own is fine.</summary>
    public async Task EnsureBucketAsync(CancellationToken ct)
    {
        try
        {
            // Outside us-east-1 S3 requires a location constraint. The SDK adds it only for AWS's own
            // endpoints, not when ServiceURL points at an emulator, so say it explicitly (us-east-1 must omit it).
            await _s3.PutBucketAsync(new PutBucketRequest
            {
                BucketName = _o.Bucket,
                BucketRegionName = _o.Region == "us-east-1" ? null : _o.Region,
            }, ct);
        }
        catch (AmazonS3Exception e) when (e.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
        {
        }
    }

    public async Task<string> BeginMultipartAsync(string key, string contentType, CancellationToken ct)
    {
        var r = await _s3.InitiateMultipartUploadAsync(
            new InitiateMultipartUploadRequest { BucketName = _o.Bucket, Key = key, ContentType = contentType }, ct);
        return r.UploadId;
    }

    public IReadOnlyList<PresignedPart> PresignParts(string key, string uploadId, IEnumerable<int> partNumbers) =>
        partNumbers.Select(n => new PresignedPart(n, _signer.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _o.Bucket,
            Key = key,
            Verb = HttpVerb.PUT,
            UploadId = uploadId,
            PartNumber = n,
            Protocol = _signProtocol,
            Expires = DateTime.UtcNow.AddMinutes(_o.UploadUrlMinutes),
        }))).ToList();

    public Task CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken ct) =>
        _s3.CompleteMultipartUploadAsync(new CompleteMultipartUploadRequest
        {
            BucketName = _o.Bucket,
            Key = key,
            UploadId = uploadId,
            PartETags = parts.OrderBy(p => p.PartNumber).Select(p => new PartETag(p.PartNumber, p.ETag)).ToList(),
        }, ct);

    public async Task<IReadOnlyList<StoredPart>?> ListPartsAsync(string key, string uploadId, CancellationToken ct)
    {
        var parts = new List<StoredPart>();
        var req = new ListPartsRequest { BucketName = _o.Bucket, Key = key, UploadId = uploadId };
        try
        {
            ListPartsResponse resp;
            do
            {
                resp = await _s3.ListPartsAsync(req, ct);
                parts.AddRange(resp.Parts.Select(p => new StoredPart(p.PartNumber, p.ETag, p.Size)));
                req.PartNumberMarker = resp.NextPartNumberMarker.ToString();
            } while (resp.IsTruncated);
        }
        catch (AmazonS3Exception e) when (e.ErrorCode == "NoSuchUpload")
        {
            return null;
        }
        return parts;
    }

    public Task AbortMultipartAsync(string key, string uploadId, CancellationToken ct) =>
        _s3.AbortMultipartUploadAsync(_o.Bucket, key, uploadId, ct);

    public async Task<long?> GetObjectSizeAsync(string key, CancellationToken ct)
    {
        try
        {
            var m = await _s3.GetObjectMetadataAsync(_o.Bucket, key, ct);
            return m.ContentLength;
        }
        catch (AmazonS3Exception e) when (e.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct) =>
        (await _s3.GetObjectAsync(_o.Bucket, key, ct)).ResponseStream;

    public string PresignDownload(string key, string fileName, TimeSpan ttl) =>
        _signer.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = _o.Bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Protocol = _signProtocol,
            Expires = DateTime.UtcNow.Add(ttl),
            ResponseHeaderOverrides = new ResponseHeaderOverrides
            {
                ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(fileName)}",
            },
        });

    public async Task DeleteAsync(IEnumerable<string> keys, CancellationToken ct)
    {
        var list = keys.Select(k => new KeyVersion { Key = k }).ToList();
        foreach (var chunk in list.Chunk(1000))
            await _s3.DeleteObjectsAsync(new DeleteObjectsRequest { BucketName = _o.Bucket, Objects = chunk.ToList(), Quiet = true }, ct);
    }
}
