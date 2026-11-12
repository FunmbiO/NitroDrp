using NitroDrp.Api.Domain;

namespace NitroDrp.Tests;

/// <summary>The contract TryReserveEmailsAsync must keep. (The DynamoDB version is exercised end to end by dev/smoke.py.)</summary>
public class EmailCapTests
{
    private static async Task<InMemoryShareStore> StoreWith(string token)
    {
        var store = new InMemoryShareStore();
        await store.CreateAsync(new Share
        {
            Token = token, SenderName = "x", CreatedAt = DateTimeOffset.UtcNow, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), OwnerKeyHash = "h",
        }, default);
        return store;
    }

    [Fact]
    public async Task Reservations_add_up_to_the_cap_and_no_further()
    {
        var store = await StoreWith("t");
        Assert.True(await store.TryReserveEmailsAsync("t", 15, 20, default));
        Assert.True(await store.TryReserveEmailsAsync("t", 5, 20, default));
        Assert.False(await store.TryReserveEmailsAsync("t", 1, 20, default));
        Assert.Equal(20, store.Shares["t"].EmailsSent);
    }

    [Fact]
    public async Task A_refused_reservation_changes_nothing()
    {
        var store = await StoreWith("t");
        Assert.True(await store.TryReserveEmailsAsync("t", 18, 20, default));
        Assert.False(await store.TryReserveEmailsAsync("t", 5, 20, default)); // would be 23
        Assert.Equal(18, store.Shares["t"].EmailsSent);
        Assert.True(await store.TryReserveEmailsAsync("t", 2, 20, default)); // the remaining room still works
    }

    [Fact]
    public async Task Concurrent_requests_cannot_exceed_the_cap()
    {
        var store = await StoreWith("t");
        var results = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => store.TryReserveEmailsAsync("t", 1, 20, default)));
        Assert.Equal(20, results.Count(r => r));
        Assert.Equal(20, store.Shares["t"].EmailsSent);
    }

    [Fact]
    public async Task An_unknown_share_reserves_nothing()
    {
        Assert.False(await (await StoreWith("t")).TryReserveEmailsAsync("nope", 1, 20, default));
    }
}
