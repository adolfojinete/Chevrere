using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using YaaJuu.Modules.Payments.Application.Contracts;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.Modules.Payments.Domain;
using YaaJuu.Modules.Tenancy.Application.Contracts;

namespace YaaJuu.IntegrationTests;

internal static class PaymentsTestData
{
    public const string PrivateKeyA = "test-private-key-a";
    public const string IntegritySecretA = "test-integrity-secret-a";
    public const string EventsSecretA = "test-events-secret-a";
    public const string PublicKeyA = "pub_test_merchant_a";

    public const string PrivateKeyB = "test-private-key-b";
    public const string IntegritySecretB = "test-integrity-secret-b";
    public const string EventsSecretB = "test-events-secret-b";
    public const string PublicKeyB = "pub_test_merchant_b";

    public static async Task ConfigureAndEnableMerchantAsync(
        HttpClient admin,
        Guid tenantId,
        string publicKey,
        string privateKey,
        string integritySecret,
        string eventsSecret,
        string environment = "Sandbox")
    {
        var put = await admin.PutAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/payments/wompi",
            new WompiMerchantWriteRequest(
                environment,
                publicKey,
                privateKey,
                integritySecret,
                eventsSecret,
                ReplaceSecrets: true),
            AuthHelper.Json);
        put.EnsureSuccessStatusCode();

        var enable = await admin.PostAsync(
            $"/api/v1/admin/tenants/{tenantId}/payments/wompi/enable?environment={environment}",
            null);
        enable.EnsureSuccessStatusCode();
    }

    public static async Task DisableMerchantAsync(
        HttpClient admin,
        Guid tenantId,
        string environment = "Sandbox")
    {
        var disable = await admin.PostAsync(
            $"/api/v1/admin/tenants/{tenantId}/payments/wompi/disable?environment={environment}",
            null);
        disable.EnsureSuccessStatusCode();
    }

    public static async Task<HttpResponseMessage> InitializePaymentAsync(
        HttpClient client,
        Guid orderId,
        string idempotencyKey,
        InitializePaymentRequest? body = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/consumer/orders/{orderId}/payments")
        {
            Content = JsonContent.Create(body ?? new InitializePaymentRequest(null, null, null, null, null), options: AuthHelper.Json)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return await client.SendAsync(request);
    }

    public static async Task<ConsumerPaymentDto> InitializePaymentBodyAsync(
        HttpClient client,
        Guid orderId,
        string idempotencyKey,
        InitializePaymentRequest? body = null)
    {
        var response = await InitializePaymentAsync(client, orderId, idempotencyKey, body);
        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<ConsumerPaymentDto>(AuthHelper.Json);
        Assert.NotNull(dto);
        return dto;
    }

    public static (string Body, string Checksum) BuildSignedTransactionUpdatedWebhook(
        string eventsSecret,
        string reference,
        string providerTransactionId,
        string status,
        long amountInCents,
        string currency,
        string? eventId = null,
        long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var id = eventId ?? $"evt_{Guid.CreateVersion7():N}";
        var properties = new[] { "transaction.id", "transaction.status", "transaction.amount_in_cents" };
        var values = new[]
        {
            providerTransactionId,
            status,
            amountInCents.ToString(CultureInfo.InvariantCulture)
        };
        var checksum = ComputeChecksum(eventsSecret, values, ts);

        var payload = new Dictionary<string, object?>
        {
            ["event"] = "transaction.updated",
            ["id"] = id,
            ["timestamp"] = ts,
            ["signature"] = new Dictionary<string, object?>
            {
                ["properties"] = properties,
                ["checksum"] = checksum
            },
            ["data"] = new Dictionary<string, object?>
            {
                ["transaction"] = new Dictionary<string, object?>
                {
                    ["id"] = providerTransactionId,
                    ["status"] = status,
                    ["amount_in_cents"] = amountInCents,
                    ["currency"] = currency,
                    ["reference"] = reference
                }
            }
        };

        var body = JsonSerializer.Serialize(payload, AuthHelper.Json);
        return (body, checksum);
    }

    public static string ComputeChecksum(string eventsSecret, IReadOnlyList<string> propertyValues, long timestamp)
    {
        var sb = new StringBuilder();
        foreach (var value in propertyValues)
        {
            sb.Append(value);
        }

        sb.Append(timestamp);
        sb.Append(eventsSecret);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()))).ToLowerInvariant();
    }

    public static async Task<HttpResponseMessage> PostWebhookAsync(
        HttpClient client,
        string body,
        string checksum)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/wompi")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-Event-Checksum", checksum);
        return await client.SendAsync(request);
    }

    public static CreateFranchiseeResponse RequireStore(CreateFranchiseeResponse? store) =>
        store ?? throw new InvalidOperationException("Store seed was null.");
}
