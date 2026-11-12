using Microsoft.AspNetCore.Mvc;
using NitroDrp.Api.Services;
using NitroDrp.Api.Storage;

namespace NitroDrp.Api.Endpoints;

public static class ShareEndpoints
{
    private const string OwnerHeader = "X-Owner-Key";
    private const string ApiKeyHeader = "X-Api-Key";

    public static void MapShareEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        api.MapGet("/config", (ShareService svc) => Results.Ok(svc.GetConfig())).RequireRateLimiting("public");

        var shares = api.MapGroup("/shares");

        // Sender: needs the API key (private instance). Everything after this uses the per-share owner key.
        shares.MapPost("/", async (
            [FromBody] CreateShareRequest body, HttpRequest http, ShareService svc, Microsoft.Extensions.Options.IOptions<NitroDrpOptions> opts, CancellationToken ct) =>
        {
            var key = http.Headers[ApiKeyHeader].ToString();
            if (key.Length == 0 || !Secrets.FixedTimeEquals(key, opts.Value.ApiKey))
                throw ServiceException.Unauthorized("bad_api_key", "Wrong access key.");
            var created = await svc.CreateAsync(body, ct);
            return Results.Created($"/api/shares/{created.Token}", created);
        }).RequireRateLimiting("create");

        shares.MapGet("/{token}/upload", async (string token, HttpRequest http, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetUploadStatusAsync(token, Owner(http), ct))).RequireRateLimiting("upload");

        shares.MapPost("/{token}/files/{fileId}/presign", async (
            string token, string fileId, [FromBody] PresignRequest body, HttpRequest http, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.PresignAsync(token, Owner(http), fileId, body, ct))).RequireRateLimiting("upload");

        shares.MapPost("/{token}/files/{fileId}/complete", async (
            string token, string fileId, [FromBody] CompleteFileRequest body, HttpRequest http, ShareService svc, CancellationToken ct) =>
        {
            await svc.CompleteFileAsync(token, Owner(http), fileId, body, ct);
            return Results.NoContent();
        }).RequireRateLimiting("upload");

        // Optional: email the link. Owner-only, only once the link is live, and capped per link.
        shares.MapPost("/{token}/email", async (
            string token, [FromBody] EmailShareRequest body, HttpRequest http, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.SendEmailsAsync(token, Owner(http), body, ct))).RequireRateLimiting("email");

        shares.MapPost("/{token}/finalize", async (string token, HttpRequest http, ShareService svc, CancellationToken ct) =>
        {
            await svc.FinalizeAsync(token, Owner(http), ct);
            return Results.NoContent();
        }).RequireRateLimiting("upload");

        shares.MapDelete("/{token}", async (string token, HttpRequest http, ShareService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(token, Owner(http), ct);
            return Results.NoContent();
        }).RequireRateLimiting("upload");

        // Recipient
        shares.MapGet("/{token}", async (string token, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetViewAsync(token, ct))).RequireRateLimiting("public");

        shares.MapPost("/{token}/unlock", async (string token, [FromBody] PasswordRequest body, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.UnlockAsync(token, body.Password, ct))).RequireRateLimiting("password");

        // Zip: POST counts the download and returns a signed URL; the browser then navigates to it so the
        // archive streams straight to disk. Bytes are read from storage and zipped on the fly.
        shares.MapPost("/{token}/zip-link", async (string token, [FromBody] PasswordRequest body, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.CreateZipLinkAsync(token, body.Password, ct))).RequireRateLimiting("password");

        shares.MapGet("/{token}/zip", async (
            string token, long? exp, string? sig, HttpContext http, ShareService svc, IObjectReader reader, ZipGate gate, CancellationToken ct) =>
        {
            var plan = await svc.PrepareZipAsync(token, exp, sig, ct);
            if (!gate.TryEnter())
                throw new ServiceException(429, "busy", "Too many zips are being prepared right now. Try again in a minute.");
            try
            {
                var res = http.Response;
                res.ContentType = "application/zip";
                res.Headers.ContentDisposition = $"attachment; filename*=UTF-8''{Uri.EscapeDataString(plan.FileName)}";
                res.Headers.CacheControl = "no-store";
                await ZipStreamer.WriteAsync(res.Body, plan.Entries.Select(e => new ZipSource(e.Name, c => reader.OpenAsync(e.StorageKey, c))), ct);
            }
            finally
            {
                gate.Exit();
            }
            return Results.Empty;
        }).RequireRateLimiting("public");

        shares.MapPost("/{token}/download", async (string token, [FromBody] DownloadRequest body, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.DownloadAsync(token, body, ct))).RequireRateLimiting("password");
    }

    private static string? Owner(HttpRequest http) => http.Headers[OwnerHeader].FirstOrDefault();
}
