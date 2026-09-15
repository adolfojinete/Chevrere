using System.Net;
using System.Net.Http.Json;
using YaaJuu.Infrastructure.Identity;
using YaaJuu.Infrastructure.Persistence;
using YaaJuu.Modules.Identity.Domain;
using YaaJuu.Modules.Orders.Application.Contracts;
using YaaJuu.Modules.Orders.Domain;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.SharedKernel.Context;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace YaaJuu.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class PaymentsAuthorizationTests(YaaJuuApiFactory factory)
{
    [Fact]
    public async Task Consumer_authorization_and_anonymous_webhook_rules()
    {
        factory.FakePayments.Reset();
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PAZ1", "931800401", 4.8501d, -74.8501d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);

        var (_, consumerA) = await OrdersTestData.CreateConsumerAsync(factory, "cons-paz1a@example.com", "Cons PAZ1A");
        var (_, consumerB) = await OrdersTestData.CreateConsumerAsync(factory, "cons-paz1b@example.com", "Cons PAZ1B");
        using var a = consumerA;
        using var b = consumerB;
        await OrdersTestData.PutItemBodyAsync(a, seed.ProductId, seed.Latitude, seed.Longitude, 1);
        var order = await OrdersTestData.CreateOrderBodyAsync(a, seed.Latitude, seed.Longitude, "paz1-ord-00001");

        var own = await PaymentsTestData.InitializePaymentAsync(a, order.Id, "paz1-pay-00001");
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        var foreign = await PaymentsTestData.InitializePaymentAsync(b, order.Id, "paz1-pay-foreign");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        using var anon = factory.CreateClientUnredirected();
        var anonymousInit = await PaymentsTestData.InitializePaymentAsync(anon, order.Id, "paz1-pay-anon");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousInit.StatusCode);

        var payment = await own.Content.ReadFromJsonAsync<ConsumerPaymentDto>(AuthHelper.Json);
        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ITenantFilterBypass>().Enabled = true;
        var db = scope.ServiceProvider.GetRequiredService<YaaJuuDbContext>();
        var attempt = await db.PaymentAttempts.IgnoreQueryFilters()
            .SingleAsync(x => x.PaymentId == payment!.PaymentId);
        var cents = WompiAmountConverter.ToAmountInCents(Money.Create(order.TotalAmount, order.Currency));
        var (body, checksum) = PaymentsTestData.BuildSignedTransactionUpdatedWebhook(
            PaymentsTestData.EventsSecretB,
            attempt.MerchantReference,
            attempt.ProviderTransactionId!,
            "APPROVED",
            cents,
            "COP");
        Assert.Equal(HttpStatusCode.OK, (await PaymentsTestData.PostWebhookAsync(anon, body, checksum)).StatusCode);
        Assert.Equal(OrderStatus.Confirmed,
            (await a.GetFromJsonAsync<ConsumerOrderDto>($"/api/v1/consumer/orders/{order.Id}", AuthHelper.Json))!.Status);
    }

    [Fact]
    public async Task Business_foreign_store_and_platform_support_merchant_policies()
    {
        using var admin = await AuthHelper.AuthenticatedAdminAsync(factory);
        var seed = await OrdersTestData.SeedBuyableStoreAsync(
            factory, admin, "PAZ2", "931800411", 4.8601d, -74.8601d);
        await PaymentsTestData.ConfigureAndEnableMerchantAsync(
            admin, seed.Store.TenantId,
            PaymentsTestData.PublicKeyB, PaymentsTestData.PrivateKeyB,
            PaymentsTestData.IntegritySecretB, PaymentsTestData.EventsSecretB);

        var other = await ConsumerTestData.CreateActiveStoreAsync(
            admin, "PAZ2X", "owner-paz2x@example.com", "931800412");
        using var foreign = await ConsumerTestData.OwnerClientAsync(factory, "owner-paz2x@example.com");
        var business = await foreign.GetAsync("/api/v1/business/payment-configuration");
        business.EnsureSuccessStatusCode();
        var status = await business.Content.ReadFromJsonAsync<BusinessPaymentConfigurationStatusDto>(AuthHelper.Json);
        Assert.False(status!.Configured);
        Assert.False(status.Enabled);
        Assert.NotEqual(seed.Store.TenantId, other.TenantId);

        await EnsureSupportUserAsync();
        using var support = factory.CreateClientUnredirected();
        var token = await AuthHelper.LoginAsync(support, "support-payments@yaajuu.test", "SupportTest!23456");
        support.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var get = await support.GetAsync($"/api/v1/admin/tenants/{seed.Store.TenantId}/payments/wompi");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);

        var put = await support.PutAsJsonAsync(
            $"/api/v1/admin/tenants/{seed.Store.TenantId}/payments/wompi",
            new WompiMerchantWriteRequest(
                "Sandbox",
                "pub_should_fail",
                "priv",
                "int",
                "evt",
                ReplaceSecrets: true),
            AuthHelper.Json);
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    private async Task EnsureSupportUserAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        const string email = "support-payments@yaajuu.test";
        if (await users.FindByEmailAsync(email) is not null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = "Platform Support Payments",
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        Assert.True((await users.CreateAsync(user, "SupportTest!23456")).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, RoleNames.PlatformSupport)).Succeeded);
    }
}
