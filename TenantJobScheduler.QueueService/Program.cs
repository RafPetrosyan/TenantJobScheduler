using System.Net.Http.Json;
using TenantJobScheduler.Shared;

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    shutdown.Cancel();
};

var store = await JobStoreFactory.CreateAsync();
var protector = new AesPayloadProtector(
    Environment.GetEnvironmentVariable("PAYLOAD_ENCRYPTION_KEY")
    ?? "development-only-key-change-me");
var scheduler = new TenantScheduler();
using var httpClient = new HttpClient();

var workerUrl = Environment.GetEnvironmentVariable("WORKER_URL") ?? "http://localhost:5081/worker/jobs";
var initialSlots = int.TryParse(Environment.GetEnvironmentVariable("TOTAL_SLOTS"), out var parsedSlots)
    ? parsedSlots
    : 20;
var settingsStore = new SchedulerSettingsStore(
    Environment.GetEnvironmentVariable("SCHEDULER_SETTINGS_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "scheduler-settings.json"));
await settingsStore.SaveAsync(new SchedulerSettings(Math.Clamp(initialSlots, 1, 100)), CancellationToken.None);
var lockSeconds = int.TryParse(Environment.GetEnvironmentVariable("JOB_LOCK_SECONDS"), out var parsedLock)
    ? parsedLock
    : 120;

Console.WriteLine($"Queue Service started. slots={initialSlots}, worker={workerUrl}");

using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
try
{
    while (await timer.WaitForNextTickAsync(shutdown.Token))
    {
        var now = DateTimeOffset.UtcNow;
        var totalSlots = (await settingsStore.GetAsync(shutdown.Token)).TotalSlots;
        await RecoverExpiredLocksAsync(now);
        await RecoverFailedJobsAsync(now);

        var jobs = await store.ListAsync(CancellationToken.None);
        var batch = scheduler.SelectDispatchBatch(jobs, totalSlots, now);

        foreach (var job in batch)
        {
            await DispatchAsync(job, now, shutdown.Token);
        }
    }
}
catch (OperationCanceledException)
{
    Console.WriteLine("Queue Service stopped gracefully.");
}

async Task RecoverExpiredLocksAsync(DateTimeOffset now)
{
    var jobs = await store.ListAsync(CancellationToken.None);
    foreach (var job in jobs.Where(job => job.Status == JobStatus.Running && job.LockedUntil <= now))
    {
        if (job.AttemptCount >= job.MaxAttempts)
        {
            job.Status = JobStatus.DeadLetter;
            job.Error = "Worker lock expired and retry limit was reached.";
        }
        else
        {
            job.Status = JobStatus.Queued;
            job.Error = "Worker lock expired; job returned to queue.";
            job.AvailableAt = null;
            job.LockedUntil = null;
        }

        if (!await store.TryUpdateAsync(job, CancellationToken.None))
        {
            Console.WriteLine($"Skipping stale recovery job {job.Id}; it no longer exists.");
        }
    }
}

async Task RecoverFailedJobsAsync(DateTimeOffset now)
{
    var jobs = await store.ListAsync(CancellationToken.None);
    foreach (var job in jobs.Where(job => job.Status == JobStatus.Failed))
    {
        if (job.AttemptCount >= job.MaxAttempts)
        {
            job.Status = JobStatus.DeadLetter;
            job.Error = job.Error ?? "Retry limit was reached.";
            job.AvailableAt = null;
            job.LockedUntil = null;
        }
        else
        {
            var delaySeconds = Math.Pow(2, Math.Max(0, job.AttemptCount));
            job.Status = JobStatus.Queued;
            job.Error = $"{job.Error ?? "Worker failed"}. Retry after {delaySeconds:0}s.";
            job.AvailableAt = now.AddSeconds(delaySeconds);
            job.LockedUntil = null;
        }

        if (!await store.TryUpdateAsync(job, CancellationToken.None))
        {
            Console.WriteLine($"Skipping stale failed callback job {job.Id}; it no longer exists.");
        }
    }
}

async Task DispatchAsync(JobRecord job, DateTimeOffset now, CancellationToken cancellationToken)
{
    try
    {
        var payload = protector.Unprotect(job.EncryptedPayload);
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new InvalidOperationException("Job payload is empty.");
        }

        job.Status = JobStatus.Running;
        job.AttemptCount++;
        job.AvailableAt = null;
        job.LockedUntil = now.AddSeconds(lockSeconds);
        job.Error = null;
        if (!await store.TryUpdateAsync(job, cancellationToken))
        {
            Console.WriteLine($"Skipping stale job {job.Id}; it no longer exists.");
            return;
        }

        var request = new WorkerJobRequest(job.Id, job.TenantId, payload, job.AttemptCount);
        var response = await httpClient.PostAsJsonAsync(workerUrl, request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Worker returned {(int)response.StatusCode}.");
        }
    }
    catch (Exception exception)
    {
        await MarkFailedOrRetryAsync(job, exception.Message);
    }
}

async Task MarkFailedOrRetryAsync(JobRecord job, string error)
{
    if (job.AttemptCount >= job.MaxAttempts)
    {
        job.Status = JobStatus.DeadLetter;
        job.Error = error;
        job.LockedUntil = null;
        if (!await store.TryUpdateAsync(job, CancellationToken.None))
        {
            Console.WriteLine($"Skipping stale failed job {job.Id}; it no longer exists.");
        }
        return;
    }

    var delaySeconds = Math.Pow(2, Math.Max(0, job.AttemptCount));
    job.Status = JobStatus.Queued;
    job.Error = $"{error}. Retry after {delaySeconds:0}s.";
    job.AvailableAt = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);
    job.LockedUntil = null;
    if (!await store.TryUpdateAsync(job, CancellationToken.None))
    {
        Console.WriteLine($"Skipping stale retry job {job.Id}; it no longer exists.");
    }
}
