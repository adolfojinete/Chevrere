using YaaJuu.Modules.Payments.Application.Services;
using YaaJuu.SharedKernel.Domain.ValueObjects;
using YaaJuu.SharedKernel.Payments;

namespace YaaJuu.UnitTests.Payments;

public sealed class WompiContractTests
{
    [Theory]
    [InlineData(1.00, 100)]
    [InlineData(6500.00, 650000)]
    [InlineData(50000.00, 5000000)]
    public void Amount_in_cents_conversion_is_exact(decimal amount, long expectedCents)
    {
        var money = Money.Create(amount, CurrencyCodes.Cop);
        Assert.Equal(expectedCents, WompiAmountConverter.ToAmountInCents(money));
        Assert.Equal(amount, WompiAmountConverter.FromAmountInCents(expectedCents));
    }

    [Fact]
    public void Integrity_signature_matches_official_concatenation()
    {
        // reference + amount_in_cents + currency + integrity_secret
        var signature = WompiContract.ComputeIntegritySignature(
            "ref-abc",
            5000000,
            "COP",
            "test-integrity-secret");
        Assert.Equal(64, signature.Length);
        Assert.Equal(
            WompiContract.ComputeIntegritySignature("ref-abc", 5000000, "COP", "test-integrity-secret"),
            signature);
        Assert.NotEqual(
            signature,
            WompiContract.ComputeIntegritySignature("ref-abc", 4900000, "COP", "test-integrity-secret"));
    }

    [Fact]
    public void Webhook_checksum_validates_with_correct_secret_only()
    {
        const string secretA = "events-secret-a";
        const string secretB = "events-secret-b";
        var values = new[] { "tx_123", "APPROVED", "5000000" };
        const long timestamp = 1726400000;
        var checksum = Compute(secretA, values, timestamp);

        Assert.True(WompiContract.VerifyEventChecksum(secretA, values, timestamp, checksum));
        Assert.False(WompiContract.VerifyEventChecksum(secretB, values, timestamp, checksum));
    }

    [Theory]
    [InlineData("APPROVED", PaymentProviderOutcomeStatus.Approved)]
    [InlineData("DECLINED", PaymentProviderOutcomeStatus.Declined)]
    [InlineData("PENDING", PaymentProviderOutcomeStatus.Pending)]
    [InlineData("VOIDED", PaymentProviderOutcomeStatus.Voided)]
    [InlineData("ERROR", PaymentProviderOutcomeStatus.Error)]
    [InlineData("FUTURE_STATUS", PaymentProviderOutcomeStatus.Unknown)]
    public void Status_mapping_is_explicit(string raw, PaymentProviderOutcomeStatus expected) =>
        Assert.Equal(expected, WompiContract.MapStatus(raw));

    private static string Compute(string secret, IReadOnlyList<string> values, long timestamp)
    {
        var payload = string.Concat(values) + timestamp + secret;
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
