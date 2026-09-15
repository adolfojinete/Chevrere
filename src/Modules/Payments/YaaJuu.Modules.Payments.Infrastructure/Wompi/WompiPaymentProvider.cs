using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using YaaJuu.Modules.Payments.Application;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using YaaJuu.SharedKernel.Payments;
using Microsoft.Extensions.Options;

namespace YaaJuu.Modules.Payments.Infrastructure.Wompi;

public sealed class WompiPaymentProvider(
    IHttpClientFactory httpClientFactory,
    IOptions<PaymentsOptions> options) : IPaymentProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public PaymentProviderKind Kind => PaymentProviderKind.Wompi;

    public Task<PaymentProviderInitializeResult> InitializeWidgetAsync(
        PaymentProviderInitializeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var money = Money.Create(request.Amount, request.Currency);
        var cents = WompiAmountConverter.ToAmountInCents(money);
        var signature = ComputeIntegritySignature(
            request.MerchantReference,
            cents,
            request.Currency,
            request.Secrets.IntegritySecret);

        // Widget MVP: client-safe parameters only. No fabricated checkout URL.
        var action = new PaymentClientAction(
            "Widget",
            request.Secrets.PublicKey,
            null,
            request.MerchantReference,
            cents,
            request.Currency,
            signature);

        return Task.FromResult(new PaymentProviderInitializeResult(
            true,
            PaymentProviderOutcomeStatus.Pending,
            null,
            "PENDING",
            null,
            null,
            action,
            false));
    }

    public async Task<PaymentProviderTransactionResult> CreateCardTransactionAsync(
        PaymentProviderCardChargeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var money = Money.Create(request.Amount, request.Currency);
        var cents = WompiAmountConverter.ToAmountInCents(money);
        var signature = ComputeIntegritySignature(
            request.MerchantReference,
            cents,
            request.Currency,
            request.Secrets.IntegritySecret);

        var client = httpClientFactory.CreateClient("Wompi");
        using var message = new HttpRequestMessage(HttpMethod.Post, BuildUrl(request.Environment, "/transactions"));
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.Secrets.PrivateKey);
        message.Content = JsonContent.Create(new WompiCreateTransactionRequest(
            request.AcceptanceToken,
            request.AcceptPersonalAuth,
            cents,
            request.Currency,
            request.CustomerEmail,
            request.MerchantReference,
            signature,
            new WompiPaymentMethod("CARD", request.CardToken, request.Installments),
            request.RedirectUrl), options: JsonOptions);

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // 4xx/5xx after send: outcome may be unknown for 5xx; 422 duplicate reference is recoverable via GET if we had id.
                var unknown = (int)response.StatusCode >= 500 || (int)response.StatusCode == 408;
                return new PaymentProviderTransactionResult(
                    false,
                    unknown ? PaymentProviderOutcomeStatus.Unknown : PaymentProviderOutcomeStatus.Error,
                    null,
                    null,
                    null,
                    null,
                    request.MerchantReference,
                    $"http_{(int)response.StatusCode}",
                    "Provider rejected the transaction request.",
                    null,
                    unknown);
            }

            var parsed = JsonSerializer.Deserialize<WompiTransactionEnvelope>(body, JsonOptions);
            return MapTransaction(parsed?.Data, request.MerchantReference);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unknown(request.MerchantReference, "timeout");
        }
        catch (HttpRequestException)
        {
            return Unknown(request.MerchantReference, "transport");
        }
    }

    public async Task<PaymentProviderTransactionResult> GetTransactionAsync(
        PaymentProviderLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var client = httpClientFactory.CreateClient("Wompi");
        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            BuildUrl(request.Environment, $"/transactions/{Uri.EscapeDataString(request.ProviderTransactionId)}"));
        // Official Wompi Colombia contract: GET /v1/transactions/{id} requires PrivateKey (prv_*).
        // Public key queries are no longer supported and may return 404.
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.Secrets.PrivateKey);

        try
        {
            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Unknown(null, $"http_{(int)response.StatusCode}");
            }

            var parsed = JsonSerializer.Deserialize<WompiTransactionEnvelope>(body, JsonOptions);
            return MapTransaction(parsed?.Data, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unknown(null, "timeout");
        }
        catch (HttpRequestException)
        {
            return Unknown(null, "transport");
        }
    }

    private string BuildUrl(string environment, string path) =>
        $"{ResolveBaseUrl(environment, options.Value).TrimEnd('/')}{path}";

    /// <summary>
    /// Exhaustive environment → base URL mapping. Never falls back to Sandbox for unknown values.
    /// </summary>
    internal static string ResolveBaseUrl(string environment, PaymentsOptions opts)
    {
        ArgumentNullException.ThrowIfNull(opts);
        if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return opts.Wompi.ProductionBaseUrl;
        }

        if (string.Equals(environment, "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return opts.Wompi.SandboxBaseUrl;
        }

        throw new InvalidOperationException(
            $"Unsupported Wompi merchant environment '{environment}'. Expected Sandbox or Production.");
    }

    public static string ComputeIntegritySignature(
        string reference,
        long amountInCents,
        string currency,
        string integritySecret)
    {
        var payload = $"{reference}{amountInCents}{currency}{integritySecret}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static bool VerifyEventChecksum(
        string eventsSecret,
        IReadOnlyList<string> propertyValuesInOrder,
        long timestamp,
        string checksum)
    {
        var sb = new StringBuilder();
        foreach (var value in propertyValuesInOrder)
        {
            sb.Append(value);
        }

        sb.Append(timestamp);
        sb.Append(eventsSecret);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        var expected = Convert.ToHexString(hash).ToLowerInvariant();
        var actual = checksum.Trim().ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }

    public static PaymentProviderOutcomeStatus MapStatus(string? raw) =>
        raw?.Trim().ToUpperInvariant() switch
        {
            "PENDING" => PaymentProviderOutcomeStatus.Pending,
            "APPROVED" => PaymentProviderOutcomeStatus.Approved,
            "DECLINED" => PaymentProviderOutcomeStatus.Declined,
            "VOIDED" => PaymentProviderOutcomeStatus.Voided,
            "ERROR" => PaymentProviderOutcomeStatus.Error,
            null or "" => PaymentProviderOutcomeStatus.Unknown,
            _ => PaymentProviderOutcomeStatus.Unknown
        };

    private static PaymentProviderTransactionResult MapTransaction(WompiTransactionData? data, string? fallbackReference)
    {
        if (data is null || string.IsNullOrWhiteSpace(data.Id))
        {
            return Unknown(fallbackReference, "malformed");
        }

        decimal? amount = data.AmountInCents is null
            ? null
            : WompiAmountConverter.FromAmountInCents(data.AmountInCents.Value);

        return new PaymentProviderTransactionResult(
            true,
            MapStatus(data.Status),
            data.Id,
            data.Status,
            amount,
            data.Currency,
            data.Reference ?? fallbackReference,
            data.StatusMessage,
            data.StatusMessage,
            null,
            false);
    }

    private static PaymentProviderTransactionResult Unknown(string? reference, string code) =>
        new(false, PaymentProviderOutcomeStatus.Unknown, null, null, null, null, reference, code, null, null, true);

    private sealed record WompiCreateTransactionRequest(
        [property: JsonPropertyName("acceptance_token")] string AcceptanceToken,
        [property: JsonPropertyName("accept_personal_auth")] string AcceptPersonalAuth,
        [property: JsonPropertyName("amount_in_cents")] long AmountInCents,
        [property: JsonPropertyName("currency")] string Currency,
        [property: JsonPropertyName("customer_email")] string CustomerEmail,
        [property: JsonPropertyName("reference")] string Reference,
        [property: JsonPropertyName("signature")] string Signature,
        [property: JsonPropertyName("payment_method")] WompiPaymentMethod PaymentMethod,
        [property: JsonPropertyName("redirect_url")] string? RedirectUrl);

    private sealed record WompiPaymentMethod(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("installments")] int Installments);

    private sealed class WompiTransactionEnvelope
    {
        [JsonPropertyName("data")]
        public WompiTransactionData? Data { get; set; }
    }

    private sealed class WompiTransactionData
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        [JsonPropertyName("amount_in_cents")]
        public long? AmountInCents { get; set; }

        [JsonPropertyName("currency")]
        public string? Currency { get; set; }

        [JsonPropertyName("reference")]
        public string? Reference { get; set; }

        [JsonPropertyName("status_message")]
        public string? StatusMessage { get; set; }
    }
}
