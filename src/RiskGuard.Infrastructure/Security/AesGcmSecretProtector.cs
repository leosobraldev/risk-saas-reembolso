using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RiskGuard.Core.Abstractions;
using RiskGuard.Infrastructure.Options;

namespace RiskGuard.Infrastructure.Security;

/// <summary>
/// AES-256-GCM com nonce aleatorio por valor. Formato: <c>v1:</c> + base64(nonce[12] | tag[16] | cifrado). O contexto entra como
/// dado autenticado adicional, entao o valor so decifra para a mesma linha para a qual foi cifrado.
/// </summary>
internal sealed class AesGcmSecretProtector : ISecretProtector
{
    private const string Prefix = "v1:";
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key;

    public AesGcmSecretProtector(IOptions<SecurityOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _key = options.Value.TryGetKeyBytes()
            ?? throw new InvalidOperationException("Security:EncryptionKey deve ser base64 de exatamente 32 bytes.");
    }

    public string Protect(string plaintext, string context)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentException.ThrowIfNullOrEmpty(context);

        var data = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[data.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, data, cipher, tag, Encoding.UTF8.GetBytes(context));

        return Prefix + Convert.ToBase64String([.. nonce, .. tag, .. cipher]);
    }

    public string Unprotect(string protectedValue, string context)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedValue);
        ArgumentException.ThrowIfNullOrEmpty(context);

        if (!protectedValue.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new CryptographicException("Formato de segredo desconhecido.");
        }

        byte[] payload;
        try
        {
            payload = Convert.FromBase64String(protectedValue[Prefix.Length..]);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("Segredo corrompido.", ex);
        }

        if (payload.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Segredo corrompido.");
        }

        var nonce = payload.AsSpan(0, NonceSize);
        var tag = payload.AsSpan(NonceSize, TagSize);
        var cipher = payload.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain, Encoding.UTF8.GetBytes(context));

        return Encoding.UTF8.GetString(plain);
    }
}
