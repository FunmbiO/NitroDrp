using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.WebUtilities;
using NitroDrp.Api.Services;

namespace NitroDrp.Api.Auth;

public sealed record BegunRegistration(string FlowId, CredentialCreateOptions Options);
public sealed record BegunLogin(string FlowId, AssertionOptions Options);

/// <summary>
/// Passkey enrolment and sign-in (WebAuthn). There is one owner, so there is one fixed user and several
/// credentials. Sign-in uses discoverable credentials: the device offers its passkey, so no username is typed.
/// </summary>
public sealed class PasskeyService(IFido2 fido2, IPasskeyStore keys, TimeProvider clock, AuthOptions options)
{
    private static readonly TimeSpan FlowLifetime = TimeSpan.FromMinutes(5);

    /// <summary>The same for every credential: this app has a single owner.</summary>
    private static readonly byte[] UserHandle = SHA256.HashData("nitrodrp-owner"u8)[..16];

    private static readonly Fido2User Owner = new() { Id = UserHandle, Name = "owner", DisplayName = "Owner" };

    // Challenges wait here between "begin" and "finish". One server process, so memory is enough; a restart just
    // means starting the prompt again. Each is single use.
    private readonly ConcurrentDictionary<string, (object Options, DateTimeOffset Expires)> _flows = new();

    public async Task<BegunRegistration> BeginRegistrationAsync(CancellationToken ct)
    {
        var existing = await keys.ListAsync(ct);
        if (existing.Count >= options.MaxPasskeys)
            throw ServiceException.Conflict("too_many_passkeys", $"You can have at most {options.MaxPasskeys} devices. Remove one first.");

        var created = fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = Owner,
            ExcludeCredentials = existing.Select(k => new PublicKeyCredentialDescriptor(WebEncoders.Base64UrlDecode(k.Id))).ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                ResidentKey = ResidentKeyRequirement.Required, // discoverable: the device remembers it, no username needed
                UserVerification = UserVerificationRequirement.Required, // Face ID, fingerprint or device PIN, always
            },
            AttestationPreference = AttestationConveyancePreference.None, // we don't need to know the device model
        });
        return new BegunRegistration(Remember(created), created);
    }

    public async Task<Passkey> FinishRegistrationAsync(string flowId, AuthenticatorAttestationRawResponse response, string? label, CancellationToken ct)
    {
        var begun = (CredentialCreateOptions?)Take(flowId) ?? throw Expired();
        var credential = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
        {
            AttestationResponse = response,
            OriginalOptions = begun,
            IsCredentialIdUniqueToUserCallback = async (args, c) => await keys.GetAsync(WebEncoders.Base64UrlEncode(args.CredentialId), c) is null,
        }, ct);

        var key = new Passkey(
            WebEncoders.Base64UrlEncode(credential.Id),
            credential.PublicKey,
            credential.SignCount,
            CleanLabel(label),
            clock.GetUtcNow(),
            null);
        await keys.AddAsync(key, ct);
        return key;
    }

    public BegunLogin BeginLogin()
    {
        var asserted = fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            AllowedCredentials = [], // empty: let the device pick any passkey it holds for this site
            UserVerification = UserVerificationRequirement.Required,
        });
        return new BegunLogin(Remember(asserted), asserted);
    }

    /// <summary>Checks the signed challenge. Returns the credential that signed in.</summary>
    public async Task<string> FinishLoginAsync(string flowId, AuthenticatorAssertionRawResponse response, CancellationToken ct)
    {
        var begun = (AssertionOptions?)Take(flowId) ?? throw Expired();
        var id = WebEncoders.Base64UrlEncode(response.RawId);
        var key = await keys.GetAsync(id, ct) ?? throw Rejected();

        VerifyAssertionResult result;
        try
        {
            result = await fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = begun,
                StoredPublicKey = key.PublicKey,
                StoredSignatureCounter = key.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(args.UserHandle is { } h && h.AsSpan().SequenceEqual(UserHandle)),
            }, ct);
        }
        catch (Fido2VerificationException)
        {
            throw Rejected();
        }

        await keys.TouchAsync(id, result.SignCount, clock.GetUtcNow(), ct);
        return id;
    }

    private string Remember(object options)
    {
        var now = clock.GetUtcNow();
        foreach (var stale in _flows.Where(f => f.Value.Expires <= now).Select(f => f.Key).ToList()) _flows.TryRemove(stale, out _);
        var id = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));
        _flows[id] = (options, now + FlowLifetime);
        return id;
    }

    private object? Take(string flowId) =>
        _flows.TryRemove(flowId ?? "", out var f) && f.Expires > clock.GetUtcNow() ? f.Options : null;

    private static ServiceException Expired() => ServiceException.BadRequest("flow_expired", "That took too long. Start again.");
    private static ServiceException Rejected() => ServiceException.Unauthorized("passkey_rejected", "That passkey wasn't accepted.");

    /// <summary>What shows in the device list: trimmed, one line, short.</summary>
    internal static string CleanLabel(string? label)
    {
        var s = new string((label ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (s.Length == 0) return "My device";
        return s.Length > 40 ? s[..40] : s;
    }
}
