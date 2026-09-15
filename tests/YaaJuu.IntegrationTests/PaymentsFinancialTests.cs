using System.Net;
using System.Net.Http.Json;
using YaaJuu.Infrastructure.Audit;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Inventory.Domain;
using YaaJuu.Modules.Orders.Application.Commands;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Domain;
using YaaJuu.Modules.Payments.Application.Commands;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Application;
using YaaJuu.SharedKernel.Audit;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using YaaJuu.SharedKernel.Persistence;
using YaaJuu.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PaymentsFinancialTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Multi_merchant_order_uses_only_tenant_b_credentials()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var storeA = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PMA1", "931800001", 4.5001d, -74.5001d, stock: 10, price: 6500m);
        var storeB = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PMB1", "931800002", 4.5101d, -74.5101d, stock: 10, price: 6500m);

        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, storeA.Store.TenantId,
            PaymentsTestData.PublicKeyA, PaymentsTestData.PrivateKeyA,
            PaymentsTestData.IntegritySecretA, PaymentsTestData.EventsSecretA);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, storeB.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);

        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pmb1@example.com", "Cons PMB1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, storeB.ProductId, storeB.Latitude, storeB.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, storeB.Latitude, storeB.Longitude, "pmb1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pmb1-pay-00001");

        Assert.Equal(1, factory.FakePayments.InitializeCallCount);
        Assert.Contains(PaymentsTestData.PublicKeyB, factory.FakePayments.PublicKeysUsed);
        Assert.DoesNotContain(PaymentsTestData.PublicKeyA, factory.FakePayments.PublicKeysUsed);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var entity = await db.Payments.IgnoreQueryFilters().Include(p => p.Attempts)
            .SingleAsync(p => p.Id == payment.PaymentId);
        Assert.Equal(storeB.Store.TenantId, entity.TenantId);
        Assert.Equal(storeB.Store.StoreId, entity.StoreId);
        var merchantB = await db.PaymentMerchantConfigurations.IgnoreQueryFilters()
            .SingleAsync(c => c.TenantId == storeB.Store.TenantId && c.IsEnabled);
        Assert.Equal(merchantB.Id, Assert.Single(entity.Attempts).MerchantConfigurationId);
    }

    [Fact]
    public async Task Cross_tenant_payment_order_fk_is_rejected_by_postgresql()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var storeA = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PXA1", "931800011", 4.5201d, -74.5201d);
        var storeB = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PXB1", "931800012", 4.5301d, -74.5301d);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pxb1@example.com", "Cons PXB1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, storeB.ProductId, storeB.Latitude, storeB.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, storeB.Latitude, storeB.Longitude, "pxb1-ord-00001");

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var now = DateTimeOffset.UtcNow.ToString("o");
        var sql = $"""
            INSERT INTO payments (
                id, order_id, tenant_id, store_id, provider, status, amount, currency,
                requires_reconciliation, reconciliation_reason, created_at, updated_at)
            VALUES (
                '{Guid.CreateVersion7()}', '{order.Id}', '{storeA.Store.TenantId}', '{storeB.Store.StoreId}',
                'Wompi', 'Pending', 6500, 'COP', FALSE, 'None', TIMESTAMPTZ '{now}', TIMESTAMPTZ '{now}')
            """;
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql));
        Assert.True(
            (ex.ConstraintName ?? ex.Message).Contains("fk_payments", StringComparison.OrdinalIgnoreCase)
            || (ex.ConstraintName ?? ex.Message).Contains("payments", StringComparison.OrdinalIgnoreCase),
            ex.ConstraintName ?? ex.Message);
    }

    [Fact]
    public async Task Payment_amount_comes_from_order_snapshot_not_current_pricing()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PAM1", "931800021", 4.5401d, -74.5401d, stock: 10, price: 6500m);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);

        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pam1@example.com", "Cons PAM1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pam1-ord-00001");
        Assert.Equal(6500m, order.TotalAmount);

        await ConsumerTestData.SetGlobalPriceAsync(admin, seed.ProductId, 7000m);
        await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pam1-pay-00001");

        var captured = Assert.Single(factory.FakePayments.InitializeRequests);
        Assert.Equal(6500m, captured.Amount);
        Assert.Equal("COP", captured.Currency);
        Assert.Equal(650000L, WompiAmountConverter.ToAmountInCents(Money.Create(6500m, "COP")));
    }

    [Fact]
    public async Task Happy_path_approved_webhook_confirms_order_without_inventory_commit()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PHP1", "931800031", 4.5501d, -74.5501d, stock: 10, price: 6500m);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);

        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-php1@example.com", "Cons PHP1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 3);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "php1-ord-00001");
        Assert.Equal((10L, 3L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "php1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP");

        using var anon = factory.CreateClientUnredirected();
        var webhook = await PaymentsTestData.PostWebhookAsync(anon, body, checksum);
        Assert.Equal(HttpStatusCode.OK, webhook.StatusCode);

        var latestOrder = await client.GetFromJsonAsync<ConsumerOrderDto>(
            $"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json);
        Assert.Equal(OrderStatus.Confirmed, latestOrder!.Status);
        Assert.Equal((10L, 3L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
        Assert.Equal(0, await CountMovementsAsync(seed.Store.StoreId, seed.ProductId, InventoryMovementType.ReservationCommitted));
        Assert.Equal(1, await CountActiveReservationsAsync(order.Id));

        var paymentEntity = await GetPaymentAsync(payment.PaymentId);
        Assert.Equal(PaymentStatus.Approved, paymentEntity.Status);
        Assert.Equal(PaymentAttemptStatus.Approved, Assert.Single(paymentEntity.Attempts).Status);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.PaymentApproved, payment.PaymentId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.OrderConfirmed, order.Id));
    }

    [Fact]
    public async Task Invalid_webhook_signature_causes_zero_mutations()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PIV1", "931800041", 4.5601d, -74.5601d, stock: 5, price: 6500m);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-piv1@example.com", "Cons PIV1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "piv1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "piv1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, _) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP");

        using var anon = factory.CreateClientUnredirected();
        var webhook = await PaymentsTestData.PostWebhookAsync(anon, body, "deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
        Assert.Equal(HttpStatusCode.Unauthorized, webhook.StatusCode);

        var latestOrder = await client.GetFromJsonAsync<ConsumerOrderDto>(
            $"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json);
        Assert.Equal(OrderStatus.PendingPayment, latestOrder!.Status);
        Assert.Equal(0, await CountAuditsAsync(AuditActions.PaymentApproved, payment.PaymentId));
        Assert.Equal(0, await CountAuditsAsync(AuditActions.OrderConfirmed, order.Id));
        Assert.Equal(PaymentAttemptStatus.Pending, (await GetAttemptAsync(payment.PaymentId)).Status);
    }

    [Fact]
    public async Task Wrong_merchant_secret_rejects_webhook()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var storeA = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PWA1", "931800051", 4.5701d, -74.5701d);
        var storeB = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PWB1", "931800052", 4.5801d, -74.5801d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, storeA.Store.TenantId,
            PaymentsTestData.PublicKeyA, PaymentsTestData.PrivateKeyA,
            PaymentsTestData.IntegritySecretA, PaymentsTestData.EventsSecretA);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, storeB.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);

        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pwb1@example.com", "Cons PWB1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, storeB.ProductId, storeB.Latitude, storeB.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, storeB.Latitude, storeB.Longitude, "pwb1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pwb1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        // Sign with Merchant A secret while attempt belongs to B.
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretA,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP");

        using var anon = factory.CreateClientUnredirected();
        var webhook = await PaymentsTestData.PostWebhookAsync(anon, body, checksum);
        Assert.Equal(HttpStatusCode.Unauthorized, webhook.StatusCode);
        Assert.Equal(OrderStatus.PendingPayment,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
    }

    [Fact]
    public async Task Tampered_amount_without_recomputing_signature_is_rejected()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PTA1", "931800061", 4.5901d, -74.5901d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pta1@example.com", "Cons PTA1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pta1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pta1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP");
        var tampered = body.Replace(
            $"\"amount_in_cents\":{cents}",
            $"\"amount_in_cents\":{cents - 10000}",
            StringComparison.Ordinal);

        using var anon = factory.CreateClientUnredirected();
        var webhook = await PaymentsTestData.PostWebhookAsync(anon, tampered, checksum);
        Assert.Equal(HttpStatusCode.Unauthorized, webhook.StatusCode);
        Assert.Equal(0, await CountAuditsAsync(AuditActions.OrderConfirmed, order.Id));
    }

    [Fact]
    public async Task Duplicate_approved_webhook_is_semantic_noop()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PDU1", "931800071", 4.6001d, -74.6001d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pdu1@example.com", "Cons PDU1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pdu1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pdu1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP",
            eventId: "evt_duplicate_fixed_001");

        using var anon = factory.CreateClientUnredirected();
        Assert.Equal(HttpStatusCode.OK, (await PaymentsTestData.PostWebhookAsync(anon, body, checksum)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PaymentsTestData.PostWebhookAsync(anon, body, checksum)).StatusCode);

        Assert.Equal(1, await CountAuditsAsync(AuditActions.PaymentApproved, payment.PaymentId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.OrderConfirmed, order.Id));
        Assert.Equal(1, await CountProviderEventsAsync(attempt.Id));
        Assert.Equal((10L, 1L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
    }

    [Fact]
    public async Task Amount_mismatch_authentic_webhook_requires_reconciliation_without_confirm()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PMM1", "931800081", 4.6101d, -74.6101d, stock: 10, price: 50000m);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pmm1@example.com", "Cons PMM1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pmm1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pmm1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            4900000L,
            "COP");

        using var anon = factory.CreateClientUnredirected();
        Assert.Equal(HttpStatusCode.OK, (await PaymentsTestData.PostWebhookAsync(anon, body, checksum)).StatusCode);

        var latest = await client.GetFromJsonAsync<ConsumerOrderDto>(
            $"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json);
        Assert.Equal(OrderStatus.PendingPayment, latest!.Status);
        var entity = await GetPaymentAsync(payment.PaymentId);
        Assert.True(entity.RequiresReconciliation);
        Assert.Equal(ReconciliationReason.AmountMismatch, entity.ReconciliationReason);
        Assert.Equal(0, await CountAuditsAsync(AuditActions.OrderConfirmed, order.Id));
    }

    [Fact]
    public async Task Currency_mismatch_requires_reconciliation_without_confirm()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PCM1", "931800091", 4.6201d, -74.6201d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pcm1@example.com", "Cons PCM1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pcm1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pcm1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "USD");

        using var anon = factory.CreateClientUnredirected();
        Assert.Equal(HttpStatusCode.OK, (await PaymentsTestData.PostWebhookAsync(anon, body, checksum)).StatusCode);
        Assert.Equal(OrderStatus.PendingPayment,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
        var entity = await GetPaymentAsync(payment.PaymentId);
        Assert.True(entity.RequiresReconciliation);
        Assert.Equal(ReconciliationReason.CurrencyMismatch, entity.ReconciliationReason);
    }

    [Fact]
    public async Task Late_approved_after_expired_requires_reconciliation()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PLE1", "931800101", 4.6301d, -74.6301d, stock: 8);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-ple1@example.com", "Cons PLE1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 2);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "ple1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "ple1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);

        await ExpireAsync(order.Id);
        Assert.Equal(OrderStatus.Expired,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
        Assert.Equal((8L, 0L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP");
        using var anon = factory.CreateClientUnredirected();
        Assert.Equal(HttpStatusCode.OK, (await PaymentsTestData.PostWebhookAsync(anon, body, checksum)).StatusCode);

        Assert.Equal(OrderStatus.Expired,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
        var entity = await GetPaymentAsync(payment.PaymentId);
        Assert.Equal(PaymentStatus.Approved, entity.Status);
        Assert.True(entity.RequiresReconciliation);
        Assert.Equal(ReconciliationReason.OrderExpiredAfterPayment, entity.ReconciliationReason);
        Assert.Equal((8L, 0L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
        Assert.Equal(0, await CountActiveReservationsAsync(order.Id));
    }

    [Fact]
    public async Task Late_approved_after_cancelled_requires_reconciliation()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PLC1", "931800111", 4.6401d, -74.6401d, stock: 8);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-plc1@example.com", "Cons PLC1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "plc1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "plc1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);

        (await client.PostAsJsonAsync($"/api/v1/consumer/orders/{order.Id}/cancel", new CancelOrderRequest("stop")))
            .EnsureSuccessStatusCode();
        Assert.Equal((8L, 0L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP");
        using var anon = factory.CreateClientUnredirected();
        Assert.Equal(HttpStatusCode.OK, (await PaymentsTestData.PostWebhookAsync(anon, body, checksum)).StatusCode);

        Assert.Equal(OrderStatus.Cancelled,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
        var entity = await GetPaymentAsync(payment.PaymentId);
        Assert.Equal(PaymentStatus.Approved, entity.Status);
        Assert.True(entity.RequiresReconciliation);
        Assert.Equal(ReconciliationReason.OrderCancelledAfterPayment, entity.ReconciliationReason);
    }

    [Fact]
    public async Task Secrets_are_encrypted_at_rest_and_redacted_on_get()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PSE1", "931800121", 4.6501d, -74.6501d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var row = await db.PaymentMerchantConfigurations.IgnoreQueryFilters()
            .SingleAsync(c => c.TenantId == seed.Store.TenantId && c.IsEnabled);
        Assert.False(string.IsNullOrWhiteSpace(row.EncryptedPrivateKey));
        Assert.DoesNotContain(PaymentsTestData.PrivateKeyB, row.EncryptedPrivateKey, StringComparison.Ordinal);
        Assert.DoesNotContain(PaymentsTestData.IntegritySecretB, row.EncryptedIntegritySecret, StringComparison.Ordinal);
        Assert.DoesNotContain(PaymentsTestData.EventsSecretB, row.EncryptedEventsSecret, StringComparison.Ordinal);

        var get = await admin.GetAsync($"/api/v1/admin/tenants/{seed.Store.TenantId}/payments/wompi");
        get.EnsureSuccessStatusCode();
        var json = await get.Content.ReadAsStringAsync();
        Assert.DoesNotContain(PaymentsTestData.PrivateKeyB, json, StringComparison.Ordinal);
        Assert.DoesNotContain(PaymentsTestData.IntegritySecretB, json, StringComparison.Ordinal);
        Assert.DoesNotContain(PaymentsTestData.EventsSecretB, json, StringComparison.Ordinal);

        using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-pse1@example.com");
        var business = await owner.GetAsync("/api/v1/business/payment-configuration");
        business.EnsureSuccessStatusCode();
        var businessJson = await business.Content.ReadAsStringAsync();
        Assert.DoesNotContain(PaymentsTestData.PrivateKeyB, businessJson, StringComparison.Ordinal);
        Assert.DoesNotContain(PaymentsTestData.EventsSecretB, businessJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_merchant_blocks_provider_calls()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PDI1", "931800131", 4.6601d, -74.6601d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        await PaymentsTestData.DisableMerchantAsync(admin, seed.Store.TenantId);

        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pdi1@example.com", "Cons PDI1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pdi1-ord-00001");
        var response = await PaymentsTestData.InitializePaymentAsync(client, order.Id, "pdi1-pay-00001");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, factory.FakePayments.TotalChargeCreatingCalls);
    }

    [Fact]
    public async Task Historical_attempt_keeps_old_merchant_after_rotation()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PRO1", "931800141", 4.6701d, -74.6701d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);

        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pro1@example.com", "Cons PRO1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order1 = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pro1-ord-00001");
        var payment1 = await PaymentsTestData.InitializePaymentBodyAsync(client, order1.Id, "pro1-pay-00001");
        var oldAttempt = await GetAttemptAsync(payment1.PaymentId);
        var oldMerchantId = oldAttempt.MerchantConfigurationId;

        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            "pub_test_merchant_b_v2",
            "test-private-key-b-v2",
            "test-integrity-secret-b-v2",
            "test-events-secret-b-v2");

        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order1.TotalAmount, order1.Currency));
        var (approvedBody, approvedChecksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            oldAttempt.MerchantReference,
            oldAttempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP",
            eventId: "evt_historical_v1");
        using var anon = factory.CreateClientUnredirected();
        Assert.Equal(HttpStatusCode.OK,
            (await PaymentsTestData.PostWebhookAsync(anon, approvedBody, approvedChecksum)).StatusCode);
        Assert.Equal(oldMerchantId, (await GetAttemptAsync(payment1.PaymentId)).MerchantConfigurationId);

        factory.FakePayments.Reset();
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order2 = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pro1-ord-00002");
        var payment2 = await PaymentsTestData.InitializePaymentBodyAsync(client, order2.Id, "pro1-pay-00002");
        var newAttempt = await GetAttemptAsync(payment2.PaymentId);
        Assert.NotEqual(oldMerchantId, newAttempt.MerchantConfigurationId);
        Assert.Contains("pub_test_merchant_b_v2", factory.FakePayments.PublicKeysUsed);
    }

    private async Task ExpireAsync(Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(Guid.CreateVersion7().ToString("D"));
        var handler = scope.ServiceProvider.GetRequiredService<IHandler<ExpireOrderCommand, Result>>();
        var result = await handler.HandleAsync(new ExpireOrderCommand(orderId), CancellationToken.None);
        Assert.True(result.IsSuccess, result.Error?.Message);
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

    private async Task<int> CountMovementsAsync(Guid storeId, Guid productId, InventoryMovementType type)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.InventoryMovements.IgnoreQueryFilters().CountAsync(
            m => m.StoreId == storeId && m.GlobalProductId == productId && m.Type == type);
    }

    private async Task<int> CountActiveReservationsAsync(Guid orderId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var itemIds = await db.OrderItems.IgnoreQueryFilters()
            .Where(i => i.OrderId == orderId).Select(i => i.Id).ToListAsync();
        return await db.InventoryReservations.IgnoreQueryFilters().CountAsync(
            r => itemIds.Contains(r.ReferenceId) && r.Status == InventoryReservationStatus.Active);
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

    private async Task<PaymentAttempt> GetAttemptAsync(Guid paymentId)
    {
        var payment = await GetPaymentAsync(paymentId);
        return Assert.Single(payment.Attempts);
    }
}
