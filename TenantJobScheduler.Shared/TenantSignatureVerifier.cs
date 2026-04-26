using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;

namespace TenantJobScheduler.Shared;

public sealed class TenantSignatureVerifier
{
    private readonly HashSet<string> _usedNonces = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public bool Verify(
        SignedSubmitJobRequest request,
        string publicKeyPem,
        TimeSpan allowedClockSkew,
        out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            error = "TenantId is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Nonce))
        {
            error = "Nonce is required.";
            return false;
        }

        if (DateTimeOffset.UtcNow - request.Timestamp.ToUniversalTime() > allowedClockSkew
            || request.Timestamp.ToUniversalTime() - DateTimeOffset.UtcNow > allowedClockSkew)
        {
            error = "Timestamp is outside the allowed clock skew.";
            return false;
        }

        var nonceKey = $"{request.TenantId}:{request.Nonce}";
        lock (_gate)
        {
            if (!_usedNonces.Add(nonceKey))
            {
                error = "Nonce was already used.";
                return false;
            }
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(publicKeyPem);

            var payloadJson = NormalizePayload(request.Payload);
            var message = BuildMessage(request.TenantId, payloadJson, request.Timestamp, request.Nonce);
            var signature = Convert.FromBase64String(request.Signature);

            var isValid = rsa.VerifyData(
                Encoding.UTF8.GetBytes(message),
                signature,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pss);

            if (!isValid)
            {
                error = "Invalid signature.";
            }

            return isValid;
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException)
        {
            error = "Signature or public key is invalid.";
            return false;
        }
    }

    public static string NormalizePayload(JsonNode payload)
    {
        return payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    public static string BuildMessage(string tenantId, string payloadJson, DateTimeOffset timestamp, string nonce)
    {
        var normalizedTimestamp = timestamp
            .ToUniversalTime()
            .ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        return $"{tenantId}\n{normalizedTimestamp}\n{nonce}\n{payloadJson}";
    }
}
