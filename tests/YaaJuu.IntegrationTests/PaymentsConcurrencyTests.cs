using System.Net;
using System.Net.Http.Json;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Domain;
using YaaJuu.Modules.Payments.Application.Commands;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using YaaJuu.SharedKernel.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PaymentsConcurrencyTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Concurrent_duplicate_approved_webhook_applies_once()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PCW1", "931800201", 4.7001d, -74.7001d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pcw1@example.com", "Cons PCW1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pcw1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pcw1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP",
            eventId: "evt_concurrent_dup_001");

        using var a = factory.CreateClientUnredirected();
        using var b = factory.CreateClientUnredirected();
        var barrier = new Barrier(2);
        var t1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await PaymentsTestData.PostWebhookAsync(a, body, checksum);
        });
        var t2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await PaymentsTestData.PostWebhookAsync(b, body, checksum);
        });
        var responses = await Task.WhenAll(t1, t2);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        Assert.Equal(1, await CountAuditsAsync(AuditActions.PaymentApproved, payment.PaymentId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.OrderConfirmed, order.Id));
        Assert.Equal(1, await CountProviderEventsAsync(attempt.Id));
        Assert.Equal(OrderStatus.Confirmed,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
    }

    [Fact]
    public async Task Same_key_sequential_replays_without_second_provider_call()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PSK1", "931800211", 4.7101d, -74.7101d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-psk1@example.com", "Cons PSK1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "psk1-ord-00001");

        var first = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "psk1-pay-samekey");
        var second = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "psk1-pay-samekey");

        Assert.Equal(first.PaymentId, second.PaymentId);
        Assert.Equal(1, factory.FakePayments.InitializeCallCount);
        var attempts = (await GetPaymentAsync(first.PaymentId)).Attempts;
        Assert.Single(attempts);
    }

    [Fact]
    public async Task Same_key_concurrent_creates_one_payment_and_one_provider_call()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PSK2", "931800221", 4.7201d, -74.7201d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-psk2@example.com", "Cons PSK2");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "psk2-ord-00001");

        using var a = factory.CreateClientUnredirected();
        a.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        using var b = factory.CreateClientUnredirected();
        b.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;

        var barrier = new Barrier(2);
        var t1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await PaymentsTestData.InitializePaymentAsync(a, order.Id, "psk2-pay-samekey");
        });
        var t2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await PaymentsTestData.InitializePaymentAsync(b, order.Id, "psk2-pay-samekey");
        });
        var responses = await Task.WhenAll(t1, t2);
        foreach (var r in responses)
        {
            Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }

        var bodies = await Task.WhenAll(
            responses[0].Content.ReadFromJsonAsync<ConsumerPaymentDto>(AuthHelper.Json),
            responses[1].Content.ReadFromJsonAsync<ConsumerPaymentDto>(AuthHelper.Json));
        Assert.Equal(bodies[0]!.PaymentId, bodies[1]!.PaymentId);
        Assert.Equal(1, factory.FakePayments.InitializeCallCount);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        Assert.Equal(1, await db.Payments.IgnoreQueryFilters().CountAsync(p => p.OrderId == order.Id));
        Assert.Equal(1, await db.PaymentAttempts.IgnoreQueryFilters().CountAsync(x => x.PaymentId == bodies[0]!.PaymentId));
    }

    [Fact]
    public async Task Different_key_concurrent_allows_only_one_provider_charge()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PDK1", "931800231", 4.7301d, -74.7301d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pdk1@example.com", "Cons PDK1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pdk1-ord-00001");

        using var a = factory.CreateClientUnredirected();
        a.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        using var b = factory.CreateClientUnredirected();
        b.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;

        var barrier = new Barrier(2);
        var t1 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await PaymentsTestData.InitializePaymentAsync(a, order.Id, "pdk1-pay-key-aaa");
        });
        var t2 = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await PaymentsTestData.InitializePaymentAsync(b, order.Id, "pdk1-pay-key-bbb");
        });
        var responses = await Task.WhenAll(t1, t2);

        var success = responses.Count(r => r.IsSuccessStatusCode);
        var conflict = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, success);
        Assert.Equal(1, conflict);
        Assert.Equal(1, factory.FakePayments.InitializeCallCount);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        Assert.Equal(1, await db.Payments.IgnoreQueryFilters().CountAsync(p => p.OrderId == order.Id));
        var paymentId = (await db.Payments.IgnoreQueryFilters().SingleAsync(p => p.OrderId == order.Id)).Id;
        Assert.Equal(1, await db.PaymentAttempts.IgnoreQueryFilters().CountAsync(x => x.PaymentId == paymentId));
    }

    [Fact]
    public async Task Provider_timeout_unknown_blocks_new_charge_on_same_and_different_key()
    {
        factory.FakePayments.Reset();
        factory.FakePayments.SimulateInitializeUnknown = true;
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PUN1", "931800241", 4.7401d, -74.7401d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pun1@example.com", "Cons PUN1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pun1-ord-00001");

        var first = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pun1-pay-00001");
        Assert.Equal(1, factory.FakePayments.InitializeCallCount);
        Assert.Equal(PaymentAttemptStatus.Unknown, (await GetAttemptAsync(first.PaymentId)).Status);
        Assert.NotEqual(PaymentAttemptStatus.Declined, (await GetAttemptAsync(first.PaymentId)).Status);

        factory.FakePayments.SimulateInitializeUnknown = false;
        var replay = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pun1-pay-00001");
        Assert.Equal(first.PaymentId, replay.PaymentId);
        Assert.Equal(1, factory.FakePayments.InitializeCallCount);

        var otherKey = await PaymentsTestData.InitializePaymentAsync(client, order.Id, "pun1-pay-00002");
        Assert.Equal(HttpStatusCode.Conflict, otherKey.StatusCode);
        Assert.Equal(1, factory.FakePayments.InitializeCallCount);
    }

    [Fact]
    public async Task Reconciliation_approved_and_declined_match_webhook_semantics()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PYRC", "931800251", 4.7501d, -74.7501d, stock: 9);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pyrc@example.com", "Cons PYRC");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var approvedOrder = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pyrc-ord-00001");
        var approvedPayment = await PaymentsTestData.InitializePaymentBodyAsync(client, approvedOrder.Id, "pyrc-pay-00001");
        var approvedAttempt = await GetAttemptAsync(approvedPayment.PaymentId);
        factory.FakePayments.SetLookupStatus(
            approvedAttempt.ProviderTransactionId!,
            PaymentProviderOutcomeStatus.Approved,
            approvedOrder.TotalAmount,
            approvedOrder.Currency,
            approvedAttempt.MerchantReference);

        await RunReconciliationAsync();

        Assert.Equal(OrderStatus.Confirmed,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{approvedOrder.Id}", AuthHelper.Json))!.Status);
        Assert.Equal(PaymentStatus.Approved, (await GetPaymentAsync(approvedPayment.PaymentId)).Status);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.PaymentApproved, approvedPayment.PaymentId));
        Assert.Equal((9L, 1L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var declinedOrder = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pyrc-ord-00002");
        var declinedPayment = await PaymentsTestData.InitializePaymentBodyAsync(client, declinedOrder.Id, "pyrc-pay-00002");
        var declinedAttempt = await GetAttemptAsync(declinedPayment.PaymentId);
        factory.FakePayments.SetLookupStatus(
            declinedAttempt.ProviderTransactionId!,
            PaymentProviderOutcomeStatus.Declined,
            declinedOrder.TotalAmount,
            declinedOrder.Currency,
            declinedAttempt.MerchantReference);

        await RunReconciliationAsync();

        Assert.Equal(OrderStatus.PendingPayment,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{declinedOrder.Id}", AuthHelper.Json))!.Status);
        Assert.Equal(PaymentAttemptStatus.Declined, (await GetAttemptAsync(declinedPayment.PaymentId)).Status);
        // Approved order still holds 1 reserved unit; declined order reservation remains Active.
        Assert.Equal((9L, 2L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
    }

    [Fact]
    public async Task Concurrent_webhook_and_reconciliation_approved_apply_once()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PWR1", "931800261", 4.7601d, -74.7601d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pwr1@example.com", "Cons PWR1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pwr1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pwr1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        factory.FakePayments.SetLookupStatus(
            attempt.ProviderTransactionId!,
            PaymentProviderOutcomeStatus.Approved,
            order.TotalAmount,
            order.Currency,
            attempt.MerchantReference);

        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP",
            eventId: "evt_vs_recon_001");

        using var anon = factory.CreateClientUnredirected();
        var barrier = new Barrier(2);
        var webhookTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await PaymentsTestData.PostWebhookAsync(anon, body, checksum);
        });
        var reconTask = Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await RunReconciliationAsync();
        });
        await Task.WhenAll(webhookTask, reconTask);

        Assert.Equal(1, await CountAuditsAsync(AuditActions.PaymentApproved, payment.PaymentId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.OrderConfirmed, order.Id));
        Assert.Equal(OrderStatus.Confirmed,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
    }

    [Fact]
    public async Task Out_of_order_pending_after_approved_is_noop()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "POO1", "931800271", 4.7701d, -74.7701d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-poo1@example.com", "Cons POO1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "poo1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "poo1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));

        using var anon = factory.CreateClientUnredirected();
        var (approvedBody, approvedChecksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP",
            eventId: "evt_ooo_approved");
        Assert.Equal(HttpStatusCode.OK,
            (await PaymentsTestData.PostWebhookAsync(anon, approvedBody, approvedChecksum)).StatusCode);

        var (pendingBody, pendingChecksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "PENDING",
            cents,
            "COP",
            eventId: "evt_ooo_pending");
        Assert.Equal(HttpStatusCode.OK,
            (await PaymentsTestData.PostWebhookAsync(anon, pendingBody, pendingChecksum)).StatusCode);

        var entity = await GetPaymentAsync(payment.PaymentId);
        Assert.Equal(PaymentStatus.Approved, entity.Status);
        Assert.Equal(PaymentAttemptStatus.Approved, Assert.Single(entity.Attempts).Status);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.PaymentApproved, payment.PaymentId));
        Assert.Equal(OrderStatus.Confirmed,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
    }

    private async Task RunReconciliationAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(Guid.CreateVersion7().ToString("D"));
        var handler = scope.ServiceProvider.GetRequiredService<ReconcilePaymentsHandler>();
        await handler.RunBatchAsync(CancellationToken.None);
    }

    private async Task<(long OnHand, long Reserved)> BalancesAsync(Guid storeId, Guid productId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var item = await db.InventoryItems.IgnoreQueryFilters()
            .SingleAsync(i => i.StoreId == storeId && i.GlobalProductId == productId);
        return (item.OnHand, item.Reserved);
    }

    private async Task<int> CountAuditsAsync(string action, Guid entityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.AuditEvents.IgnoreQueryFilters().CountAsync(a => a.Action == action && a.EntityId == entityId);
    }

    private async Task<int> CountProviderEventsAsync(Guid attemptId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.PaymentProviderEvents.CountAsync(e => e.PaymentAttemptId == attemptId);
    }

    private async Task<Payment> GetPaymentAsync(Guid paymentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.Payments.IgnoreQueryFilters().Include(p => p.Attempts)
            .SingleAsync(p => p.Id == paymentId);
    }

    private async Task<PaymentAttempt> GetAttemptAsync(Guid paymentId) =>
        Assert.Single((await GetPaymentAsync(paymentId)).Attempts);
}
