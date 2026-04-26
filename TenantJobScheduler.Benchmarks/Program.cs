using System.Diagnostics;
using System.Globalization;
using TenantJobScheduler.Shared;

const int totalSlots = 20;
const int jobDurationTicks = 5;
var now = DateTimeOffset.UtcNow;
var scenarios = new[]
{
    BuildAllTenantsScenario(now),
    BuildTwoTenantsScenario(now),
    BuildActivationBurstScenario(now)
};

var results = scenarios.Select(scenario => RunScenario(scenario, totalSlots, jobDurationTicks)).ToList();
var outputPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "benchmark-results.md"));
Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
File.WriteAllText(outputPath, BuildMarkdown(results));

Console.WriteLine(File.ReadAllText(outputPath));
Console.WriteLine($"Results written to: {outputPath}");

static Scenario BuildAllTenantsScenario(DateTimeOffset now)
{
    var jobs = Enumerable.Range(1, 20)
        .SelectMany(tenant => Enumerable.Range(1, 10).Select(index => NewJob($"tenant-{tenant}", now.AddTicks(index))))
        .ToList();

    return new Scenario("Սցենար 1. 20 ակտիվ tenant", jobs);
}

static Scenario BuildTwoTenantsScenario(DateTimeOffset now)
{
    var jobs = new[] { "tenant-1", "tenant-2" }
        .SelectMany(tenant => Enumerable.Range(1, 100).Select(index => NewJob(tenant, now.AddTicks(index))))
        .ToList();

    return new Scenario("Սցենար 2. 2 ակտիվ tenant", jobs);
}

static Scenario BuildActivationBurstScenario(DateTimeOffset now)
{
    var jobs = Enumerable.Range(1, 20)
        .SelectMany(tenant => Enumerable.Range(1, 5).Select(index =>
        {
            var job = NewJob($"tenant-{tenant}", now.AddTicks(index));
            job.AvailableAt = now.AddTicks(tenant * 2L);
            return job;
        }))
        .ToList();

    return new Scenario("Սցենար 3. Tenant activation burst", jobs);
}

static BenchmarkResult RunScenario(Scenario scenario, int totalSlots, int jobDurationTicks)
{
    var stopwatch = Stopwatch.StartNew();
    var scheduler = new TenantScheduler();
    var jobs = scenario.Jobs.Select(Clone).ToList();
    var running = new List<RunningJob>();
    var completed = new List<CompletedJob>();
    var slotSamples = new List<double>();
    var tick = 0;
    var simulationStart = DateTimeOffset.UtcNow;

    while (completed.Count < jobs.Count)
    {
        foreach (var runningJob in running.Where(item => item.CompletesAtTick <= tick).ToList())
        {
            running.Remove(runningJob);
            runningJob.Job.Status = JobStatus.Completed;
            runningJob.Job.LockedUntil = null;
            completed.Add(new CompletedJob(
                runningJob.Job.TenantId,
                runningJob.StartTick,
                tick,
                Math.Max(0, tick - ToRelativeTick(runningJob.Job.CreatedAt, simulationStart))));
        }

        var now = simulationStart.AddTicks(tick);
        var selected = scheduler.SelectDispatchBatch(jobs, totalSlots, now);
        foreach (var job in selected)
        {
            if (job.Status != JobStatus.Queued)
            {
                continue;
            }

            job.Status = JobStatus.Running;
            job.LockedUntil = now.AddTicks(jobDurationTicks * 4L);
            running.Add(new RunningJob(job, tick, tick + jobDurationTicks));
        }

        slotSamples.Add(running.Count / (double)totalSlots);
        tick++;

        if (tick > 100_000)
        {
            throw new InvalidOperationException($"Scenario '{scenario.Name}' did not finish.");
        }
    }

    stopwatch.Stop();
    var tenantGroups = completed.GroupBy(job => job.TenantId).ToList();
    var averageLatency = completed.Average(job => job.LatencyTicks);
    var p95Latency = Percentile(completed.Select(job => (double)job.LatencyTicks).Order().ToList(), 0.95);
    var throughput = completed.Count / Math.Max(1, tick);
    var utilization = slotSamples.Average() * 100;
    var fairnessSpread = tenantGroups.Max(group => group.Count()) - tenantGroups.Min(group => group.Count());

    return new BenchmarkResult(
        scenario.Name,
        completed.Count,
        tenantGroups.Count,
        totalSlots,
        tick,
        throughput,
        averageLatency,
        p95Latency,
        utilization,
        fairnessSpread,
        stopwatch.Elapsed);
}

static string BuildMarkdown(IReadOnlyList<BenchmarkResult> results)
{
    var lines = new List<string>
    {
        "# Benchmark Results",
        "",
        "Այս արդյունքները ստացվել են TenantScheduler ալգորիթմի deterministic simulation-ով։ Ժամանակը ներկայացված է simulation tick-երով, ոչ իրական վայրկյաններով։",
        "",
        "| Սցենար | Jobs | Active tenants | Slots | Total ticks | Throughput (jobs/tick) | Avg latency | P95 latency | Slot utilization | Fairness spread |",
        "| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
    };

    lines.AddRange(results.Select(result =>
        string.Create(CultureInfo.InvariantCulture,
            $"| {result.Name} | {result.CompletedJobs} | {result.ActiveTenants} | {result.TotalSlots} | {result.TotalTicks} | {result.Throughput:F2} | {result.AverageLatency:F2} | {result.P95Latency:F2} | {result.SlotUtilization:F1}% | {result.FairnessSpread} |")));

    lines.AddRange([
        "",
        "Մեկնաբանություն.",
        "",
        "- 20 ակտիվ tenant-ների դեպքում fairness spread-ը պետք է մոտ լինի 0-ին, քանի որ բոլոր tenant-ները ստանում են համաչափ հնարավորություն։",
        "- 2 ակտիվ tenant-ների դեպքում slot utilization-ը բարձր է, քանի որ work-conserving մոտեցումը ազատ slot-երը տալիս է առկա ակտիվ tenant-ներին։",
        "- Activation burst սցենարում tenant-ները աստիճանաբար ակտիվանում են, իսկ scheduler-ը հաջորդ ցիկլերում ներառում է նոր tenant-ներին բաշխման մեջ։"
    ]);

    return string.Join(Environment.NewLine, lines);
}

static JobRecord NewJob(string tenantId, DateTimeOffset createdAt)
{
    return new JobRecord
    {
        TenantId = tenantId,
        EncryptedPayload = "benchmark",
        CreatedAt = createdAt,
        UpdatedAt = createdAt
    };
}

static JobRecord Clone(JobRecord job)
{
    return new JobRecord
    {
        Id = job.Id,
        TenantId = job.TenantId,
        EncryptedPayload = job.EncryptedPayload,
        Priority = job.Priority,
        Status = job.Status,
        AttemptCount = job.AttemptCount,
        MaxAttempts = job.MaxAttempts,
        CreatedAt = job.CreatedAt,
        UpdatedAt = job.UpdatedAt,
        AvailableAt = job.AvailableAt,
        LockedUntil = job.LockedUntil,
        Result = job.Result,
        Error = job.Error
    };
}

static long ToRelativeTick(DateTimeOffset value, DateTimeOffset simulationStart)
{
    return Math.Max(0, value.Ticks - simulationStart.Ticks);
}

static double Percentile(IReadOnlyList<double> values, double percentile)
{
    if (values.Count == 0)
    {
        return 0;
    }

    var index = (int)Math.Ceiling(percentile * values.Count) - 1;
    return values[Math.Clamp(index, 0, values.Count - 1)];
}

public sealed record Scenario(string Name, IReadOnlyList<JobRecord> Jobs);

public sealed record RunningJob(JobRecord Job, int StartTick, int CompletesAtTick);

public sealed record CompletedJob(string TenantId, int StartTick, int CompletedTick, long LatencyTicks);

public sealed record BenchmarkResult(
    string Name,
    int CompletedJobs,
    int ActiveTenants,
    int TotalSlots,
    int TotalTicks,
    double Throughput,
    double AverageLatency,
    double P95Latency,
    double SlotUtilization,
    int FairnessSpread,
    TimeSpan Runtime);
