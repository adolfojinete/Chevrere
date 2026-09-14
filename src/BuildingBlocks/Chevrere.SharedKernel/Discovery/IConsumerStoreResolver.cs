namespace Chevrere.SharedKernel.Discovery;

/// <summary>
/// Internal fulfillment decision. It is never serialized: leaking it would tell a consumer which dark
/// store and which tenant is behind the single Chevrere brand.
/// </summary>
public sealed record ConsumerFulfillmentStore(Guid StoreId, Guid TenantId, Guid ServiceAreaId);

/// <summary>
/// Picks the one store that serves a coordinate. Coverage, catalog, cart and order placement all go
/// through it, so those surfaces can never disagree about who would deliver.
/// </summary>
/// <remarks>
/// Shared by Consumer and Orders. Coordinates stay as latitude/longitude here so SharedKernel does
/// not depend on Consumer.Domain; each Application layer validates them (Consumer via
/// <c>GeoCoordinate</c>) before calling.
/// </remarks>
public interface IConsumerStoreResolver
{
    Task<ConsumerFulfillmentStore?> ResolveEligibleStoreAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken);
}
