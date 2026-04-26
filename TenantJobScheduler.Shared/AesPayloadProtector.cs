using System.Security.Cryptography;
using System.Text;

namespace TenantJobScheduler.Shared;

public sealed class AesPayloadProtector
{
    private readonly byte[] _key;

    public AesPayloadProtector(string key)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
    }

    public string Protect(string payload)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(payload);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var output = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, output, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, output, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(output);
    }

    public string Unprotect(string encryptedPayload)
    {
        var input = Convert.FromBase64String(encryptedPayload);
        var iv = input[..16];
        var cipherBytes = input[16..];

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
