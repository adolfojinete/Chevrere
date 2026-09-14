using Chevrere.Modules.Procurement.Application.Abstractions;
using Chevrere.Modules.Procurement.Application.Contracts;
using Chevrere.Modules.Procurement.Domain;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Procurement.Application.Commands;

public sealed class CreateSupplierHandler(
    IProcurementStore store,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<CreateSupplierRequest, Result<SupplierDto>>
{
    public async Task<Result<SupplierDto>> HandleAsync(
        CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.TenantId is not Guid tenantId)
        {
            return Result.Failure<SupplierDto>(
                Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;
        if (await store.SupplierCodeExistsAsync(tenantId, code, cancellationToken))
        {
            return Result.Failure<SupplierDto>(
                Error.Conflict("supplier.code.duplicate", "The supplier code is already in use."));
        }

        Supplier supplier;
        try
        {
            supplier = Supplier.Create(
                tenantId,
                request.Code,
                request.Name,
                new SupplierContactDetails(
                    request.TaxIdentification,
                    request.ContactName,
                    request.Email,
                    request.Phone,
                    request.Address,
                    request.Notes),
                clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<SupplierDto>(Error.Domain(ex.Code, ex.Message));
        }

        store.AddSupplier(supplier);
        audit.Record(
            AuditActions.SupplierCreated,
            nameof(Supplier),
            supplier.Id,
            tenantId,
            previousValue: null,
            newValue: new { supplier.Code, supplier.Name, supplier.IsActive });

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateKeyException)
        {
            return Result.Failure<SupplierDto>(
                Error.Conflict("supplier.code.duplicate", "The supplier code is already in use."));
        }

        return Result.Success(ProcurementMapping.ToDto(supplier));
    }
}

public sealed record UpdateSupplierCommand(Guid SupplierId, UpdateSupplierRequest Request);

public sealed class UpdateSupplierHandler(
    IProcurementStore store,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<UpdateSupplierCommand, Result<SupplierDto>>
{
    public async Task<Result<SupplierDto>> HandleAsync(
        UpdateSupplierCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (supplier, error) = await SupplierAccess.LoadAsync(store, currentUser, request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure<SupplierDto>(error!);
        }

        var previous = new { supplier.Name, supplier.Email, supplier.Phone };
        try
        {
            supplier.Update(
                request.Request.Name,
                new SupplierContactDetails(
                    request.Request.TaxIdentification,
                    request.Request.ContactName,
                    request.Request.Email,
                    request.Request.Phone,
                    request.Request.Address,
                    request.Request.Notes),
                clock.UtcNow);
        }
        catch (DomainException ex)
        {
            return Result.Failure<SupplierDto>(Error.Domain(ex.Code, ex.Message));
        }

        audit.Record(
            AuditActions.SupplierUpdated,
            nameof(Supplier),
            supplier.Id,
            supplier.TenantId,
            previousValue: previous,
            newValue: new { supplier.Name, supplier.Email, supplier.Phone });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(ProcurementMapping.ToDto(supplier));
    }
}

public sealed record ActivateSupplierCommand(Guid SupplierId);

public sealed class ActivateSupplierHandler(
    IProcurementStore store,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<ActivateSupplierCommand, Result>
{
    public async Task<Result> HandleAsync(ActivateSupplierCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (supplier, error) = await SupplierAccess.LoadAsync(store, currentUser, request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure(error!);
        }

        if (!supplier.Activate(clock.UtcNow))
        {
            return Result.Success();
        }

        audit.Record(
            AuditActions.SupplierActivated,
            nameof(Supplier),
            supplier.Id,
            supplier.TenantId,
            previousValue: new { IsActive = false },
            newValue: new { supplier.IsActive });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

public sealed record DeactivateSupplierCommand(Guid SupplierId);

public sealed class DeactivateSupplierHandler(
    IProcurementStore store,
    ICurrentUser currentUser,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<DeactivateSupplierCommand, Result>
{
    public async Task<Result> HandleAsync(DeactivateSupplierCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (supplier, error) = await SupplierAccess.LoadAsync(store, currentUser, request.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return Result.Failure(error!);
        }

        if (!supplier.Deactivate(clock.UtcNow))
        {
            return Result.Success();
        }

        audit.Record(
            AuditActions.SupplierDeactivated,
            nameof(Supplier),
            supplier.Id,
            supplier.TenantId,
            previousValue: new { IsActive = true },
            newValue: new { supplier.IsActive });

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal static class SupplierAccess
{
    /// <summary>
    /// Loads a supplier of the caller's tenant. A supplier of another tenant is reported as missing.
    /// </summary>
    public static async Task<(Supplier? Supplier, Error? Error)> LoadAsync(
        IProcurementStore store,
        ICurrentUser currentUser,
        Guid supplierId,
        CancellationToken cancellationToken)
    {
        if (currentUser.TenantId is not Guid tenantId)
        {
            return (null, Error.Forbidden(ErrorCodes.Forbidden, "The current user is not bound to a tenant."));
        }

        var supplier = await store.GetSupplierAsync(supplierId, cancellationToken);
        if (supplier is null || supplier.TenantId != tenantId)
        {
            return (null, Error.NotFound(ErrorCodes.NotFound, "Supplier not found."));
        }

        return (supplier, null);
    }
}
