using Chevrere.Modules.Identity.Application.Abstractions;
using Chevrere.Modules.Identity.Application.Contracts;
using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Tenancy.Application.Abstractions;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.Modules.Tenancy.Domain;
using Chevrere.Modules.Tenancy.Domain.ValueObjects;
using Chevrere.SharedKernel.Application;
using Chevrere.SharedKernel.Audit;
using Chevrere.SharedKernel.Domain;
using Chevrere.SharedKernel.Persistence;
using Chevrere.SharedKernel.Results;
using Chevrere.SharedKernel.Time;

namespace Chevrere.Modules.Tenancy.Application.Commands.CreateFranchisee;

public sealed class CreateFranchiseeHandler(
    ITenancyStore store,
    IIdentityProvisioning identity,
    ISubscriptionProvisioning subscriptions,
    IAuditRecorder audit,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IHandler<CreateFranchiseeRequest, Result<CreateFranchiseeResponse>>
{
    public async Task<Result<CreateFranchiseeResponse>> HandleAsync(
        CreateFranchiseeRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenantCode = TenantCode.Create(request.TenantCode);
            var franchiseeCode = TenantCode.Create(
                string.IsNullOrWhiteSpace(request.FranchiseeCode) ? request.TenantCode : request.FranchiseeCode);
            var storeCode = TenantCode.Create(request.StoreCode);
            var identification = Identification.Create(request.IdentificationType, request.IdentificationNumber);
            var email = EmailAddress.Create(request.Email);
            var phone = PhoneNumber.Create(request.Phone);
            var location = GeoCoordinate.Create(request.Latitude, request.Longitude);

            if (await store.TenantCodeExistsAsync(tenantCode.Value, cancellationToken))
            {
                return Result.Failure<CreateFranchiseeResponse>(
                    Error.Conflict("tenant.code.duplicate", "The tenant code is already in use."));
            }

            if (await store.FranchiseeCodeExistsAsync(franchiseeCode.Value, cancellationToken))
            {
                return Result.Failure<CreateFranchiseeResponse>(
                    Error.Conflict("franchisee.code.duplicate", "The franchisee code is already in use."));
            }

            if (await store.IdentificationExistsAsync(identification.Number, cancellationToken))
            {
                return Result.Failure<CreateFranchiseeResponse>(
                    Error.Conflict("franchisee.identification.duplicate", "The identification number is already in use."));
            }

            if (await identity.EmailExistsAsync(request.OwnerEmail, cancellationToken))
            {
                return Result.Failure<CreateFranchiseeResponse>(
                    Error.Conflict("owner.email.duplicate", "The owner email is already in use."));
            }

            var now = clock.UtcNow;
            var tenant = Tenant.Create(tenantCode, request.TenantName, now);
            var franchisee = Franchisee.Create(
                tenant,
                franchiseeCode,
                request.LegalName,
                request.TradeName,
                identification,
                email,
                phone,
                now);

            if (await store.StoreCodeExistsAsync(tenant.Id, storeCode.Value, cancellationToken))
            {
                return Result.Failure<CreateFranchiseeResponse>(
                    Error.Conflict("store.code.duplicate", "The store code is already in use for this tenant."));
            }

            var darkStore = Store.Create(
                tenant,
                franchisee,
                storeCode,
                request.StoreName,
                request.AddressInternal,
                location,
                now);

            store.AddTenant(tenant);
            store.AddFranchisee(franchisee);
            store.AddStore(darkStore);

            var ownerResult = await identity.AddOwnerAsync(
                new OwnerUserRequest(tenant.Id, request.OwnerEmail, request.OwnerDisplayName, request.OwnerPassword),
                cancellationToken);

            if (ownerResult.IsFailure)
            {
                return Result.Failure<CreateFranchiseeResponse>(ownerResult.Error!);
            }

            var subscriptionResult = await subscriptions.StartAsync(tenant.Id, request.PlanCode, cancellationToken);
            if (subscriptionResult.IsFailure)
            {
                return Result.Failure<CreateFranchiseeResponse>(subscriptionResult.Error!);
            }

            audit.Record(AuditActions.TenantCreated, nameof(Tenant), tenant.Id, tenant.Id, newValue: new { tenant.Code, tenant.Name });
            audit.Record(AuditActions.FranchiseeCreated, nameof(Franchisee), franchisee.Id, tenant.Id, newValue: new { franchisee.Code, franchisee.LegalName, franchisee.IdentificationNumber });
            audit.Record(AuditActions.StoreCreated, nameof(Store), darkStore.Id, tenant.Id, newValue: new { darkStore.Code, darkStore.Name });
            audit.Record(AuditActions.OwnerCreated, "User", ownerResult.Value.UserId, tenant.Id, newValue: new { ownerResult.Value.Email });
            audit.Record(
                AuditActions.SubscriptionCreated,
                "Subscription",
                subscriptionResult.Value.Id,
                tenant.Id,
                newValue: new { PlanCode = request.PlanCode, subscriptionResult.Value.Status });

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success(new CreateFranchiseeResponse(
                tenant.Id,
                franchisee.Id,
                darkStore.Id,
                ownerResult.Value.UserId,
                subscriptionResult.Value.Id));
        }
        catch (DomainException ex)
        {
            return Result.Failure<CreateFranchiseeResponse>(Error.Domain(ex.Code, ex.Message));
        }
    }
}
