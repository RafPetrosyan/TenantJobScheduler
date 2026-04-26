using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace TenantJobScheduler.Shared;

public sealed class FileJobStore(JobStoreOptions options) : IJobStore
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly Mutex _processGate = new(false, BuildMutexName(options.FilePath));

    public async Task<IReadOnlyList<JobRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        WaitForProcessGate();
        try
        {
            return ReadUnsafe();
        }
        finally
        {
            _processGate.ReleaseMutex();
            Gate.Release();
        }
    }

    public async Task<JobRecord?> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var jobs = await ListAsync(cancellationToken);
        return jobs.FirstOrDefault(job => job.Id == jobId);
    }

    public async Task AddAsync(JobRecord job, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        WaitForProcessGate();
        try
        {
            var jobs = ReadUnsafe();
            jobs.Add(job);
            WriteUnsafe(jobs);
        }
        finally
        {
            _processGate.ReleaseMutex();
            Gate.Release();
        }
    }

    public async Task UpdateAsync(JobRecord job, CancellationToken cancellationToken)
    {
        if (!await TryUpdateAsync(job, cancellationToken))
        {
            throw new InvalidOperationException($"Job '{job.Id}' was not found.");
        }
    }

    public async Task<bool> TryUpdateAsync(JobRecord job, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        WaitForProcessGate();
        try
        {
            var jobs = ReadUnsafe();
            var index = jobs.FindIndex(current => current.Id == job.Id);
            if (index < 0)
            {
                return false;
            }

            job.UpdatedAt = DateTimeOffset.UtcNow;
            jobs[index] = job;
            WriteUnsafe(jobs);
            return true;
        }
        finally
        {
            _processGate.ReleaseMutex();
            Gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        WaitForProcessGate();
        try
        {
            WriteUnsafe([]);
        }
        finally
        {
            _processGate.ReleaseMutex();
            Gate.Release();
        }
    }

    private List<JobRecord> ReadUnsafe()
    {
        if (!File.Exists(options.FilePath))
        {
            return [];
        }

        return WithFileRetry(() =>
        {
            using var stream = new FileStream(
                options.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            return JsonSerializer.Deserialize<List<JobRecord>>(stream, _jsonOptions) ?? [];
        });
    }

    private void WriteUnsafe(List<JobRecord> jobs)
    {
        var directory = Path.GetDirectoryName(options.FilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        WithFileRetry(() =>
        {
            using var stream = new FileStream(
                options.FilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.Read);

            JsonSerializer.Serialize(stream, jobs, _jsonOptions);
            return true;
        });
    }

    private static T WithFileRetry<T>(Func<T> operation)
    {
        const int attempts = 8;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                return operation();
            }
            catch (IOException) when (attempt < attempts)
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(40 * attempt));
            }
        }

        return operation();
    }

    private void WaitForProcessGate()
    {
        try
        {
            _processGate.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // Previous process crashed while holding the mutex; current process owns it now.
        }
    }

    private static string BuildMutexName(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        return $@"Global\TenantJobScheduler_{hash}";
    }
}
