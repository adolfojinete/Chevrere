using YaaJuu.Modules.Orders.Application.Abstractions;
using YaaJuu.Modules.Orders.Application.Commands;
using YaaJuu.Modules.Orders.Domain;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Domain;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using YaaJuu.SharedKernel.Inventory;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using YaaJuu.SharedKernel.Time;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace YaaJuu.UnitTests.Orders;

public sealed class OrderReservationLifecycleHandlerTests
{
    private static readonly Guid ConsumerId = Guid.CreateVersion7();
    private static readonly Guid TenantId = Guid.CreateVersion7();
    private static readonly Guid StoreId = Guid.CreateVersion7();

    [Fact]
    public async Task Cancel_does_not_mutate_the_order_when_the_reservation_set_is_incomplete()
    {
        var order = Place();
        var store = Substitute.For<IOrderStore>();
        var reservations = Substitute.For<IInventoryReservationService>();
        var currentUser = Substitute.For<ICurrentUser>();
        var correlation = Substitute.For<ICorrelationContext>();
        var audit = Substitute.For<IAuditRecorder>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        currentUser.UserId.Returns(ConsumerId);
        correlation.CorrelationId.Returns("corr");
        clock.UtcNow.Returns(FixedClock.Now);
        store.GetOrderForConsumerAsync(order.Id, ConsumerId, Arg.Any<CancellationToken>()).Returns(order);
        reservations.ReleaseAsync(Arg.Any<InventoryReservationReleaseRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException(
                InventoryReservationErrors.IncompleteSet,
                "The reservation set is incomplete or does not match the requested references."));

        var handler = new CancelOrderHandler(store, reservations, currentUser, correlation, audit, unitOfWork, clock);
        var result = await handler.HandleAsync(new CancelOrderCommand(order.Id, "nope"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryReservationErrors.IncompleteSet, result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        audit.DidNotReceive().Record(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<object?>(), Arg.Any<object?>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expire_does_not_swallow_integrity_errors_or_expire_the_order()
    {
        var order = Place();
        var store = Substitute.For<IOrderStore>();
        var reservations = Substitute.For<IInventoryReservationService>();
        var correlation = Substitute.For<ICorrelationContext>();
        var audit = Substitute.For<IAuditRecorder>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        correlation.CorrelationId.Returns("corr");
        clock.UtcNow.Returns(FixedClock.Now);
        store.GetOrderAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);
        reservations.ReleaseAsync(Arg.Any<InventoryReservationReleaseRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DomainException(
                InventoryReservationErrors.IncompleteSet,
                "The reservation set is incomplete or does not match the requested references."));

        var handler = new ExpireOrderHandler(store, reservations, correlation, audit, unitOfWork, clock);
        var result = await handler.HandleAsync(new ExpireOrderCommand(order.Id), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(InventoryReservationErrors.IncompleteSet, result.Error!.Code);
        Assert.Equal(ErrorType.Failure, result.Error.Type);
        Assert.Equal(OrderStatus.PendingPayment, order.Status);
        audit.DidNotReceive().Record(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<object?>(), Arg.Any<object?>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Expire_still_treats_an_already_cancelled_order_as_a_success_race()
    {
        var order = Place();
        Assert.True(order.Cancel("race", FixedClock.Now));
        var store = Substitute.For<IOrderStore>();
        var reservations = Substitute.For<IInventoryReservationService>();
        var correlation = Substitute.For<ICorrelationContext>();
        var audit = Substitute.For<IAuditRecorder>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var clock = Substitute.For<IClock>();
        store.GetOrderAsync(order.Id, Arg.Any<CancellationToken>()).Returns(order);

        var handler = new ExpireOrderHandler(store, reservations, correlation, audit, unitOfWork, clock);
        var result = await handler.HandleAsync(new ExpireOrderCommand(order.Id), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await reservations.DidNotReceive().ReleaseAsync(
            Arg.Any<InventoryReservationReleaseRequest>(), Arg.Any<CancellationToken>());
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static Order Place() =>
        Order.Place(
            "ORD-00000001",
            ConsumerId,
            TenantId,
            StoreId,
            Guid.CreateVersion7(),
            [
                new OrderLineSnapshot(
                    Guid.CreateVersion7(), "SKU-A", "Agua", "Cristal", "600 ml", 1, Money.Create(2500, "COP")),
                new OrderLineSnapshot(
                    Guid.CreateVersion7(), "SKU-B", "Cola", "Coca", "350 ml", 2, Money.Create(3000, "COP")),
                new OrderLineSnapshot(
                    Guid.CreateVersion7(), "SKU-C", "Papas", "Lays", "40 g", 3, Money.Create(2000, "COP"))
            ],
            TimeSpan.FromMinutes(15),
            FixedClock.Now);
}
