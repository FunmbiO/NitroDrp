namespace NitroDrp.Api.Auth;

/// <summary>One enrolled device. A person can have several (phone, laptop, a hardware key).</summary>
public sealed record Passkey(
    string Id, // the credential id, base64url
    byte[] PublicKey,
    uint SignCount,
    string Label,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastUsedAt);

/// <summary>Where passkeys are kept. DynamoDB in production, in memory in tests.</summary>
public interface IPasskeyStore
{
    Task AddAsync(Passkey key, CancellationToken ct);
    Task<Passkey?> GetAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<Passkey>> ListAsync(CancellationToken ct);
    Task<bool> RemoveAsync(string id, CancellationToken ct);

    /// <summary>Records a successful sign-in: the authenticator's new signature counter and when it was used.</summary>
    Task TouchAsync(string id, uint signCount, DateTimeOffset now, CancellationToken ct);
}
