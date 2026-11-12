using System.Threading.RateLimiting;
using Amazon.DynamoDBv2;
using Amazon.S3;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;
using NitroDrp.Api;
using NitroDrp.Api.Email;
using NitroDrp.Api.Endpoints;
using NitroDrp.Api.Services;
using NitroDrp.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<NitroDrpOptions>(builder.Configuration.GetSection("NitroDrp"));
var opts = builder.Configuration.GetSection("NitroDrp").Get<NitroDrpOptions>() ?? new();
if (opts.ApiKey.Length < 16) throw new InvalidOperationException("NitroDrp:ApiKey must be set (16+ characters).");
if (string.IsNullOrWhiteSpace(opts.S3.Bucket)) throw new InvalidOperationException("NitroDrp:S3:Bucket must be set.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAmazonDynamoDB>(_ => DynamoShareStore.CreateClient(opts.Dynamo));
builder.Services.AddSingleton<DynamoShareStore>(sp => new(sp.GetRequiredService<IAmazonDynamoDB>(), opts.Dynamo));
builder.Services.AddSingleton<IShareStore>(sp => sp.GetRequiredService<DynamoShareStore>());
builder.Services.AddSingleton<S3FileStorage>(_ => new S3FileStorage(
    S3FileStorage.CreateClient(opts.S3, opts.S3.ServiceUrl),
    S3FileStorage.CreateClient(opts.S3, opts.S3.PublicServiceUrl ?? opts.S3.ServiceUrl),
    opts.S3));
builder.Services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<S3FileStorage>());
if (opts.CloudFront.Enabled)
{
    // Downloads via CloudFront signed URLs; uploads still go straight to S3.
    var cf = opts.CloudFront;
    if (string.IsNullOrWhiteSpace(cf.KeyPairId) || string.IsNullOrWhiteSpace(cf.PrivateKeyPath))
        throw new InvalidOperationException("NitroDrp:CloudFront needs KeyPairId and PrivateKeyPath when Domain is set.");
    var signer = CloudFrontDownloadLinkSigner.FromPem(cf.Domain!, cf.KeyPairId, File.ReadAllText(cf.PrivateKeyPath), TimeProvider.System);
    builder.Services.AddSingleton<IDownloadLinkSigner>(signer);
    builder.Services.AddSingleton<IObjectReader>(new CloudFrontObjectReader(new HttpClient { Timeout = Timeout.InfiniteTimeSpan }, signer));
}
else
{
    builder.Services.AddSingleton<IDownloadLinkSigner>(sp => new S3DownloadLinkSigner(sp.GetRequiredService<IFileStorage>()));
    builder.Services.AddSingleton<IObjectReader>(sp => new S3ObjectReader(sp.GetRequiredService<IFileStorage>()));
}
if (opts.CloudFront.Enabled && !string.IsNullOrWhiteSpace(opts.CloudFront.DistributionId))
    // CloudFront's API is global; credentials come from the default chain (the AWS_ACCESS_KEY_ID env vars in Compose).
    builder.Services.AddSingleton<IEdgeCache>(new CloudFrontEdgeCache(new Amazon.CloudFront.AmazonCloudFrontClient(), opts.CloudFront.DistributionId));
else
    builder.Services.AddSingleton<IEdgeCache, NoEdgeCache>();
if (opts.Email.Enabled)
{
    if (string.IsNullOrWhiteSpace(opts.Email.FromAddress) || !opts.Email.FromAddress.Contains('@'))
        throw new InvalidOperationException("NitroDrp:Email:FromAddress must be set (e.g. transfers@example.com) when email is enabled.");
    if (!Uri.TryCreate(opts.PublicBaseUrl, UriKind.Absolute, out _))
        throw new InvalidOperationException("NitroDrp:PublicBaseUrl must be set (e.g. https://drop.example.com) when email is enabled.");
    builder.Services.AddSingleton(new SesEmailSender(SesEmailSender.CreateClient(opts.Email), opts.Email));
    builder.Services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<SesEmailSender>());
}
else
{
    builder.Services.AddSingleton<IEmailSender, NoEmailSender>();
}
builder.Services.AddSingleton(ZipLinks.FromApiKey(opts.ApiKey, TimeProvider.System));
builder.Services.AddSingleton(new ZipGate(Math.Max(1, opts.Limits.MaxConcurrentZips)));
builder.Services.AddSingleton<ShareService>();

// The API only ever sits behind our nginx, which sets X-Client-IP from Cloudflare's CF-Connecting-IP
// (or the socket address when not behind Cloudflare). Rate limits key on that, not on nginx's own IP.
static string ClientIp(HttpContext c) =>
    c.Request.Headers["X-Client-IP"].FirstOrDefault() ?? c.Connection.RemoteIpAddress?.ToString() ?? "unknown";

static RateLimitPartition<string> PerIp(HttpContext c, int permits) =>
    RateLimitPartition.GetFixedWindowLimiter(ClientIp(c), _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = permits,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
    });

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("create", c => PerIp(c, 20));
    o.AddPolicy("upload", c => PerIp(c, 1200));   // one presign + one complete per 16 MiB part
    o.AddPolicy("public", c => PerIp(c, 60));
    o.AddPolicy("email", c => PerIp(c, 10));    // each request can reach up to 10 inboxes
    o.AddPolicy("password", c => PerIp(c, 10));   // PBKDF2 is deliberately slow; this also throttles guessing
});

var app = builder.Build();

app.UseExceptionHandler(errors => errors.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (ex is ServiceException se)
    {
        ctx.Response.StatusCode = se.Status;
        await ctx.Response.WriteAsJsonAsync(new { code = se.Code, message = se.Message });
        return;
    }
    ctx.RequestServices.GetRequiredService<ILogger<Program>>().LogError(ex, "Unhandled error");
    ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await ctx.Response.WriteAsJsonAsync(new { code = "internal_error", message = "Something went wrong." });
}));
app.UseRateLimiter();

if (opts.Dynamo.AutoCreateTable)
    await app.Services.GetRequiredService<DynamoShareStore>().EnsureTableAsync(CancellationToken.None);

if (opts.S3.AutoCreateBucket)
    await app.Services.GetRequiredService<S3FileStorage>().EnsureBucketAsync(CancellationToken.None);

app.MapShareEndpoints();
app.Run();

public partial class Program;
