using System.Globalization;
using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime;
using NitroDrp.Api.Domain;

namespace NitroDrp.Api.Storage;

/// <summary>
/// Single-table layout: pk = share token; sk = "META" for the share, "FILE#{id}" for each file.
/// The "ttl" attribute (epoch seconds) lets DynamoDB expire rows on its own.
/// </summary>
public sealed class DynamoShareStore(IAmazonDynamoDB db, DynamoOptions options) : IShareStore
{
    private const string Meta = "META";
    private const string FilePrefix = "FILE#";
    private static readonly TimeSpan TtlGrace = TimeSpan.FromDays(1);
    private readonly string _table = options.Table;

    public static IAmazonDynamoDB CreateClient(DynamoOptions o)
    {
        var cfg = new AmazonDynamoDBConfig();
        if (!string.IsNullOrWhiteSpace(o.ServiceUrl))
        {
            cfg.ServiceURL = o.ServiceUrl;
            cfg.AuthenticationRegion = o.Region;
        }
        else
        {
            cfg.RegionEndpoint = RegionEndpoint.GetBySystemName(o.Region);
        }
        return !string.IsNullOrEmpty(o.AccessKey)
            ? new AmazonDynamoDBClient(new BasicAWSCredentials(o.AccessKey, o.SecretKey), cfg)
            : new AmazonDynamoDBClient(cfg);
    }

    public async Task CreateAsync(Share s, CancellationToken ct)
    {
        await db.PutItemAsync(new PutItemRequest
        {
            TableName = _table,
            Item = ToMetaItem(s),
            ConditionExpression = "attribute_not_exists(pk)",
        }, ct);

        var writes = s.Files.Select(f => new WriteRequest(new PutRequest(ToFileItem(s, f)))).ToList();
        foreach (var chunk in writes.Chunk(25)) await BatchWriteAsync(chunk.ToList(), ct);
    }

    public async Task<Share?> GetAsync(string token, CancellationToken ct)
    {
        var resp = await db.QueryAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "pk = :pk",
            ExpressionAttributeValues = new() { [":pk"] = S(token) },
            ConsistentRead = true,
        }, ct);

        var meta = resp.Items.FirstOrDefault(i => i["sk"].S == Meta);
        if (meta is null) return null;

        return new Share
        {
            Token = token,
            SenderName = meta["senderName"].S,
            Status = Enum.Parse<ShareStatus>(meta["status"].S),
            CreatedAt = DateTimeOffset.FromUnixTimeSeconds(Num(meta, "createdAt")),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(Num(meta, "expiresAt")),
            PasswordHash = meta.TryGetValue("passwordHash", out var ph) ? ph.S : null,
            DownloadLimit = (int)Num(meta, "downloadLimit"),
            DownloadCount = (int)Num(meta, "downloadCount"),
            OwnerKeyHash = meta["ownerKeyHash"].S,
            Files = resp.Items
                .Where(i => i["sk"].S.StartsWith(FilePrefix, StringComparison.Ordinal))
                .Select(FromFileItem)
                .OrderBy(f => f.Order)
                .ToList(),
        };
    }

    public Task UpdateFileAsync(string token, ShareFile f, CancellationToken ct)
    {
        var set = new List<string> { "#st = :st" };
        var remove = new List<string>();
        var values = new Dictionary<string, AttributeValue> { [":st"] = S(f.Status.ToString()) };
        if (f.UploadId is null) remove.Add("uploadId");
        else { set.Add("uploadId = :uid"); values[":uid"] = S(f.UploadId); }

        var expr = "SET " + string.Join(", ", set) + (remove.Count > 0 ? " REMOVE " + string.Join(", ", remove) : "");
        return db.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _table,
            Key = Key(token, FilePrefix + f.Id),
            UpdateExpression = expr,
            ConditionExpression = "attribute_exists(pk)",
            ExpressionAttributeNames = new() { ["#st"] = "status" },
            ExpressionAttributeValues = values,
        }, ct);
    }

    public Task ActivateAsync(string token, CancellationToken ct) =>
        db.UpdateItemAsync(new UpdateItemRequest
        {
            TableName = _table,
            Key = Key(token, Meta),
            UpdateExpression = "SET #st = :active",
            ConditionExpression = "attribute_exists(pk)",
            ExpressionAttributeNames = new() { ["#st"] = "status" },
            ExpressionAttributeValues = new() { [":active"] = S(nameof(ShareStatus.Active)) },
        }, ct);

    public async Task<bool> TryRegisterDownloadAsync(string token, DateTimeOffset now, CancellationToken ct)
    {
        try
        {
            await db.UpdateItemAsync(new UpdateItemRequest
            {
                TableName = _table,
                Key = Key(token, Meta),
                UpdateExpression = "ADD downloadCount :one",
                ConditionExpression =
                    "#st = :active AND expiresAt > :now AND (downloadLimit = :zero OR downloadCount < downloadLimit)",
                ExpressionAttributeNames = new() { ["#st"] = "status" },
                ExpressionAttributeValues = new()
                {
                    [":one"] = N(1),
                    [":zero"] = N(0),
                    [":now"] = N(now.ToUnixTimeSeconds()),
                    [":active"] = S(nameof(ShareStatus.Active)),
                },
            }, ct);
            return true;
        }
        catch (ConditionalCheckFailedException)
        {
            return false;
        }
    }

    public async Task DeleteAsync(string token, CancellationToken ct)
    {
        var resp = await db.QueryAsync(new QueryRequest
        {
            TableName = _table,
            KeyConditionExpression = "pk = :pk",
            ProjectionExpression = "pk, sk",
            ExpressionAttributeValues = new() { [":pk"] = S(token) },
            ConsistentRead = true,
        }, ct);

        var deletes = resp.Items.Select(i => new WriteRequest(new DeleteRequest(Key(token, i["sk"].S)))).ToList();
        foreach (var chunk in deletes.Chunk(25)) await BatchWriteAsync(chunk.ToList(), ct);
    }

    public async Task EnsureTableAsync(CancellationToken ct)
    {
        try
        {
            await db.DescribeTableAsync(_table, ct);
            return;
        }
        catch (ResourceNotFoundException) { }

        await db.CreateTableAsync(new CreateTableRequest
        {
            TableName = _table,
            BillingMode = BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions =
            [
                new AttributeDefinition("pk", ScalarAttributeType.S),
                new AttributeDefinition("sk", ScalarAttributeType.S),
            ],
            KeySchema =
            [
                new KeySchemaElement("pk", KeyType.HASH),
                new KeySchemaElement("sk", KeyType.RANGE),
            ],
        }, ct);

        try
        {
            await db.UpdateTimeToLiveAsync(new UpdateTimeToLiveRequest
            {
                TableName = _table,
                TimeToLiveSpecification = new TimeToLiveSpecification { AttributeName = "ttl", Enabled = true },
            }, ct);
        }
        catch (AmazonDynamoDBException) { /* DynamoDB Local may not implement TTL */ }
    }

    private async Task BatchWriteAsync(List<WriteRequest> chunk, CancellationToken ct)
    {
        var pending = new Dictionary<string, List<WriteRequest>> { [_table] = chunk };
        for (var attempt = 0; pending.Count > 0; attempt++)
        {
            if (attempt > 6) throw new InvalidOperationException("DynamoDB batch write kept returning unprocessed items.");
            var r = await db.BatchWriteItemAsync(new BatchWriteItemRequest { RequestItems = pending }, ct);
            pending = r.UnprocessedItems ?? [];
            if (pending.Count > 0) await Task.Delay(50 << attempt, ct);
        }
    }

    private static Dictionary<string, AttributeValue> ToMetaItem(Share s)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = S(s.Token),
            ["sk"] = S(Meta),
            ["senderName"] = S(s.SenderName),
            ["status"] = S(s.Status.ToString()),
            ["createdAt"] = N(s.CreatedAt.ToUnixTimeSeconds()),
            ["expiresAt"] = N(s.ExpiresAt.ToUnixTimeSeconds()),
            ["ttl"] = N((s.ExpiresAt + TtlGrace).ToUnixTimeSeconds()),
            ["downloadLimit"] = N(s.DownloadLimit),
            ["downloadCount"] = N(s.DownloadCount),
            ["ownerKeyHash"] = S(s.OwnerKeyHash),
        };
        if (s.PasswordHash is not null) item["passwordHash"] = S(s.PasswordHash);
        return item;
    }

    private static Dictionary<string, AttributeValue> ToFileItem(Share s, ShareFile f)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["pk"] = S(s.Token),
            ["sk"] = S(FilePrefix + f.Id),
            ["id"] = S(f.Id),
            ["ord"] = N(f.Order),
            ["name"] = S(f.Name),
            ["size"] = N(f.Size),
            ["contentType"] = S(f.ContentType),
            ["storageKey"] = S(f.StorageKey),
            ["status"] = S(f.Status.ToString()),
            ["ttl"] = N((s.ExpiresAt + TtlGrace).ToUnixTimeSeconds()),
        };
        if (f.UploadId is not null) item["uploadId"] = S(f.UploadId);
        return item;
    }

    private static ShareFile FromFileItem(Dictionary<string, AttributeValue> i) => new()
    {
        Id = i["id"].S,
        Order = (int)Num(i, "ord"),
        Name = i["name"].S,
        Size = Num(i, "size"),
        ContentType = i["contentType"].S,
        StorageKey = i["storageKey"].S,
        Status = Enum.Parse<FileStatus>(i["status"].S),
        UploadId = i.TryGetValue("uploadId", out var u) ? u.S : null,
    };

    private static Dictionary<string, AttributeValue> Key(string token, string sk) =>
        new() { ["pk"] = S(token), ["sk"] = S(sk) };

    private static AttributeValue S(string v) => new() { S = v };
    private static AttributeValue N(long v) => new() { N = v.ToString(CultureInfo.InvariantCulture) };
    private static long Num(Dictionary<string, AttributeValue> i, string k) => long.Parse(i[k].N, CultureInfo.InvariantCulture);
}
