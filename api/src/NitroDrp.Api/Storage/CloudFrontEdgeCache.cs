using Amazon.CloudFront;
using Amazon.CloudFront.Model;

namespace NitroDrp.Api.Storage;

public sealed class CloudFrontEdgeCache(IAmazonCloudFront cloudFront, string distributionId) : IEdgeCache
{
    public Task InvalidateAsync(IReadOnlyCollection<string> paths, CancellationToken ct) =>
        cloudFront.CreateInvalidationAsync(new CreateInvalidationRequest
        {
            DistributionId = distributionId,
            InvalidationBatch = new InvalidationBatch
            {
                Paths = new Paths { Quantity = paths.Count, Items = paths.ToList() },
                // Must be unique per request; a repeat of the same reference with a different batch is rejected.
                CallerReference = Guid.NewGuid().ToString("N"),
            },
        }, ct);
}
