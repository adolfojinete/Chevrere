using YaaJuu.Modules.Consumer.Application.Abstractions;
using YaaJuu.Modules.Consumer.Application.Contracts;
using YaaJuu.Modules.Consumer.Domain;
using YaaJuu.Modules.Consumer.Domain.ValueObjects;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;

namespace YaaJuu.Modules.Consumer.Application.Commands;

public sealed record ConfigureStoreServiceAreaCommand(Guid StoreId, ConfigureServiceAreaRequest Request);

public sealed class ConfigureStoreServiceAreaHandler(
    IStoreServiceAreaStore store,
    IConsumerStoreAccess storeAccess,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<ConfigureStoreServiceAreaCommand, Result<AdminServiceAreaDto>>
{
    public async Task<Result<AdminServiceAreaDto>> HandleAsync(
        ConfigureStoreServiceAreaCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await storeAccess.GetStoreTenantIdAsync(request.StoreId, cancellationToken) is not Guid tenantId)
        {
            return Result.Failure<AdminServiceAreaDto>(Error.NotFound(ErrorCodes.NotFound, "Store not found."));
        }

        GeoCoordinate center;
        ServiceRadius radius;
        try
        {
            center = GeoCoordinate.Create(request.Request.Latitude, request.Request.Longitude);
            radius = ServiceRadius.Create(request.Request.ServiceRadiusMeters);
        }
        catch (DomainException ex)
        {
            return Result.Failure<AdminServiceAreaDto>(Error.Domain(ex.Code, ex.Message));
        }

        var area = await store.GetByStoreAsync(request.StoreId, cancellationToken);
        if (area is null)
        {
            area = StoreServiceArea.Create(tenantId, request.StoreId, center, radius, clock.UtcNow);
            store.Upsert(area);
            audit.Record(
                AuditActions.StoreServiceAreaConfigured,
                nameof(StoreServiceArea),
                area.Id,
                area.TenantId,
                previousValue: null,
                newValue: Snapshot(area));

            await unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Success(ConsumerMapping.ToDto(area));
        }

        var previous = Snapshot(area);
        if (!area.Configure(center, radius, clock.UtcNow))
        {
            return Result.Success(ConsumerMapping.ToDto(area));
        }

        store.Upsert(area);
        audit.Record(
            AuditActions.StoreServiceAreaConfigured,
            nameof(StoreServiceArea),
            area.Id,
            area.TenantId,
            previousValue: previous,
            newValue: Snapshot(area));

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ConsumerMapping.ToDto(area));
    }

    private static object Snapshot(StoreServiceArea area) =>
        new { area.Latitude, area.Longitude, area.ServiceRadiusMeters, area.IsEnabled };
}

public sealed record EnableStoreServiceAreaCommand(Guid StoreId);

public sealed class EnableStoreServiceAreaHandler(
    IStoreServiceAreaStore store,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<EnableStoreServiceAreaCommand, Result>
{
    public async Task<Result> HandleAsync(EnableStoreServiceAreaCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var area = await store.GetByStoreAsync(request.StoreId, cancellationToken);
        if (area is null)
        {
            var missing = StoreServiceArea.NotConfigured();
            return Result.Failure(Error.NotFound(missing.Code, missing.Message));
        }

        if (!area.Enable(clock.UtcNow))
        {
            return Result.Success();
        }

        audit.Record(
            AuditActions.StoreServiceAreaEnabled,
            nameof(StoreServiceArea),
            area.Id,
            area.TenantId,
            previousValue: new { IsEnabled = false },
            newValue: new { area.IsEnabled });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record DisableStoreServiceAreaCommand(Guid StoreId);

public sealed class DisableStoreServiceAreaHandler(
    IStoreServiceAreaStore store,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<DisableStoreServiceAreaCommand, Result>
{
    public async Task<Result> HandleAsync(DisableStoreServiceAreaCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var area = await store.GetByStoreAsync(request.StoreId, cancellationToken);
        if (area is null)
        {
            var missing = StoreServiceArea.NotConfigured();
            return Result.Failure(Error.NotFound(missing.Code, missing.Message));
        }

        if (!area.Disable(clock.UtcNow))
        {
            return Result.Success();
        }

        audit.Record(
            AuditActions.StoreServiceAreaDisabled,
            nameof(StoreServiceArea),
            area.Id,
            area.TenantId,
            previousValue: new { IsEnabled = true },
            newValue: new { area.IsEnabled });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
