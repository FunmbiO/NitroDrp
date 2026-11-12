using NitroDrp.Api.Auth;

namespace NitroDrp.Tests;

/// <summary>The contract IPasskeyStore must keep. (The DynamoDB version is exercised by the end-to-end passkey run.)</summary>
public class PasskeyStoreTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private static Passkey Key(string id, string label = "Phone", int day = 0) => new(id, [1, 2, 3], 0, label, T0.AddDays(day), null);

    [Fact]
    public async Task Added_keys_come_back_oldest_first()
    {
        var s = new InMemoryPasskeyStore();
        await s.AddAsync(Key("b", "Laptop", 1), default);
        await s.AddAsync(Key("a", "Phone", 0), default);
        Assert.Equal(["Phone", "Laptop"], (await s.ListAsync(default)).Select(k => k.Label));
        Assert.Equal("Phone", (await s.GetAsync("a", default))!.Label);
        Assert.Null(await s.GetAsync("missing", default));
    }

    [Fact]
    public async Task The_same_credential_cannot_be_added_twice()
    {
        var s = new InMemoryPasskeyStore();
        await s.AddAsync(Key("a"), default);
        await Assert.ThrowsAnyAsync<Exception>(() => s.AddAsync(Key("a"), default));
    }

    [Fact]
    public async Task Touch_records_the_counter_and_time_but_never_brings_back_a_removed_key()
    {
        var s = new InMemoryPasskeyStore();
        await s.AddAsync(Key("a"), default);
        await s.TouchAsync("a", 7, T0.AddHours(1), default);
        var k = (await s.GetAsync("a", default))!;
        Assert.Equal(7u, k.SignCount);
        Assert.Equal(T0.AddHours(1), k.LastUsedAt);

        Assert.True(await s.RemoveAsync("a", default));
        await s.TouchAsync("a", 9, T0.AddHours(2), default);
        Assert.Null(await s.GetAsync("a", default));
        Assert.False(await s.RemoveAsync("a", default));
    }
}
