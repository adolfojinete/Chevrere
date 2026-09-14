using Chevrere.SharedKernel.Idempotency;

namespace Chevrere.UnitTests.SharedKernel;

public sealed class IdempotencyFingerprintTests
{
    [Fact]
    public void Same_canonical_parts_produce_same_hash()
    {
        var a = IdempotencyFingerprint.Sha256("inventory.adjust", "store", "product", "Increase", "5", "Conteo");
        var b = IdempotencyFingerprint.Sha256("inventory.adjust", "store", "product", "Increase", "5", "Conteo");
        Assert.Equal(a, b);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void Different_payload_produces_different_hash()
    {
        var a = IdempotencyFingerprint.Sha256("inventory.adjust", "store", "product", "Increase", "5", "Conteo");
        var b = IdempotencyFingerprint.Sha256("inventory.adjust", "store", "product", "Increase", "10", "Conteo");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Idempotency_key_rules_are_case_sensitive_and_reject_whitespace()
    {
        Assert.True(IdempotencyKeyRules.IsValid("ABCDEFGH"));
        Assert.True(IdempotencyKeyRules.IsValid("abcdefgh"));
        Assert.False(IdempotencyKeyRules.IsValid("ABC DEF"));
        Assert.False(IdempotencyKeyRules.IsValid("short"));
        Assert.False(IdempotencyKeyRules.IsValid(null));
    }
}
