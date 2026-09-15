using YaaJuu.Modules.Payments.Domain;

namespace YaaJuu.Modules.Payments.Application;

/// <summary>
/// Resolves the platform-selected Wompi runtime environment for NEW payments.
/// </summary>
public static class PaymentRuntimeEnvironment
{
    public static MerchantEnvironment Resolve(PaymentsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Parse(options.Wompi.Environment);
    }

    public static MerchantEnvironment Parse(string? value)
    {
        if (string.Equals(value, "Sandbox", StringComparison.OrdinalIgnoreCase))
        {
            return MerchantEnvironment.Sandbox;
        }

        if (string.Equals(value, "Production", StringComparison.OrdinalIgnoreCase))
        {
            return MerchantEnvironment.Production;
        }

        throw new InvalidOperationException(
            $"Payments:Wompi:Environment must be 'Sandbox' or 'Production' (got '{value}').");
    }

    public static bool IsValid(string? value) =>
        string.Equals(value, "Sandbox", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "Production", StringComparison.OrdinalIgnoreCase);
}
