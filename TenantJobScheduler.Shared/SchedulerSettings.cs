using System.Text.Json;

namespace TenantJobScheduler.Shared;

public sealed record SchedulerSettings(int TotalSlots, int ReservedHeadroomSlots = 1)
{
    public static SchedulerSettings Default { get; } = new(20, 1);
}

public sealed class SchedulerSettingsStore(string filePath)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<SchedulerSettings> GetAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(filePath))
            {
                await SaveUnsafeAsync(SchedulerSettings.Default, cancellationToken);
                return SchedulerSettings.Default;
            }

            await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            return await JsonSerializer.DeserializeAsync<SchedulerSettings>(stream, _jsonOptions, cancellationToken)
                ?? SchedulerSettings.Default;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SaveAsync(SchedulerSettings settings, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await SaveUnsafeAsync(settings, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task SaveUnsafeAsync(SchedulerSettings settings, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        await JsonSerializer.SerializeAsync(stream, settings, _jsonOptions, cancellationToken);
    }
}
