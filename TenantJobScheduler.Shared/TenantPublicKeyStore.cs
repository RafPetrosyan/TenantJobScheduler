using System.Text.Json;

namespace TenantJobScheduler.Shared;

public sealed class TenantPublicKeyStore(string filePath)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task RegisterAsync(string tenantId, string publicKeyPem, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var keys = await ReadUnsafeAsync(cancellationToken);
            keys.RemoveAll(key => string.Equals(key.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
            keys.Add(new TenantPublicKeyRecord(tenantId, publicKeyPem, DateTimeOffset.UtcNow));
            await WriteUnsafeAsync(keys, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<TenantPublicKeyRecord?> GetAsync(string tenantId, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var keys = await ReadUnsafeAsync(cancellationToken);
            return keys.FirstOrDefault(key => string.Equals(key.TenantId, tenantId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<IReadOnlyList<TenantPublicKeyRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            return await ReadUnsafeAsync(cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<List<TenantPublicKeyRecord>> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return await JsonSerializer.DeserializeAsync<List<TenantPublicKeyRecord>>(stream, _jsonOptions, cancellationToken) ?? [];
    }

    private async Task WriteUnsafeAsync(List<TenantPublicKeyRecord> keys, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, keys, _jsonOptions, cancellationToken);
    }
}
