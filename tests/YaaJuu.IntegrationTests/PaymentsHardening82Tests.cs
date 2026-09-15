using System.Net;
using System.Net.Http.Json;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Domain;
using YaaJuu.Modules.Payments.Application;
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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PaymentsHardening82Tests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Runtime_Production_selects_Production_config_not_higher_Sandbox_version()
    {
        factory.FakePayments.Reset();
        SetRuntimeEnvironment("Production");
        try
        {
            using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
            var seed = await OrdersTestData.SeedBuyableStoreAsync(
                factory, admin, "PEV1", "931801001", 4.9101d, -74.9101d);
            await PaymentsTestData.ConfigureAndEnableMerchantAsync(
                admin, seed.Store.TenantId,
                "pub_sandbox_v3", "prv_sb", "int_sb", "evt_sb", "Sandbox");
            await PaymentsTestData.ConfigureAndEnableMerchantAsync(
                admin, seed.Store.TenantId,
                "pub_prod_v2", "prv_pr", "int_pr", "evt_pr", "Production");
            // Bump Sandbox version higher than Production
            await PaymentsTestData.ConfigureAndEnableMerchantAsync(
                admin, seed.Store.TenantId,
                "pub_sandbox_v4", "prv_sb2", "int_sb2", "evt_sb2", "Sandbox");

            var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pev1@example.com", "Cons PEV1");
            using var client = consumer;
            await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
            var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pev1-ord-00001");
            var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pev1-pay-00001");

            Assert.Contains("pub_prod_v2", factory.FakePayments.PublicKeysUsed);
            Assert.DoesNotContain("pub_sandbox_v4", factory.FakePayments.PublicKeysUsed);
            Assert.DoesNotContain("pub_sandbox_v3", factory.FakePayments.PublicKeysUsed);
            Assert.Contains("Production", factory.FakePayments.EnvironmentsUsed);
            Assert.Equal(1, factory.FakePayments.InitializeCallCount);

            var attempt = await GetAttemptAsync(payment.PaymentId);
            Assert.Equal(MerchantEnvironment.Production, attempt.Environment);
            Assert.Equal("Widget", payment.Action!.Kind);
            Assert.Null(payment.Action.CheckoutUrl);
            Assert.Equal("pub_prod_v2", payment.Action.PublicKey);
            Assert.DoesNotContain("checkout.wompi.co", payment.Action.CheckoutUrl ?? string.Empty);
        }
        finally
        {
            SetRuntimeEnvironment("Sandbox");
        }
    }

    [Fact]
    public async Task Runtime_Sandbox_selects_Sandbox_when_both_enabled()
    {
        factory.FakePayments.Reset();
        SetRuntimeEnvironment("Sandbox");
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PEV2", "931801011", 4.9201d, -74.9201d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            "pub_sb_pev2", "prv", "int", "evt", "Sandbox");
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            "pub_pr_pev2", "prv2", "int2", "evt2", "Production");

        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pev2@example.com", "Cons PEV2");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pev2-ord-00001");
        await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pev2-pay-00001");

        Assert.Contains("pub_sb_pev2", factory.FakePayments.PublicKeysUsed);
        Assert.DoesNotContain("pub_pr_pev2", factory.FakePayments.PublicKeysUsed);
    }

    [Fact]
    public async Task Runtime_Production_with_only_Sandbox_configured_blocks_provider_call()
    {
        factory.FakePayments.Reset();
        SetRuntimeEnvironment("Production");
        try
        {
            using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
            var seed = await OrdersTestData.SeedBuyableStoreAsync(
                factory, admin, "PEV3", "931801021", 4.9301d, -74.9301d);
            await PaymentsTestData.ConfigureAndEnableMerchantAsync(
                admin, seed.Store.TenantId,
                PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
                PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB, "Sandbox");

            var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pev3@example.com", "Cons PEV3");
            using var client = consumer;
            await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
            var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pev3-ord-00001");
            var response = await PaymentsTestData.InitializePaymentAsync(client, order.Id, "pev3-pay-00001");
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Equal(0, factory.FakePayments.TotalChargeCreatingCalls);

            using var owner = await ConsumerTestData.OwnerClientAsync(factory, "owner-pev3@example.com");
            var statusResponse = await owner.GetAsync("/api/v1/business/payment-configuration");
            statusResponse.EnsureSuccessStatusCode();
            var status = await statusResponse.Content.ReadFromJsonAsync<BusinessPaymentConfigurationStatusDto>(AuthHelper.Json);
            Assert.False(status!.Enabled);
            Assert.Equal("Production", status.Environment);
        }
        finally
        {
            SetRuntimeEnvironment("Sandbox");
        }
    }

    [Fact]
    public async Task Same_key_replay_after_environment_switch_keeps_historical_attempt()
    {
        factory.FakePayments.Reset();
        SetRuntimeEnvironment("Sandbox");
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PEV4", "931801031", 4.9401d, -74.9401d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            "pub_sb_pev4", "prv", "int", "evt", "Sandbox");
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            "pub_pr_pev4", "prv2", "int2", "evt2", "Production");

        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-pev4@example.com", "Cons PEV4");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "pev4-ord-00001");
        var first = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pev4-same-key");
        var firstAttempt = await GetAttemptAsync(first.PaymentId);
        Assert.Equal(MerchantEnvironment.Sandbox, firstAttempt.Environment);
        Assert.Equal(1, factory.FakePayments.InitializeCallCount);

        SetRuntimeEnvironment("Production");
        try
        {
            var replay = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "pev4-same-key");
            Assert.Equal(first.PaymentId, replay.PaymentId);
            Assert.Equal(1, factory.FakePayments.InitializeCallCount);
            var attempt = await GetAttemptAsync(replay.PaymentId);
            Assert.Equal(firstAttempt.Id, attempt.Id);
            Assert.Equal(MerchantEnvironment.Sandbox, attempt.Environment);
        }
        finally
        {
            SetRuntimeEnvironment("Sandbox");
        }
    }

    [Fact]
    public async Task Pending_amount_mismatch_webhook_does_not_approve()
    {
        await AssertNonApprovedMismatchAsync("PNM1", "931801041", 4.9501d, -74.9501d, "PENDING", wrongAmount: true, wrongCurrency: false);
    }

    [Fact]
    public async Task Declined_amount_mismatch_webhook_stays_declined_not_approved()
    {
        await AssertNonApprovedMismatchAsync("DNM1", "931801051", 4.9601d, -74.9601d, "DECLINED", wrongAmount: true, wrongCurrency: false);
    }

    [Fact]
    public async Task Pending_currency_mismatch_webhook_does_not_approve()
    {
        await AssertNonApprovedMismatchAsync("PCM2", "931801061", 4.9701d, -74.9701d, "PENDING", wrongAmount: false, wrongCurrency: true);
    }

    [Fact]
    public async Task Declined_currency_mismatch_webhook_stays_declined_not_approved()
    {
        await AssertNonApprovedMismatchAsync("DCM1", "931801071", 4.9801d, -74.9801d, "DECLINED", wrongAmount: false, wrongCurrency: true);
    }

    [Fact]
    public async Task Approved_amount_mismatch_preserves_external_approval_without_order_confirm()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "AAM2", "931801081", 4.9901d, -74.9901d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-aam2@example.com", "Cons AAM2");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "aam2-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "aam2-pay-00001");
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

        var entity = await GetPaymentAsync(payment.PaymentId);
        var latestAttempt = await GetAttemptAsync(payment.PaymentId);
        Assert.Equal(PaymentAttemptStatus.Approved, latestAttempt.Status);
        Assert.True(entity.RequiresReconciliation);
        Assert.Equal(ReconciliationReason.AmountMismatch, entity.ReconciliationReason);
        Assert.Equal(OrderStatus.PendingPayment,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
        Assert.Equal(1, await CountAuditsAsync(AuditActions.PaymentApproved, payment.PaymentId));
        Assert.Equal(1, await CountAuditsAsync(AuditActions.PaymentReconciliationRequired, payment.PaymentId));
        Assert.Equal(0, await CountAuditsAsync(AuditActions.OrderConfirmed, order.Id));
    }

    [Fact]
    public async Task Widget_action_has_official_client_safe_params_and_null_CheckoutUrl()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "WID1", "931801091", 5.0101d, -75.0101d, price: 6500m);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, "cons-wid1@example.com", "Cons WID1");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, "wid1-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, "wid1-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);

        Assert.NotNull(payment.Action);
        Assert.Equal("Widget", payment.Action!.Kind);
        Assert.Equal(PaymentsTestData.PublicKeyB, payment.Action.PublicKey);
        Assert.Equal(attempt.MerchantReference, payment.Action.MerchantReference);
        Assert.Equal(WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency)), payment.Action.AmountInCents);
        Assert.Equal("COP", payment.Action.Currency);
        Assert.Equal(
            WompiContract.ComputeIntegritySignature(
                attempt.MerchantReference,
                payment.Action.AmountInCents!.Value,
                "COP",
                PaymentsTestData.IntegritySecretB),
            payment.Action.IntegritySignature);
        Assert.Null(payment.Action.CheckoutUrl);
        Assert.DoesNotContain("checkout.wompi.co/l", payment.Action.CheckoutUrl ?? string.Empty);
    }

    [Fact]
    public async Task Reconciliation_unexpected_exception_is_logged_and_batch_continues()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seedA = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "REC1", "931801101", 5.0201d, -75.0201d);
        var seedB = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "REC2", "931801111", 5.0301d, -75.0301d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seedA.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seedB.Store.TenantId,
            PaymentsTestData.PublicKeyA, PaymentsTestData.PrivateKeyA,
            PaymentsTestData.IntegritySecretA, PaymentsTestData.EventsSecretA);

        var (_, consumerA) = await OrdersTestData.CreateConsumerAsync(factory, "cons-rec1@example.com", "Cons REC1");
        var (_, consumerB) = await OrdersTestData.CreateConsumerAsync(factory, "cons-rec2@example.com", "Cons REC2");
        using var a = consumerA;
        using var b = consumerB;
        await OrdersTestData.PutItemBodyAsync(a, seedA.ProductId, seedA.Latitude, seedA.Longitude, 1);
        await OrdersTestData.PutItemBodyAsync(b, seedB.ProductId, seedB.Latitude, seedB.Longitude, 1);
        var orderA = await OrdersTestData.CreateOrderBodyAsync(a, seedA.Latitude, seedA.Longitude, "rec1-ord-00001");
        var orderB = await OrdersTestData.CreateOrderBodyAsync(b, seedB.Latitude, seedB.Longitude, "rec2-ord-00001");
        var paymentA = await PaymentsTestData.InitializePaymentBodyAsync(a, orderA.Id, "rec1-pay-00001");
        var paymentB = await PaymentsTestData.InitializePaymentBodyAsync(b, orderB.Id, "rec2-pay-00001");
        var attemptA = await GetAttemptAsync(paymentA.PaymentId);
        var attemptB = await GetAttemptAsync(paymentB.PaymentId);

        factory.FakePayments.ThrowOnLookupTransactionId = attemptA.ProviderTransactionId;
        factory.FakePayments.SetLookupStatus(
            attemptB.ProviderTransactionId!,
            PaymentProviderOutcomeStatus.Approved,
            orderB.TotalAmount,
            orderB.Currency,
            attemptB.MerchantReference);

        var logger = new CapturingLogger<ReconcilePaymentsHandler>();
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set($"test-recon-{Guid.CreateVersion7():N}");
        var handler = ActivatorUtilities.CreateInstance<ReconcilePaymentsHandler>(
            scope.ServiceProvider,
            logger);
        await handler.RunBatchAsync(CancellationToken.None);

        Assert.Contains(logger.Entries, e => e.Level == LogLevel.Error && e.Exception is not null);
        Assert.DoesNotContain(logger.Entries, e =>
            (e.Message ?? string.Empty).Contains(PaymentsTestData.PrivateKeyB, StringComparison.Ordinal)
            || (e.Message ?? string.Empty).Contains(PaymentsTestData.EventsSecretB, StringComparison.Ordinal));

        Assert.Equal(OrderStatus.Confirmed,
            (await b.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{orderB.Id}", AuthHelper.Json))!.Status);
        Assert.Equal(OrderStatus.PendingPayment,
            (await a.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{orderA.Id}", AuthHelper.Json))!.Status);
    }

    private async Task AssertNonApprovedMismatchAsync(
        string suffix,
        string nit,
        double lat,
        double lon,
        string providerStatus,
        bool wrongAmount,
        bool wrongCurrency)
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, suffix, nit, lat, lon, price: 50000m);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);
        var (_, consumer) = await OrdersTestData.CreateConsumerAsync(factory, $"cons-{suffix.ToLowerInvariant()}@example.com", $"Cons {suffix}");
        using var client = consumer;
        await OrdersTestData.PutItemBodyAsync(client, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(client, seed.Latitude, seed.Longitude, $"{suffix.ToLowerInvariant()}-ord-00001");
        var payment = await PaymentsTestData.InitializePaymentBodyAsync(client, order.Id, $"{suffix.ToLowerInvariant()}-pay-00001");
        var attempt = await GetAttemptAsync(payment.PaymentId);
        var cents = wrongAmount
            ? 4900000L
            : WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var currency = wrongCurrency ? "USD" : "COP";
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            providerStatus,
            cents,
            currency);

        using var anon = factory.CreateClientUnredirected();
        Assert.Equal(HttpStatusCode.OK, (await PaymentsTestData.PostWebhookAsync(anon, body, checksum)).StatusCode);

        var entity = await GetPaymentAsync(payment.PaymentId);
        var latestAttempt = await GetAttemptAsync(payment.PaymentId);
        Assert.NotEqual(PaymentAttemptStatus.Approved, latestAttempt.Status);
        Assert.NotEqual(PaymentStatus.Approved, entity.Status);
        if (providerStatus == "DECLINED")
        {
            Assert.Equal(PaymentAttemptStatus.Declined, latestAttempt.Status);
        }
        else
        {
            Assert.Equal(PaymentAttemptStatus.Pending, latestAttempt.Status);
        }

        Assert.True(entity.RequiresReconciliation);
        Assert.Equal(OrderStatus.PendingPayment,
            (await client.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
        Assert.Equal(0, await CountAuditsAsync(AuditActions.PaymentApproved, payment.PaymentId));
        Assert.Equal(0, await CountAuditsAsync(AuditActions.OrderConfirmed, order.Id));
    }

    private void SetRuntimeEnvironment(string environment) =>
        factory.Services.GetRequiredService<IOptions<PaymentsOptions>>().Value.Wompi.Environment = environment;

    private async Task<Payment> GetPaymentAsync(Guid paymentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.Payments.IgnoreQueryFilters().SingleAsync(x => x.Id == paymentId);
    }

    private async Task<PaymentAttempt> GetAttemptAsync(Guid paymentId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.PaymentAttempts.IgnoreQueryFilters()
            .Where(x => x.PaymentId == paymentId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstAsync();
    }

    private async Task<int> CountAuditsAsync(string action, Guid entityId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        return await db.AuditEvents.IgnoreQueryFilters()
            .CountAsync(x => x.Action == action && x.EntityId == entityId);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception, string? Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, exception, formatter(state, exception)));
        }
    }
}
