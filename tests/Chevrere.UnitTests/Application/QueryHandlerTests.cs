using Chevrere.Modules.Subscriptions.Application.Abstractions;
using Chevrere.Modules.Subscriptions.Application.Commands;
using Chevrere.Modules.Subscriptions.Application.Contracts;
using Chevrere.Modules.Subscriptions.Application.Queries;
using Chevrere.Modules.Subscriptions.Domain;
using Chevrere.Modules.Tenancy.Application.Abstractions;
using Chevrere.Modules.Tenancy.Application.Contracts;
using Chevrere.Modules.Tenancy.Application.Queries;
using Chevrere.Modules.Tenancy.Domain;
using Chevrere.SharedKernel.Context;
using Chevrere.SharedKernel.Results;
using NSubstitute;

namespace Chevrere.UnitTests.Application;

public sealed class QueryHandlerTests
{
    [Fact]
    public async Task Get_franchisee_not_found()
    {
        var store = Substitute.For<ITenancyStore>();
        var handler = new GetFranchiseeHandler(store);
        var result = await handler.HandleAsync(new GetFranchiseeQuery(Guid.CreateVersion7()), CancellationToken.None);
        Assert.Equal("not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Get_franchisee_returns_detail()
    {
        var store = Substitute.For<ITenancyStore>();
        var detail = Detail();
        store.GetFranchiseeDetailAsync(detail.Id, Arg.Any<CancellationToken>()).Returns(detail);
        var result = await new GetFranchiseeHandler(store).HandleAsync(new GetFranchiseeQuery(detail.Id), CancellationToken.None);
        Assert.Equal(detail.Id, result.Value.Id);
    }

    [Fact]
    public async Task List_franchisees_delegates_to_store()
    {
        var store = Substitute.For<ITenancyStore>();
        var page = new PagedResult<FranchiseeListItemDto>([], 1, 20, 0);
        store.ListFranchiseesAsync(Arg.Any<FranchiseeListQuery>(), Arg.Any<CancellationToken>()).Returns(page);
        var result = await new ListFranchiseesHandler(store)
            .HandleAsync(new FranchiseeListQuery(1, 20, null, null), CancellationToken.None);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task List_stores_not_found_and_success()
    {
        var store = Substitute.For<ITenancyStore>();
        var missing = await new ListFranchiseeStoresHandler(store)
            .HandleAsync(new ListFranchiseeStoresQuery(Guid.CreateVersion7()), CancellationToken.None);
        Assert.Equal("not_found", missing.Error!.Code);

        var franchisee = TestEntities.Franchisee();
        store.GetFranchiseeAsync(franchisee.Id, Arg.Any<CancellationToken>()).Returns(franchisee);
        store.ListStoresByFranchiseeAsync(franchisee.Id, Arg.Any<CancellationToken>()).Returns([TestEntities.Store()]);
        var ok = await new ListFranchiseeStoresHandler(store)
            .HandleAsync(new ListFranchiseeStoresQuery(franchisee.Id), CancellationToken.None);
        Assert.Single(ok.Value);
    }

    [Fact]
    public async Task Business_stores_require_tenant_and_hide_foreign_ids()
    {
        var store = Substitute.For<ITenancyStore>();
        var user = Substitute.For<ICurrentUser>();
        user.TenantId.Returns((Guid?)null);
        var forbidden = await new ListBusinessStoresHandler(store, user)
            .HandleAsync(new ListBusinessStoresQuery(null), CancellationToken.None);
        Assert.Equal("forbidden", forbidden.Error!.Code);

        var tenantId = Guid.CreateVersion7();
        user.TenantId.Returns(tenantId);
        store.ListStoresForCurrentTenantAsync(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<StoreDto>());
        var missing = await new ListBusinessStoresHandler(store, user)
            .HandleAsync(new ListBusinessStoresQuery(Guid.CreateVersion7()), CancellationToken.None);
        Assert.Equal("not_found", missing.Error!.Code);

        var dto = new StoreDto(Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), "DS", "S", StoreStatus.Active, "x", null, null, FixedClock.Now);
        store.ListStoresForCurrentTenantAsync(null, Arg.Any<CancellationToken>()).Returns([dto]);
        var ok = await new ListBusinessStoresHandler(store, user)
            .HandleAsync(new ListBusinessStoresQuery(null), CancellationToken.None);
        Assert.Single(ok.Value);
    }

    [Fact]
    public async Task Audit_and_subscription_queries()
    {
        var store = Substitute.For<ITenancyStore>();
        store.ListAuditEventsAsync(null, null, Arg.Any<CancellationToken>()).Returns(Array.Empty<AuditEventDto>());
        var events = await new ListAuditEventsHandler(store).HandleAsync(new ListAuditEventsQuery(null, null), CancellationToken.None);
        Assert.Empty(events);

        var reader = Substitute.For<ISubscriptionReader>();
        var missing = await new GetSubscriptionByTenantHandler(reader)
            .HandleAsync(new GetSubscriptionByTenantQuery(Guid.CreateVersion7()), CancellationToken.None);
        Assert.Equal("not_found", missing.Error!.Code);

        var dto = new SubscriptionDto(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "STANDARD", SubscriptionStatus.Trial, FixedClock.Now, FixedClock.Now, FixedClock.Now, FixedClock.Now, null, null, null);
        reader.GetByTenantAsync(dto.TenantId, Arg.Any<CancellationToken>()).Returns(dto);
        var found = await new GetSubscriptionByTenantHandler(reader)
            .HandleAsync(new GetSubscriptionByTenantQuery(dto.TenantId), CancellationToken.None);
        Assert.Equal("STANDARD", found.Value.PlanCode);
    }

    [Fact]
    public async Task List_plans_and_change_plan()
    {
        var plans = Substitute.For<IPlanReader>();
        plans.ListActiveAsync(Arg.Any<CancellationToken>()).Returns([
            new PlanDto(Guid.CreateVersion7(), "STANDARD", "Std", null, 1, "COP", BillingPeriod.Monthly, true)
        ]);
        var listed = await new ListPlansHandler(plans).HandleAsync(new ListPlansQuery(), CancellationToken.None);
        Assert.Single(listed);

        var provisioning = Substitute.For<ISubscriptionProvisioning>();
        provisioning.ChangePlanAsync(Arg.Any<Guid>(), "STANDARD", Arg.Any<CancellationToken>()).Returns(Result.Success());
        var changed = await new ChangePlanHandler(provisioning)
            .HandleAsync(new ChangePlanCommand(Guid.CreateVersion7(), "STANDARD"), CancellationToken.None);
        Assert.True(changed.IsSuccess);
    }

    [Fact]
    public async Task Login_handler_delegates()
    {
        var auth = Substitute.For<Chevrere.Modules.Identity.Application.Abstractions.IUserAuthenticator>();
        var response = new Chevrere.Modules.Identity.Application.Contracts.LoginResponse("t", FixedClock.Now, Guid.CreateVersion7(), "a@b.com", null, ["PlatformAdmin"]);
        auth.AuthenticateAsync(Arg.Any<Chevrere.Modules.Identity.Application.Contracts.LoginRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(response));
        var result = await new Chevrere.Modules.Identity.Application.Login.LoginHandler(auth)
            .HandleAsync(new Chevrere.Modules.Identity.Application.Contracts.LoginRequest("a@b.com", "x"), CancellationToken.None);
        Assert.Equal("t", result.Value.AccessToken);
    }

    private static FranchiseeDetailDto Detail() => new(
        Guid.CreateVersion7(),
        Guid.CreateVersion7(),
        "OP",
        TenantStatus.Pending,
        "FR",
        "Legal",
        "Trade",
        IdentificationType.Nit,
        "900",
        "a@b.com",
        "300",
        FranchiseeStatus.Pending,
        FixedClock.Now,
        FixedClock.Now,
        null,
        null,
        []);
}
