using System.Net;
using Amazon.CloudFront;
using Amazon.Runtime;
using NitroDrp.Api.Storage;

namespace NitroDrp.Tests;

public class CloudFrontEdgeCacheTests
{
    private const string Ok = """
        <?xml version="1.0"?><Invalidation xmlns="http://cloudfront.amazonaws.com/doc/2020-05-31/"><Id>I1</Id><Status>InProgress</Status>
        <CreateTime>2026-11-11T12:00:00Z</CreateTime><InvalidationBatch><Paths><Quantity>1</Quantity><Items><Path>/x</Path></Items></Paths>
        <CallerReference>r</CallerReference></InvalidationBatch></Invalidation>
        """;

    [Fact]
    public async Task Sends_one_invalidation_for_the_given_paths_to_the_right_distribution()
    {
        var port = Random.Shared.Next(20000, 40000);
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        string? method = null, path = null, body = null;
        var served = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            method = ctx.Request.HttpMethod;
            path = ctx.Request.Url!.AbsolutePath;
            body = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync();
            var bytes = System.Text.Encoding.UTF8.GetBytes(Ok);
            ctx.Response.StatusCode = 201;
            ctx.Response.ContentType = "text/xml";
            await ctx.Response.OutputStream.WriteAsync(bytes);
            ctx.Response.Close();
        });

        var client = new AmazonCloudFrontClient(new BasicAWSCredentials("a", "b"),
            new AmazonCloudFrontConfig { ServiceURL = $"http://127.0.0.1:{port}", AuthenticationRegion = "us-east-1" });
        await new CloudFrontEdgeCache(client, "EDIST123").InvalidateAsync(["/d7/abc/*", "/d1/def/*"], default);
        await served;

        Assert.Equal("POST", method);
        Assert.Contains("/distribution/EDIST123/invalidation", path);
        Assert.Contains("<Quantity>2</Quantity>", body);
        Assert.Contains("<Path>/d7/abc/*</Path>", body);
        Assert.Contains("<Path>/d1/def/*</Path>", body);
        Assert.Contains("<CallerReference>", body);
    }
}
