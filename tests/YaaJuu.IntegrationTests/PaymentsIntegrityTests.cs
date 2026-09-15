using System.Net.Http.Json;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Inventory.Domain;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PaymentsIntegrityTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Database_rejects_duplicate_provider_event_identity()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PIX1", "931800301", 4.8001d, -74.8001d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pix1@example.com", "Cons PIX1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pix1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pix1-pay-00001");

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var attempt = await db.PaymentAttempts.IgnoreQueryFilters()
            .SingleAsync(a => a.PaymentId == payment.PaymentId);
        var merchantId = attempt.MerchantConfigurationId;
        var now = DateTimeOffset.UtcNow.ToString("o");
        var eventId = "evt_unique_provider_001";

        var insert1 = $"""
            INSERT INTO payment_provider_events (
                id, provider, environment, provider_event_id, event_type, payload_hash, status,
                payment_attempt_id, merchant_configuration_id, received_at, created_at, updated_at)
            VALUES (
                '{Guid.CreateVersion7()}', 'Wompi', 'Sandbox', '{eventId}', 'transaction.updated',
                'hash1', 'Processed', '{attempt.Id}', '{merchantId}',
                TIMESTAMPTZ '{now}', TIMESTAMPTZ '{now}', TIMESTAMPTZ '{now}')
            """;
        await db.Database.ExecuteSqlRawAsync(insert1);

        var insert2 = $"""
            INSERT INTO payment_provider_events (
                id, provider, environment, provider_event_id, event_type, payload_hash, status,
                payment_attempt_id, merchant_configuration_id, received_at, created_at, updated_at)
            VALUES (
                '{Guid.CreateVersion7()}', 'Wompi', 'Sandbox', '{eventId}', 'transaction.updated',
                'hash2', 'Processed', '{attempt.Id}', '{merchantId}',
                TIMESTAMPTZ '{now}', TIMESTAMPTZ '{now}', TIMESTAMPTZ '{now}')
            """;
        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(insert2));
        Assert.Contains("ux_payment_provider_events_id", ex.ConstraintName ?? ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Database_rejects_duplicate_merchant_reference_and_order_payment()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PIX2", "931800311", 4.8101d, -74.8101d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pix2@example.com", "Cons PIX2");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pix2-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pix2-pay-00001");

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var attempt = await db.PaymentAttempts.IgnoreQueryFilters()
            .SingleAsync(a => a.PaymentId == payment.PaymentId);
        var paymentRow = await db.Payments.IgnoreQueryFilters().SingleAsync(p => p.Id == payment.PaymentId);
        var now = DateTimeOffset.UtcNow.ToString("o");

        var dupRef = $"""
            INSERT INTO payment_attempts (
                id, payment_id, merchant_configuration_id, provider, environment, status,
                amount, currency, merchant_reference, created_at, updated_at)
            VALUES (
                '{Guid.CreateVersion7()}', '{payment.PaymentId}', '{attempt.MerchantConfigurationId}',
                'Wompi', 'Sandbox', 'Declined', 6500, 'COP', '{attempt.MerchantReference}',
                TIMESTAMPTZ '{now}', TIMESTAMPTZ '{now}')
            """;
        var refEx = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(dupRef));
        Assert.Contains("ux_payment_attempts_merchant_reference", refEx.ConstraintName ?? refEx.Message, StringComparison.OrdinalIgnoreCase);

        var dupOrder = $"""
            INSERT INTO payments (
                id, order_id, tenant_id, store_id, provider, status, amount, currency,
                requires_reconciliation, reconciliation_reason, created_at, updated_at)
            VALUES (
                '{Guid.CreateVersion7()}', '{paymentRow.OrderId}', '{paymentRow.TenantId}', '{paymentRow.StoreId}',
                'Wompi', 'Pending', 6500, 'COP', FALSE, 'None', TIMESTAMPTZ '{now}', TIMESTAMPTZ '{now}')
            """;
        var orderEx = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(dupOrder));
        Assert.Contains("ux_payments_order_id", orderEx.ConstraintName ?? orderEx.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inventory_absolute_boundary_no_mutations_across_payment_lifecycle()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PIB1", "931800321", 4.8201d, -74.8201d, stock: 12);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pib1@example.com", "Cons PIB1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 2);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pib1-ord-00001");
        Assert.Equal((12L, 2L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pib1-pay-00001");
        Assert.Equal((12L, 2L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (declinedBody, declinedChecksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "DECLINED",
            cents,
            "COP");
        using var anon = factory.CreateClientUnredirected();
        Assert.Equal(System.Net.HttpStatusCode.OK,
            (await PaymentsTestData.PostWebhookAsync(anon, declinedBody, declinedChecksum)).StatusCode);

        Assert.Equal(PaymentAttemptStatus.Declined, (await GetAttemptAsync(payment.PaymentId)).Status);
        Assert.Equal((12L, 2L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));

        var retryResponse = await PaymentsTestData.InitializePaymentAsync(client, order.Id, "pib1-pay-00002");
        if (!retryResponse.IsSuccessStatusCode)
        {
            var bodyText = await retryResponse.Content.ReadAsStringAsync();
            Assert.Fail($"{(int)retryResponse.StatusCode}: {bodyText}");
        }

        var retry = await retryResponse.Content.ReadFromJsonAsync<ConsumerPaymentDto>(AuthHelper.Json);
        Assert.NotNull(retry);
        var approvedAttempt = (await GetPaymentAsync(retry.PaymentId)).Attempts
            .OrderByDescending(a => a.CreatedAt).First();
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            approvedAttempt.MerchantReference,
            approvedAttempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP",
            eventId: "evt_pib1_approved");
        Assert.Equal(System.Net.HttpStatusCode.OK,
            (await PaymentsTestData.PostWebhookAsync(anon, body, checksum)).StatusCode);

        Assert.Equal((12L, 2L), await BalancesAsync(seed.Store.StoreId, seed.ProductId));
        await using var check = factory.Services.CreateAsyncScope();
        check.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = check.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        Assert.Equal(0, await db.InventoryMovements.IgnoreQueryFilters().CountAsync(m =>
            m.StoreId == seed.Store.StoreId
            && m.GlobalProductId == seed.ProductId
            && m.Type == InventoryMovementType.ReservationCommitted));
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
