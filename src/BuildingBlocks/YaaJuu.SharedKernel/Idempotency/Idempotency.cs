using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace YaaJuu.SharedKernel.Idempotency;

public static class IdempotencyOperations
{
    public const string InventoryInitialize = "inventory.initialize";
    public const string InventoryAdjust = "inventory.adjust";
    public const string InventoryWaste = "inventory.waste";
    public const string ProcurementPurchaseOrderCreate = "procurement.purchase_order.create";
    public const string ProcurementReceive = "procurement.receive";
    public const string OrdersCreate = "orders.create";
    public const string PaymentsInitialize = "payments.initialize";
}

public static class IdempotencyKeyRules
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static bool IsValid(string? key) =>
        !string.IsNullOrWhiteSpace(key)
        && key.Length >= MinLength
        && key.Length <= MaxLength
        && !key.Any(char.IsWhiteSpace);
}

public static class IdempotencyFingerprint
{
    public static string Format(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Format(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    public static string Sha256(params string?[] parts)
    {
        var canonical = string.Join('|', parts.Select(p => p ?? string.Empty));
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

/// <summary>
/// Successful idempotent operation record. Persisted only after a successful mutation.
/// </summary>
public sealed class IdempotentOperation
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public string Operation { get; set; } = null!;

    public string IdempotencyKey { get; set; } = null!;

    public string RequestHash { get; set; } = null!;

    public Guid? ResourceId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public interface IIdempotencyStore
{
    Task<IdempotentOperation?> FindAsync(
        Guid tenantId,
        string operation,
        string idempotencyKey,
        CancellationToken cancellationToken);

    void Add(IdempotentOperation operation);

    /// <summary>
    /// Detaches a pending (Added) idempotency row from a failed write attempt.
    /// </summary>
    void DiscardPending(IdempotentOperation operation);
}
