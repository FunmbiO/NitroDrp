using Microsoft.AspNetCore.Mvc;
using NitroDrp.Api.Auth;
using NitroDrp.Api.Services;
using NitroDrp.Api.Storage;

namespace NitroDrp.Api.Endpoints;

public static class ShareEndpoints
{
    private const string OwnerHeader = "X-Owner-Key";

    public static void MapShareEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        api.MapGet("/config", (ShareService svc) => Results.Ok(svc.GetConfig())).RequireRateLimiting("public");

        var shares = api.MapGroup("/shares");

        // Sender: signed in with a passkey, or holding the access key (the recovery key, and what scripts use).
        // Everything after this uses the per-share owner key, or the signed-in session.
        shares.MapPost("/", async (
            [FromBody] CreateShareRequest body, HttpRequest http, Sessions sessions, ShareService svc, Microsoft.Extensions.Options.IOptions<NitroDrpOptions> opts, CancellationToken ct) =>
        {
            var caller = await AdminAccess.CheckAsync(http, sessions, opts.Value.ApiKey, ct);
            if (!caller.IsAdmin)
                throw ServiceException.Unauthorized("bad_api_key", "Wrong access key.");
            var created = await svc.CreateAsync(body, ct);
            return Results.Created($"/api/shares/{created.Token}", created);
        }).RequireRateLimiting("create");

        // The owner's live links, the same on every device.
        shares.MapGet("/", async (HttpRequest http, Sessions sessions, ShareService svc, Microsoft.Extensions.Options.IOptions<NitroDrpOptions> opts, HttpResponse res, CancellationToken ct) =>
        {
            var caller = await AdminAccess.CheckAsync(http, sessions, opts.Value.ApiKey, ct);
            if (!caller.IsAdmin) throw ServiceException.Unauthorized("sign_in_required", "Sign in first.");
            res.Headers.CacheControl = "no-store";
            return Results.Ok(await svc.ListMineAsync(ct));
        }).RequireRateLimiting("public");

        shares.MapGet("/{token}/upload", async (string token, HttpRequest http, Sessions sessions, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.GetUploadStatusAsync(token, await OwnerAsync(http, sessions, ct), ct))).RequireRateLimiting("upload");

        shares.MapPost("/{token}/files/{fileId}/presign", async (
            string token, string fileId, [FromBody] PresignRequest body, HttpRequest http, Sessions sessions, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.PresignAsync(token, await OwnerAsync(http, sessions, ct), fileId, body, ct))).RequireRateLimiting("upload");

        shares.MapPost("/{token}/files/{fileId}/complete", async (
            string token, string fileId, [FromBody] CompleteFileRequest body, HttpRequest http, Sessions sessions, ShareService svc, CancellationToken ct) =>
        {
            await svc.CompleteFileAsync(token, await OwnerAsync(http, sessions, ct), fileId, body, ct);
            return Results.NoContent();
        }).RequireRateLimiting("upload");

        // Optional: email the link. Owner-only, only once the link is live, and capped per link.
        shares.MapPost("/{token}/email", async (
            string token, [FromBody] EmailShareRequest body, HttpRequest http, Sessions sessions, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.SendEmailsAsync(token, await OwnerAsync(http, sessions, ct), body, ct))).RequireRateLimiting("email");

        shares.MapPost("/{token}/finalize", async (string token, HttpRequest http, Sessions sessions, ShareService svc, CancellationToken ct) =>
        {
            await svc.FinalizeAsync(token, await OwnerAsync(http, sessions, ct), ct);
            return Results.NoContent();
        }).RequireRateLimiting("upload");

        shares.MapDelete("/{token}", async (string token, HttpRequest http, Sessions sessions, ShareService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(token, await OwnerAsync(http, sessions, ct), ct);
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

    /// <summary>The signed-in owner may act on any of their links; otherwise the link's own owner key is needed.</summary>
    private static async Task<OwnerAuth> OwnerAsync(HttpRequest http, Sessions sessions, CancellationToken ct) =>
        await sessions.GetAsync(http, ct) is not null ? OwnerAuth.SignedIn : http.Headers[OwnerHeader].FirstOrDefault();
}
