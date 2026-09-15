using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using YaaJuu.Modules.Payments.Application;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.Modules.Payments.Infrastructure.Security;
using YaaJuu.Modules.Payments.Infrastructure.Wompi;
using YaaJuu.SharedKernel.Payments;

namespace YaaJuu.UnitTests.Payments;

public sealed class WompiProviderContractTests
{
    [Fact]
    public async Task GetTransactionAsync_uses_PrivateKey_not_PublicKey()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"id":"tx_1","status":"APPROVED","amount_in_cents":100,"currency":"COP","reference":"ref"}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Wompi").Returns(new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/") });

        var provider = new WompiPaymentProvider(
            factory,
            Options.Create(new PaymentsOptions
            {
                Wompi = new WompiOptions
                {
                    SandboxBaseUrl = "https://sandbox.wompi.co/v1",
                    ProductionBaseUrl = "https://production.wompi.co/v1"
                }
            }));

        await provider.GetTransactionAsync(
            new PaymentProviderLookupRequest(
                new PaymentProviderMerchantSecrets("pub_test_x", "prv_test_x", "int", "evt"),
                "Sandbox",
                "tx_1"),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("prv_test_x", captured.Headers.Authorization.Parameter);
        Assert.DoesNotContain("pub_test_x", captured.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task InitializeWidgetAsync_returns_client_safe_params_without_CheckoutUrl()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        var provider = new WompiPaymentProvider(
            factory,
            Options.Create(new PaymentsOptions
            {
                Wompi = new WompiOptions
                {
                    SandboxBaseUrl = "https://sandbox.wompi.co/v1",
                    ProductionBaseUrl = "https://production.wompi.co/v1"
                }
            }));

        var result = await provider.InitializeWidgetAsync(
            new PaymentProviderInitializeRequest(
                new PaymentProviderMerchantSecrets("pub_widget", "prv_secret", "integrity-secret", "events"),
                "Sandbox",
                "ref-widget-1",
                50000m,
                "COP",
                null,
                null),
            CancellationToken.None);

        Assert.NotNull(result.ClientAction);
        Assert.Equal("Widget", result.ClientAction!.Kind);
        Assert.Equal("pub_widget", result.ClientAction.PublicKey);
        Assert.Equal("ref-widget-1", result.ClientAction.MerchantReference);
        Assert.Equal(5000000L, result.ClientAction.AmountInCents);
        Assert.Equal("COP", result.ClientAction.Currency);
        Assert.False(string.IsNullOrWhiteSpace(result.ClientAction.IntegritySignature));
        Assert.Null(result.ClientAction.CheckoutUrl);
        Assert.DoesNotContain("checkout.wompi.co", result.ClientAction.CheckoutUrl ?? string.Empty);
    }

    [Theory]
    [InlineData("Sandbox", "https://sandbox.wompi.co/v1")]
    [InlineData("Production", "https://production.wompi.co/v1")]
    public void ResolveBaseUrl_is_exhaustive_for_known_environments(string environment, string expectedBase)
    {
        var opts = new PaymentsOptions
        {
            Wompi = new WompiOptions
            {
                SandboxBaseUrl = "https://sandbox.wompi.co/v1",
                ProductionBaseUrl = "https://production.wompi.co/v1"
            }
        };
        Assert.Equal(expectedBase, WompiPaymentProvider.ResolveBaseUrl(environment, opts));
    }

    [Theory]
    [InlineData("Foo")]
    [InlineData("")]
    [InlineData("staging")]
    public void ResolveBaseUrl_rejects_unknown_environment_without_sandbox_fallback(string environment)
    {
        var opts = new PaymentsOptions
        {
            Wompi = new WompiOptions
            {
                SandboxBaseUrl = "https://sandbox.wompi.co/v1",
                ProductionBaseUrl = "https://production.wompi.co/v1"
            }
        };
        Assert.Throws<InvalidOperationException>(() => WompiPaymentProvider.ResolveBaseUrl(environment, opts));
    }

    [Fact]
    public async Task Historical_Sandbox_lookup_uses_Sandbox_base_url_not_runtime_Production()
    {
        HttpRequestMessage? captured = null;
        var handler = new CapturingHandler(req =>
        {
            captured = req;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"data":{"id":"tx_hist","status":"PENDING","amount_in_cents":100,"currency":"COP"}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        });

        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient("Wompi").Returns(new HttpClient(handler));

        var provider = new WompiPaymentProvider(
            factory,
            Options.Create(new PaymentsOptions
            {
                Wompi = new WompiOptions
                {
                    Environment = "Production",
                    SandboxBaseUrl = "https://sandbox.wompi.co/v1",
                    ProductionBaseUrl = "https://production.wompi.co/v1"
                }
            }));

        await provider.GetTransactionAsync(
            new PaymentProviderLookupRequest(
                new PaymentProviderMerchantSecrets("pub", "prv", "int", "evt"),
                "Sandbox",
                "tx_hist"),
            CancellationToken.None);

        Assert.NotNull(captured);
        Assert.StartsWith("https://sandbox.wompi.co/v1/transactions/", captured!.RequestUri!.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("production.wompi.co", captured.RequestUri.ToString(), StringComparison.Ordinal);
    }

    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}

public sealed class PaymentSecretMasterKeyTests
{
    [Fact]
    public void Production_missing_key_fails_fast()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Production");
        Assert.Throws<InvalidOperationException>(() =>
            new AesGcmPaymentSecretProtector(Options.Create(new PaymentsOptions { SecretsMasterKey = "" }), env));
    }

    [Fact]
    public void Production_invalid_base64_fails_fast()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Production");
        Assert.Throws<InvalidOperationException>(() =>
            new AesGcmPaymentSecretProtector(
                Options.Create(new PaymentsOptions { SecretsMasterKey = "not-base64!!!" }), env));
    }

    [Theory]
    [InlineData(31)]
    [InlineData(33)]
    public void Production_wrong_decoded_length_fails_fast(int length)
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Production");
        var key = Convert.ToBase64String(new byte[length]);
        Assert.Throws<InvalidOperationException>(() =>
            new AesGcmPaymentSecretProtector(
                Options.Create(new PaymentsOptions { SecretsMasterKey = key }), env));
    }

    [Fact]
    public void Production_valid_32_byte_base64_succeeds()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Production");
        var key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        var protector = new AesGcmPaymentSecretProtector(
            Options.Create(new PaymentsOptions { SecretsMasterKey = key }), env);
        var cipher = protector.Protect("secret-value", "WompiPrivateKey");
        Assert.Equal("secret-value", protector.Unprotect(cipher, "WompiPrivateKey"));
    }

    [Fact]
    public void Explicit_invalid_Development_key_fails_fast()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Development);
        Assert.Throws<InvalidOperationException>(() =>
            new AesGcmPaymentSecretProtector(
                Options.Create(new PaymentsOptions { SecretsMasterKey = "a" }), env));
    }

    [Fact]
    public void Testing_without_explicit_key_allows_deterministic_fallback()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Testing");
        var protector = new AesGcmPaymentSecretProtector(
            Options.Create(new PaymentsOptions { SecretsMasterKey = null }), env);
        var cipher = protector.Protect("dev-secret", "WompiEventsSecret");
        Assert.Equal("dev-secret", protector.Unprotect(cipher, "WompiEventsSecret"));
    }

    [Fact]
    public void DecodeStrictBase64Key_rejects_padded_weak_strings()
    {
        Assert.Throws<InvalidOperationException>(() =>
            AesGcmPaymentSecretProtector.DecodeStrictBase64Key("password"));
        Assert.Throws<InvalidOperationException>(() =>
            AesGcmPaymentSecretProtector.DecodeStrictBase64Key("a"));
        Assert.Throws<InvalidOperationException>(() =>
            AesGcmPaymentSecretProtector.DecodeStrictBase64Key("12345678901234567890123456789012"));
    }

    [Fact]
    public void Cross_purpose_decrypt_fails()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns("Testing");
        var protector = new AesGcmPaymentSecretProtector(
            Options.Create(new PaymentsOptions { SecretsMasterKey = null }), env);
        var cipher = protector.Protect("private", "WompiPrivateKey");
        Assert.ThrowsAny<Exception>(() => protector.Unprotect(cipher, "WompiEventsSecret"));
    }
}

public sealed class PaymentRuntimeEnvironmentTests
{
    [Theory]
    [InlineData("Sandbox", MerchantEnvironment.Sandbox)]
    [InlineData("Production", MerchantEnvironment.Production)]
    [InlineData("sandbox", MerchantEnvironment.Sandbox)]
    public void Parse_accepts_known_values(string value, MerchantEnvironment expected) =>
        Assert.Equal(expected, PaymentRuntimeEnvironment.Parse(value));

    [Theory]
    [InlineData("Foo")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_rejects_invalid_without_silent_default(string? value) =>
        Assert.Throws<InvalidOperationException>(() => PaymentRuntimeEnvironment.Parse(value));
}
