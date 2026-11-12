using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using NitroDrp.Api.Auth;

namespace NitroDrp.Api.Storage;

/// <summary>
/// Same table as the shares. pk = "AUTH", sk = "CRED#{credential id}". These rows have no "ttl", so they never expire.
/// </summary>
public sealed class DynamoPasskeyStore(IAmazonDynamoDB db, DynamoOptions options) : IPasskeyStore
{
    private const string Pk = "AUTH";
    private const string Prefix = "CRED#";
    private readonly string _table = options.Table;

    public async Task AddAsync(Passkey k, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = new() { S = Pk },
            ["sk"] = new() { S = Prefix + k.Id },
            ["publicKey"] = new() { B = new MemoryStream(k.PublicKey) },
            ["signCount"] = N(k.SignCount),
            ["label"] = new() { S = k.Label },
            ["createdAt"] = N(k.CreatedAt.ToUnixTimeSeconds()),
        };
        if (k.LastUsedAt is { } used) item["lastUsedAt"] = N(used.ToUnixTimeSeconds());
        await db.PutItemAsync(new PutItemRequest { TableName = _table, Item = item, ConditionExpression = "attribute_not_exists(pk)" }, ct);
    }

    public async Task<Passkey?> GetAsync(string id, CancellationToken ct)
    {
        var r = await db.GetItemAsync(new GetItemRequest { TableName = _table, Key = Key(id), ConsistentRead = true }, ct);
        return r.Item is { Count: > 0 } ? From(r.Item) : null;
    }

    public async Task<IReadOnlyList<Passkey>> ListAsync(CancellationToken ct)
    {
        var r = await db.QueryAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "pk = :pk AND begins_with(sk, :p)",
            ExpressionAttributeValues = new() { [":pk"] = new() { S = Pk }, [":p"] = new() { S = Prefix } },
            ConsistentRead = true,
        }, ct);
        return r.Items.Select(From).OrderBy(k => k.CreatedAt).ToList();
    }

    public async Task<bool> RemoveAsync(string id, CancellationToken ct)
    {
        var r = await db.DeleteItemAsync(new DeleteItemRequest { TableName = _table, Key = Key(id), ReturnValues = ReturnValue.ALL_OLD }, ct);
        return r.Attributes is { Count: > 0 };
    }

    public async Task TouchAsync(string id, uint signCount, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await db.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _table,
                Key = Key(id),
                UpdateExpression = "SET signCount = :c, lastUsedAt = :t",
                ConditionExpression = "attribute_exists(pk)", // never resurrect a passkey that was removed meanwhile
                ExpressionAttributeValues = new() { [":c"] = N(signCount), [":t"] = N(now.ToUnixTimeSeconds()) },
            }, ct);
        }
        catch (ConditionalCheckFailedException) { }
    }

    private static Dictionary<string, AttributeValue> Key(string id) => new() { ["pk"] = new() { S = Pk }, ["sk"] = new() { S = Prefix + id } };
    private static AttributeValue N(long v) => new() { N = v.ToString(CultureInfo.InvariantCulture) };

    private static Passkey From(Dictionary<string, AttributeValue> i) => new(
        i["sk"].S[Prefix.Length..],
        i["publicKey"].B.ToArray(),
        uint.Parse(i["signCount"].N, CultureInfo.InvariantCulture),
        i["label"].S,
        DateTimeOffset.FromUnixTimeSeconds(long.Parse(i["createdAt"].N, CultureInfo.InvariantCulture)),
        i.TryGetValue("lastUsedAt", out var u) ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(u.N, CultureInfo.InvariantCulture)) : null);
}
