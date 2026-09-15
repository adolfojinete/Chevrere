using System.Security.Cryptography;
using System.Text;
using YaaJuu.Modules.Payments.Application;
using YaaJuu.Modules.Payments.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace YaaJuu.Modules.Payments.Infrastructure.Security;

/// <summary>
/// AES-256-GCM authenticated encryption. Ciphertext format:
/// v1.{keyId}.{base64url(nonce|tag|ciphertext)}
/// Master key from Payments:SecretsMasterKey / env YAAJUU_PAYMENT_SECRETS_KEY (32 raw bytes or base64).
/// </summary>
public sealed class AesGcmPaymentSecretProtector : IPaymentSecretProtector
{
    private const string Version = "v1";
    private const string KeyId = "k1";
    private readonly byte[] _key;

    public AesGcmPaymentSecretProtector(IOptions<PaymentsOptions> options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        var configured = options.Value.SecretsMasterKey
                         ?? Environment.GetEnvironmentVariable("YAAJUU_PAYMENT_SECRETS_KEY");

        if (string.IsNullOrWhiteSpace(configured))
        {
            if (environment.IsEnvironment("Testing") || environment.IsDevelopment())
            {
                _key = SHA256.HashData("yaajuu-dev-payment-secrets-key-v1"u8.ToArray());
                return;
            }

            throw new InvalidOperationException(
                "Payments secret master key is required (Payments:SecretsMasterKey or YAAJUU_PAYMENT_SECRETS_KEY).");
        }

        _key = DecodeKey(configured);
        if (_key.Length != 32)
        {
            throw new InvalidOperationException("Payments secret master key must decode to 32 bytes.");
        }
    }

    public string Protect(string plaintext, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];
        var aad = Encoding.UTF8.GetBytes(purpose);

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag, aad);

        var payload = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, payload, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, payload, nonce.Length + tag.Length, ciphertext.Length);

        return $"{Version}.{KeyId}.{Base64UrlEncode(payload)}";
    }

    public string Unprotect(string ciphertext, string purpose)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);

        var parts = ciphertext.Split('.', 3);
        if (parts.Length != 3 || parts[0] != Version)
        {
            throw new CryptographicException("Unsupported payment secret ciphertext format.");
        }

        var payload = Base64UrlDecode(parts[2]);
        if (payload.Length < 12 + 16 + 1)
        {
            throw new CryptographicException("Invalid payment secret ciphertext.");
        }

        var nonce = payload.AsSpan(0, 12);
        var tag = payload.AsSpan(12, 16);
        var data = payload.AsSpan(28);
        var plain = new byte[data.Length];
        var aad = Encoding.UTF8.GetBytes(purpose);

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, data, tag, plain, aad);
        return Encoding.UTF8.GetString(plain);
    }

    private static byte[] DecodeKey(string configured)
    {
        try
        {
            return Convert.FromBase64String(configured);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(configured.PadRight(32)[..32]);
        }
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}
