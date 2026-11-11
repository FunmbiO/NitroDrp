namespace NitroDrp.Api.Services;

/// <summary>Caps simultaneous zip streams. Each one holds a connection and pulls its bytes through this server.</summary>
public sealed class ZipGate(int max)
{
    private readonly SemaphoreSlim _slots = new(max, max);
    public bool TryEnter() => _slots.Wait(0);
    public void Exit() => _slots.Release();
}
