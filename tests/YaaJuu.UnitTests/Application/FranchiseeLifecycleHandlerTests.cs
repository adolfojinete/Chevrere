using YaaJuu.Modules.Subscriptions.Application.Abstractions;
using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.Modules.Tenancy.Application.Abstractions;
using YaaJuu.Modules.Tenancy.Application.Commands.ActivateFranchisee;
using YaaJuu.Modules.Tenancy.Application.Commands.ReactivateFranchisee;
using YaaJuu.Modules.Tenancy.Application.Commands.SuspendFranchisee;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Time;
using NSubstitute;

namespace YaaJuu.UnitTests.Application;

public sealed class FranchiseeLifecycleHandlerTests
{
    private readonly ITenancyStore _store = Substitute.For<ITenancyStore>();
    private readonly ISubscriptionProvisioning _subscriptions = Substitute.For<ISubscriptionProvisioning>();
    private readonly IAuditRecorder _audit = Substitute.For<IAuditRecorder>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public FranchiseeLifecycleHandlerTests() => _clock.UtcNow.Returns(FixedClock.Now);

    [Fact]
    public async Task Activate_returns_not_found_when_franchisee_missing()
    {
        var handler = new ActivateFranchiseeHandler(_store, _audit, _unitOfWork, _clock);
        var result = await handler.HandleAsync(new ActivateFranchiseeCommand(Guid.CreateVersion7()), CancellationToken.None);
        Assert.Equal("not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Activate_succeeds_for_pending_franchisee()
    {
        var tenant = TestEntities.Tenant();
        var franchisee = TestEntities.Franchisee(tenant);
        var store = TestEntities.Store(tenant, franchisee);
        _store.GetFranchiseeAsync(franchisee.Id, Arg.Any<CancellationToken>()).Returns(franchisee);
        _store.GetTenantAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _store.ListStoresByFranchiseeAsync(franchisee.Id, Arg.Any<CancellationToken>()).Returns([store]);

        var handler = new ActivateFranchiseeHandler(_store, _audit, _unitOfWork, _clock);
        var result = await handler.HandleAsync(new ActivateFranchiseeCommand(franchisee.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FranchiseeStatus.Active, franchisee.Status);
        Assert.Equal(TenantStatus.Active, tenant.Status);
        Assert.Equal(StoreStatus.Active, store.Status);
    }

    [Fact]
    public async Task Activate_conflict_when_already_active()
    {
        var tenant = TestEntities.Tenant();
        var franchisee = TestEntities.Franchisee(tenant);
        franchisee.Activate(FixedClock.Now);
        _store.GetFranchiseeAsync(franchisee.Id, Arg.Any<CancellationToken>()).Returns(franchisee);
        _store.GetTenantAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _store.ListStoresByFranchiseeAsync(franchisee.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<Store>());

        var handler = new ActivateFranchiseeHandler(_store, _audit, _unitOfWork, _clock);
        var result = await handler.HandleAsync(new ActivateFranchiseeCommand(franchisee.Id), CancellationToken.None);
        Assert.Equal("franchisee.invalid_transition", result.Error!.Code);
    }

    [Fact]
    public async Task Suspend_requires_reason_and_active_status()
    {
        var handler = new SuspendFranchiseeHandler(_store, _subscriptions, _audit, _unitOfWork, _clock);
        var noReason = await handler.HandleAsync(new SuspendFranchiseeCommand(Guid.CreateVersion7(), " "), CancellationToken.None);
        Assert.Equal("suspension.reason.required", noReason.Error!.Code);

        var tenant = TestEntities.Tenant();
        var franchisee = TestEntities.Franchisee(tenant);
        franchisee.Activate(FixedClock.Now);
        tenant.Activate(FixedClock.Now);
        var subscription = TestEntities.Subscription();
        _store.GetFranchiseeAsync(franchisee.Id, Arg.Any<CancellationToken>()).Returns(franchisee);
        _store.GetTenantAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _subscriptions.GetByTenantAsync(franchisee.TenantId, Arg.Any<CancellationToken>()).Returns(subscription);

        var ok = await handler.HandleAsync(new SuspendFranchiseeCommand(franchisee.Id, "Past due"), CancellationToken.None);
        Assert.True(ok.IsSuccess);
        Assert.Equal(FranchiseeStatus.Suspended, franchisee.Status);
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
    }

    [Fact]
    public async Task Reactivate_restores_suspended_subscription()
    {
        var tenant = TestEntities.Tenant();
        var franchisee = TestEntities.Franchisee(tenant);
        franchisee.Activate(FixedClock.Now);
        franchisee.Suspend(FixedClock.Now.AddMinutes(1));
        tenant.Activate(FixedClock.Now);
        tenant.Suspend(FixedClock.Now.AddMinutes(1));
        var subscription = TestEntities.Subscription();
        subscription.Suspend(FixedClock.Now, "x");

        _store.GetFranchiseeAsync(franchisee.Id, Arg.Any<CancellationToken>()).Returns(franchisee);
        _store.GetTenantAsync(tenant.Id, Arg.Any<CancellationToken>()).Returns(tenant);
        _subscriptions.GetByTenantAsync(franchisee.TenantId, Arg.Any<CancellationToken>()).Returns(subscription);

        var handler = new ReactivateFranchiseeHandler(_store, _subscriptions, _audit, _unitOfWork, _clock);
        var result = await handler.HandleAsync(new ReactivateFranchiseeCommand(franchisee.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(FranchiseeStatus.Active, franchisee.Status);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
    }

    [Fact]
    public async Task Activate_returns_not_found_when_tenant_missing()
    {
        var franchisee = TestEntities.Franchisee();
        _store.GetFranchiseeAsync(franchisee.Id, Arg.Any<CancellationToken>()).Returns(franchisee);
        var handler = new ActivateFranchiseeHandler(_store, _audit, _unitOfWork, _clock);
        var result = await handler.HandleAsync(new ActivateFranchiseeCommand(franchisee.Id), CancellationToken.None);
        Assert.Equal("not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Suspend_and_reactivate_not_found()
    {
        var suspend = await new SuspendFranchiseeHandler(_store, _subscriptions, _audit, _unitOfWork, _clock)
            .HandleAsync(new SuspendFranchiseeCommand(Guid.CreateVersion7(), "reason"), CancellationToken.None);
        var reactivate = await new ReactivateFranchiseeHandler(_store, _subscriptions, _audit, _unitOfWork, _clock)
            .HandleAsync(new ReactivateFranchiseeCommand(Guid.CreateVersion7()), CancellationToken.None);
        Assert.Equal("not_found", suspend.Error!.Code);
        Assert.Equal("not_found", reactivate.Error!.Code);
    }
}
