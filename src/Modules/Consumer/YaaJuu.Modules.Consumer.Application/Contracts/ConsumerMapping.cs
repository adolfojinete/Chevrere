using YaaJuu.Modules.Consumer.Domain;

namespace YaaJuu.Modules.Consumer.Application.Contracts;

internal static class ConsumerMapping
{
    public static AdminServiceAreaDto ToDto(StoreServiceArea area) => new(
        area.Id,
        area.StoreId,
        area.TenantId,
        area.Latitude,
        area.Longitude,
        area.ServiceRadiusMeters,
        area.IsEnabled,
        area.CreatedAt,
        area.UpdatedAt);
}
