using System.Security.Cryptography;
using System.Text;
using YaaJuu.SharedKernel.Payments;

namespace YaaJuu.Modules.Payments.Application.Services;

/// <summary>
/// Pure Wompi contract helpers (no HTTP). Verified against docs.wompi.co Colombia.
/// </summary>
public static class WompiContract
{
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
}
