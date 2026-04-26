using System.Text.Json.Nodes;

namespace TenantJobScheduler.Shared;

public sealed record RegisterTenantKeyRequest(string TenantId, string PublicKeyPem);

public sealed record SignedSubmitJobRequest(
    string TenantId,
    JsonNode Payload,
    DateTimeOffset Timestamp,
    string Nonce,
    string Signature);

public sealed record TenantPublicKeyRecord(string TenantId, string PublicKeyPem, DateTimeOffset RegisteredAt);
