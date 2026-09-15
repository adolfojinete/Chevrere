using System.Collections.Concurrent;
using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using YaaJuu.SharedKernel.Payments;

namespace YaaJuu.IntegrationTests;

/// <summary>
/// Test double for IPaymentProvider. Counts external side-effect calls and records merchant public keys used.
/// </summary>
public sealed class FakePaymentProvider : IPaymentProvider
{
    private int _initializeCalls;
    private int _cardChargeCalls;
    private int _lookupCalls;

    public PaymentProviderKind Kind => PaymentProviderKind.Wompi;

    public ConcurrentBag<string> PublicKeysUsed { get; } = [];

    public ConcurrentBag<string> EnvironmentsUsed { get; } = [];

    public ConcurrentBag<(decimal Amount, string Currency, string PublicKey, string Reference, string Environment)> InitializeRequests { get; } = [];

    public ConcurrentDictionary<string, PaymentProviderTransactionResult> LookupByTransactionId { get; } = new(StringComparer.Ordinal);

    /// <summary>When true, InitializeWidgetAsync returns OutcomeUnknown without assigning a durable provider tx id.</summary>
    public bool SimulateInitializeUnknown { get; set; }

    /// <summary>Optional gate to serialize/coordinate concurrent initialize calls.</summary>
    public Func<Task>? BeforeInitializeAsync { get; set; }

    /// <summary>When set, GetTransactionAsync throws for the matching provider transaction id (then clears).</summary>
    public string? ThrowOnLookupTransactionId { get; set; }

    public int InitializeCallCount => Volatile.Read(ref _initializeCalls);

    public int CardChargeCallCount => Volatile.Read(ref _cardChargeCalls);

    public int LookupCallCount => Volatile.Read(ref _lookupCalls);

    public int TotalChargeCreatingCalls => InitializeCallCount + CardChargeCallCount;

    public void Reset()
    {
        Interlocked.Exchange(ref _initializeCalls, 0);
        Interlocked.Exchange(ref _cardChargeCalls, 0);
        Interlocked.Exchange(ref _lookupCalls, 0);
        PublicKeysUsed.Clear();
        EnvironmentsUsed.Clear();
        InitializeRequests.Clear();
        LookupByTransactionId.Clear();
        SimulateInitializeUnknown = false;
        BeforeInitializeAsync = null;
        ThrowOnLookupTransactionId = null;
    }

    public async Task<PaymentProviderInitializeResult> InitializeWidgetAsync(
        PaymentProviderInitializeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (BeforeInitializeAsync is not null)
        {
            await BeforeInitializeAsync();
        }

        Interlocked.Increment(ref _initializeCalls);
        PublicKeysUsed.Add(request.Secrets.PublicKey);
        EnvironmentsUsed.Add(request.Environment);
        InitializeRequests.Add((request.Amount, request.Currency, request.Secrets.PublicKey, request.MerchantReference, request.Environment));

        if (SimulateInitializeUnknown)
        {
            return new PaymentProviderInitializeResult(
                false,
                PaymentProviderOutcomeStatus.Unknown,
                null,
                "UNKNOWN",
                "timeout",
                "Simulated lost response",
                null,
                true);
        }

        var money = Money.Create(request.Amount, request.Currency);
        var cents = WompiAmountConverter.ToAmountInCents(money);
        var signature = WompiContract.ComputeIntegritySignature(
            request.MerchantReference, cents, request.Currency, request.Secrets.IntegritySecret);
        var txId = $"fake_tx_{Guid.CreateVersion7():N}";
        // Mirror production Widget contract: client-safe params, no fabricated CheckoutUrl.
        var action = new PaymentClientAction(
            "Widget",
            request.Secrets.PublicKey,
            null,
            request.MerchantReference,
            cents,
            request.Currency,
            signature);

        LookupByTransactionId[txId] = new PaymentProviderTransactionResult(
            true,
            PaymentProviderOutcomeStatus.Pending,
            txId,
            "PENDING",
            request.Amount,
            request.Currency,
            request.MerchantReference,
            null,
            null,
            null,
            false);

        return new PaymentProviderInitializeResult(
            true,
            PaymentProviderOutcomeStatus.Pending,
            txId,
            "PENDING",
            null,
            null,
            action,
            false);
    }

    public Task<PaymentProviderTransactionResult> CreateCardTransactionAsync(
        PaymentProviderCardChargeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Interlocked.Increment(ref _cardChargeCalls);
        PublicKeysUsed.Add(request.Secrets.PublicKey);
        EnvironmentsUsed.Add(request.Environment);
        if (SimulateInitializeUnknown)
        {
            return Task.FromResult(new PaymentProviderTransactionResult(
                false,
                PaymentProviderOutcomeStatus.Unknown,
                null,
                null,
                null,
                null,
                request.MerchantReference,
                "timeout",
                null,
                null,
                true));
        }

        var txId = $"fake_card_{Guid.CreateVersion7():N}";
        var result = new PaymentProviderTransactionResult(
            true,
            PaymentProviderOutcomeStatus.Pending,
            txId,
            "PENDING",
            request.Amount,
            request.Currency,
            request.MerchantReference,
            null,
            null,
            null,
            false);
        LookupByTransactionId[txId] = result;
        return Task.FromResult(result);
    }

    public Task<PaymentProviderTransactionResult> GetTransactionAsync(
        PaymentProviderLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Interlocked.Increment(ref _lookupCalls);
        PublicKeysUsed.Add(request.Secrets.PublicKey);
        EnvironmentsUsed.Add(request.Environment);

        if (ThrowOnLookupTransactionId is not null
            && string.Equals(ThrowOnLookupTransactionId, request.ProviderTransactionId, StringComparison.Ordinal))
        {
            ThrowOnLookupTransactionId = null;
            throw new InvalidOperationException("Simulated unexpected reconciliation failure.");
        }

        if (LookupByTransactionId.TryGetValue(request.ProviderTransactionId, out var known))
        {
            return Task.FromResult(known);
        }

        return Task.FromResult(new PaymentProviderTransactionResult(
            false,
            PaymentProviderOutcomeStatus.Unknown,
            request.ProviderTransactionId,
            null,
            null,
            null,
            null,
            "not_found",
            null,
            null,
            true));
    }

    public void SetLookupStatus(
        string providerTransactionId,
        PaymentProviderOutcomeStatus status,
        decimal amount,
        string currency,
        string? reference = null)
    {
        LookupByTransactionId[providerTransactionId] = new PaymentProviderTransactionResult(
            true,
            status,
            providerTransactionId,
            status.ToString().ToUpperInvariant(),
            amount,
            currency,
            reference,
            null,
            null,
            null,
            false);
    }
}
