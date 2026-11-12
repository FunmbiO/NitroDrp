namespace NitroDrp.Api.Services;

/// <summary>
/// Who is acting on a link: someone holding its owner key, or the signed-in owner (who may act on any of their links
/// from any device). A plain string converts to the first kind.
/// </summary>
public readonly record struct OwnerAuth(string? Key, bool SignedInOwner)
{
    public static OwnerAuth SignedIn { get; } = new(null, true);
    public static implicit operator OwnerAuth(string? ownerKey) => new(ownerKey, false);
}
