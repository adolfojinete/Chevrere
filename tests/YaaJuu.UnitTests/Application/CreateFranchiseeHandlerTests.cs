using YaaJuu.SharedKernel.Audit;
using YaaJuu.Modules.Identity.Application.Abstractions;
using YaaJuu.Modules.Identity.Application.Contracts;
using YaaJuu.Modules.Subscriptions.Application.Abstractions;
using YaaJuu.Modules.Subscriptions.Domain;
using YaaJuu.Modules.Tenancy.Application.Abstractions;
using YaaJuu.Modules.Tenancy.Application.Commands.CreateFranchisee;
using YaaJuu.Modules.Tenancy.Application.Contracts;
using YaaJuu.Modules.Tenancy.Domain;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;
using NSubstitute;

namespace YaaJuu.UnitTests.Application;

public sealed class CreateFranchiseeHandlerTests
{
    private readonly ITenancyStore _store = Substitute.For<ITenancyStore>();
    private readonly IIdentityProvisioning _identity = Substitute.For<IIdentityProvisioning>();
    private readonly ISubscriptionProvisioning _subscriptions = Substitute.For<ISubscriptionProvisioning>();
    private readonly IAuditRecorder _audit = Substitute.For<IAuditRecorder>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly IClock _clock = Substitute.For<IClock>();

    public CreateFranchiseeHandlerTests()
    {
        _clock.UtcNow.Returns(FixedClock.Now);
        _identity.AddOwnerAsync(Arg.Any<OwnerUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new OwnerUserResult(Guid.CreateVersion7(), "owner@example.com")));
        _subscriptions.StartAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result.Success(TestEntities.Subscription()));
    }

    [Fact]
    public async Task Creates_aggregate_when_data_is_unique()
    {
        var handler = Handler();
        var result = await handler.HandleAsync(Request(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        _store.Received(1).AddTenant(Arg.Any<Tenant>());
        _store.Received(1).AddFranchisee(Arg.Any<Franchisee>());
        _store.Received(1).AddStore(Arg.Any<Store>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _audit.Received().Record(
            AuditActions.TenantCreated,
            nameof(Tenant),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<object?>(),
            Arg.Any<object?>());
    }

    [Fact]
    public async Task Rejects_duplicate_tenant_code()
    {
        _store.TenantCodeExistsAsync("OP-NORTE", Arg.Any<CancellationToken>()).Returns(true);
        var result = await Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.True(result.IsFailure);
        Assert.Equal("tenant.code.duplicate", result.Error!.Code);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Rejects_duplicate_identification()
    {
        _store.IdentificationExistsAsync("900123456", Arg.Any<CancellationToken>()).Returns(true);
        var result = await Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.Equal("franchisee.identification.duplicate", result.Error!.Code);
    }

    [Fact]
    public async Task Rejects_duplicate_owner_email()
    {
        _identity.EmailExistsAsync("owner@example.com", Arg.Any<CancellationToken>()).Returns(true);
        var result = await Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.Equal("owner.email.duplicate", result.Error!.Code);
    }

    [Fact]
    public async Task Rejects_duplicate_franchisee_code()
    {
        _store.FranchiseeCodeExistsAsync("FR-NORTE", Arg.Any<CancellationToken>()).Returns(true);
        var result = await Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.Equal("franchisee.code.duplicate", result.Error!.Code);
    }

    [Fact]
    public async Task Returns_identity_failure()
    {
        _identity.AddOwnerAsync(Arg.Any<OwnerUserRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<OwnerUserResult>(Error.Conflict("owner.email.duplicate", "dup")));
        var result = await Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.Equal("owner.email.duplicate", result.Error!.Code);
    }

    [Fact]
    public async Task Returns_subscription_failure()
    {
        _subscriptions.StartAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<Subscription>(Error.NotFound("plan.not_found", "no")));
        var result = await Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.Equal("plan.not_found", result.Error!.Code);
    }

    [Fact]
    public async Task Rejects_duplicate_store_code()
    {
        _store.StoreCodeExistsAsync(Arg.Any<Guid>(), "DS-001", Arg.Any<CancellationToken>()).Returns(true);
        var result = await Handler().HandleAsync(Request(), CancellationToken.None);
        Assert.Equal("store.code.duplicate", result.Error!.Code);
    }

    [Fact]
    public async Task Domain_exception_becomes_domain_error()
    {
        var result = await Handler().HandleAsync(Request() with { TenantCode = "x" }, CancellationToken.None);
        Assert.Equal(ErrorType.Domain, result.Error!.Type);
    }

    private CreateFranchiseeHandler Handler() =>
        new(_store, _identity, _subscriptions, _audit, _unitOfWork, _clock);

    private static CreateFranchiseeRequest Request() => new(
        "OP-NORTE",
        "Operador Norte",
        "FR-NORTE",
        "Operador SAS",
        "YaaJuu Norte",
        IdentificationType.Nit,
        "900123456",
        "ops@example.com",
        "+573001112233",
        "owner@example.com",
        "Owner",
        "OwnerTest!23456",
        "DS-001",
        "Dark Store",
        "Calle 1",
        4.65m,
        -74.06m,
        "STANDARD");
}
