using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using NitroDrp.Api.Notifications;

namespace NitroDrp.Api.Storage;

/// <summary>
/// Same table as everything else. pk "NOTIFY": events (sk "E#{time}#{id}", expiring after the retention period), the
/// settings row (sk "SETTINGS") and one marker per link already warned about expiry (sk "X#{token}"). pk "PUSH": one row
/// per subscribed device (sk "S#{id}").
/// </summary>
public sealed class DynamoNotificationStore(IAmazonDynamoDB db, DynamoOptions options, int retentionDays) : INotificationStore
{
    private const string Feed = "NOTIFY";
    private const string Devices = "PUSH";
    private const string Settings = "SETTINGS";
    private readonly string _table = options.Table;

    private static string EventSk(NotificationEvent e) => $"E#{e.At.ToUnixTimeMilliseconds():D13}#{e.Id}";
    private static Dictionary<string, AttributeValue> Key(string pk, string sk) => new() { ["pk"] = S(pk), ["sk"] = S(sk) };
    private static AttributeValue S(string v) => new() { S = v };
    private static AttributeValue N(long v) => new() { N = v.ToString(CultureInfo.InvariantCulture) };
    private static long Num(Dictionary<string, AttributeValue> i, string k) => long.Parse(i[k].N, CultureInfo.InvariantCulture);

    public async Task AddEventAsync(NotificationEvent e, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = S(Feed),
            ["sk"] = S(EventSk(e)),
            ["id"] = S(e.Id),
            ["kind"] = S(e.Kind),
            ["count"] = N(e.Count),
            ["at"] = N(e.At.ToUnixTimeMilliseconds()),
            ["ttl"] = N(e.At.AddDays(retentionDays).ToUnixTimeSeconds()),
        };
        if (e.Token is not null) item["token"] = S(e.Token);
        await db.PutItemAsync(new PutItemRequest { TableName = _table, Item = item }, ct);
    }

    private static NotificationEvent EventFrom(Dictionary<string, AttributeValue> i) => new(
        i["id"].S, i["kind"].S, i.TryGetValue("token", out var t) ? t.S : null, (int)Num(i, "count"),
        DateTimeOffset.FromUnixTimeMilliseconds(Num(i, "at")));

    public async Task<IReadOnlyList<NotificationEvent>> ListEventsAsync(int limit, CancellationToken ct)
    {
        var r = await db.QueryAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "pk = :pk AND begins_with(sk, :p)",
            ExpressionAttributeValues = new() { [":pk"] = S(Feed), [":p"] = S("E#") },
            ScanIndexForward = false,
            Limit = limit,
        }, ct);
        return r.Items.Select(EventFrom).ToList();
    }

    public async Task<IReadOnlyList<NotificationEvent>> ListEventsSinceAsync(DateTimeOffset since, CancellationToken ct)
    {
        var all = new List<NotificationEvent>();
        Dictionary<string, AttributeValue>? start = null;
        do
        {
            var r = await db.QueryAsync(new QueryRequest
            {
                TableName = _table,
                KeyConditionExpression = "pk = :pk AND sk BETWEEN :a AND :b",
                ExpressionAttributeValues = new() { [":pk"] = S(Feed), [":a"] = S($"E#{since.ToUnixTimeMilliseconds():D13}"), [":b"] = S("E#9999999999999~") },
                ExclusiveStartKey = start,
            }, ct);
            all.AddRange(r.Items.Select(EventFrom));
            start = r.LastEvaluatedKey is { Count: > 0 } ? r.LastEvaluatedKey : null;
        } while (start is not null);
        return all;
    }

    public async Task ClearEventsAsync(CancellationToken ct)
    {
        Dictionary<string, AttributeValue>? start = null;
        do
        {
            var r = await db.QueryAsync(new QueryRequest
            {
                TableName = _table,
                KeyConditionExpression = "pk = :pk AND begins_with(sk, :p)",
                ExpressionAttributeValues = new() { [":pk"] = S(Feed), [":p"] = S("E#") },
                ProjectionExpression = "pk, sk",
                ExclusiveStartKey = start,
            }, ct);
            foreach (var chunk in r.Items.Chunk(25))
                await db.BatchWriteItemAsync(new BatchWriteItemRequest
                {
                    RequestItems = new() { [_table] = chunk.Select(i => new WriteRequest(new DeleteRequest(Key(i["pk"].S, i["sk"].S)))).ToList() },
                }, ct);
            start = r.LastEvaluatedKey is { Count: > 0 } ? r.LastEvaluatedKey : null;
        } while (start is not null);
    }

    public async Task<NotificationSettings> GetSettingsAsync(CancellationToken ct)
    {
        var r = await db.GetItemAsync(new GetItemRequest { TableName = _table, Key = Key(Feed, Settings), ConsistentRead = true }, ct);
        if (r.Item is not { Count: > 0 } i) return NotificationSettings.Default;
        return new NotificationSettings(
            i.TryGetValue("lastReadAt", out var lr) ? DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(lr.N, CultureInfo.InvariantCulture)) : null,
            i.TryGetValue("digestEnabled", out var de) && de.BOOL,
            i.TryGetValue("lastDigestDate", out var ld) ? ld.S : null);
    }

    public Task SetLastReadAsync(DateTimeOffset at, CancellationToken ct) => Update("SET lastReadAt = :v", new() { [":v"] = N(at.ToUnixTimeMilliseconds()) }, ct);
    public Task SetDigestEnabledAsync(bool enabled, CancellationToken ct) => Update("SET digestEnabled = :v", new() { [":v"] = new AttributeValue { BOOL = enabled } }, ct);

    private Task Update(string expression, Dictionary<string, AttributeValue> values, CancellationToken ct) =>
        db.UpdateItemAsync(new UpdateItemRequest { TableName = _table, Key = Key(Feed, Settings), UpdateExpression = expression, ExpressionAttributeValues = values }, ct);

    public async Task<bool> TryClaimDigestAsync(string date, CancellationToken ct)
    {
        try
        {
            await db.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _table,
                Key = Key(Feed, Settings),
                UpdateExpression = "SET lastDigestDate = :d",
                ConditionExpression = "attribute_not_exists(lastDigestDate) OR lastDigestDate <> :d",
                ExpressionAttributeValues = new() { [":d"] = S(date) },
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task<bool> TryClaimExpiryNoticeAsync(string token, DateTimeOffset forgetAfter, CancellationToken ct)
    {
        try
        {
            await db.PutItemAsync(new PutItemRequest
            {
                TableName = _table,
                Item = new() { ["pk"] = S(Feed), ["sk"] = S($"X#{token}"), ["ttl"] = N(forgetAfter.ToUnixTimeSeconds()) },
                ConditionExpression = "attribute_not_exists(pk)",
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task AddSubscriptionAsync(PushSubscriptionRecord s, CancellationToken ct)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = S(Devices),
            ["sk"] = S($"S#{s.Id}"),
            ["endpoint"] = S(s.Endpoint),
            ["p256dh"] = S(s.P256dh),
            ["auth"] = S(s.Auth),
            ["label"] = S(s.Label),
            ["kinds"] = S(string.Join(',', s.Kinds)),
            ["createdAt"] = N(s.CreatedAt.ToUnixTimeSeconds()),
        };
        if (s.LastSentAt is { } sent) item["lastSentAt"] = N(sent.ToUnixTimeSeconds());
        await db.PutItemAsync(new PutItemRequest { TableName = _table, Item = item }, ct);
    }

    public async Task<IReadOnlyList<PushSubscriptionRecord>> ListSubscriptionsAsync(CancellationToken ct)
    {
        var r = await db.QueryAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "pk = :pk AND begins_with(sk, :p)",
            ExpressionAttributeValues = new() { [":pk"] = S(Devices), [":p"] = S("S#") },
            ConsistentRead = true,
        }, ct);
        return r.Items.Select(i => new PushSubscriptionRecord(
            i["sk"].S["S#".Length..], i["endpoint"].S, i["p256dh"].S, i["auth"].S, i["label"].S,
            i["kinds"].S.Length == 0 ? [] : i["kinds"].S.Split(','),
            DateTimeOffset.FromUnixTimeSeconds(Num(i, "createdAt")),
            i.TryGetValue("lastSentAt", out var ls) ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(ls.N, CultureInfo.InvariantCulture)) : null))
            .OrderBy(s => s.CreatedAt).ToList();
    }

    public async Task<bool> RemoveSubscriptionAsync(string id, CancellationToken ct)
    {
        var r = await db.DeleteItemAsync(new DeleteItemRequest { TableName = _table, Key = Key(Devices, $"S#{id}"), ReturnValues = ReturnValue.ALL_OLD }, ct);
        return r.Attributes is { Count: > 0 };
    }

    public async Task<bool> UpdateSubscriptionKindsAsync(string id, IReadOnlyList<string> kinds, CancellationToken ct)
    {
        try
        {
            await db.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _table,
                Key = Key(Devices, $"S#{id}"),
                UpdateExpression = "SET kinds = :k",
                ConditionExpression = "attribute_exists(pk)",
                ExpressionAttributeValues = new() { [":k"] = S(string.Join(',', kinds)) },
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task TouchSubscriptionAsync(string id, DateTimeOffset at, CancellationToken ct)
    {
        try
        {
            await db.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _table,
                Key = Key(Devices, $"S#{id}"),
                UpdateExpression = "SET lastSentAt = :t",
                ConditionExpression = "attribute_exists(pk)", // never bring back a device that was removed meanwhile
                ExpressionAttributeValues = new() { [":t"] = N(at.ToUnixTimeSeconds()) },
            }, ct);
        }
        catch (ConditionalCheckFailedException) { }
    }
}
