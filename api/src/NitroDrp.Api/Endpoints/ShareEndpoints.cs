using Microsoft.AspNetCore.Mvc;
using NitroDrp.Api.Services;

namespace NitroDrp.Api.Endpoints;

public static class ShareEndpoints
{
    private const string OwnerHeader = "X-Owner-Key";
    private const string ApiKeyHeader = "X-Api-Key";

    public static void MapShareEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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

        shares.MapPost("/{token}/files/{fileId}/presign", async (
            string token, string fileId, [FromBody] PresignRequest body, HttpRequest http, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.PresignAsync(token, Owner(http), fileId, body, ct))).RequireRateLimiting("upload");

        shares.MapPost("/{token}/files/{fileId}/complete", async (
            string token, string fileId, [FromBody] CompleteFileRequest body, HttpRequest http, ShareService svc, CancellationToken ct) =>
        {
            await svc.CompleteFileAsync(token, Owner(http), fileId, body, ct);
            return Results.NoContent();
        }).RequireRateLimiting("upload");

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

        shares.MapPost("/{token}/download", async (string token, [FromBody] DownloadRequest body, ShareService svc, CancellationToken ct) =>
            Results.Ok(await svc.DownloadAsync(token, body, ct))).RequireRateLimiting("password");
    }

    private static string? Owner(HttpRequest http) => http.Headers[OwnerHeader].FirstOrDefault();
}
