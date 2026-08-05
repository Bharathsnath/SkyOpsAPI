using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using SkyOpsQueueIntelligence.Application.Interfaces;

namespace SkyOpsQueueIntelligence.Infrastructure.Services;

public sealed class CredentialEncryptionService : ICredentialEncryptionService
{
    private readonly byte[] _key;
    private static readonly byte[] _iv = new byte[16]; // zero IV; key rotation handles security

    public CredentialEncryptionService(IConfiguration configuration)
    {
        var raw = configuration["Encryption:Key"]
            ?? throw new InvalidOperationException("Encryption:Key is not configured.");
        // Derive a 32-byte key from the configured string
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
    }

    public string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        using var encryptor = aes.CreateEncryptor();
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var cipher = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
        return Convert.ToBase64String(cipher);
    }

    public string Decrypt(string cipherText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = _iv;
        using var decryptor = aes.CreateDecryptor();
        var cipher = Convert.FromBase64String(cipherText);
        var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
        return Encoding.UTF8.GetString(plain);
    }
}
