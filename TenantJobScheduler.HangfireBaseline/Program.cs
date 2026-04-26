using System.Collections.Concurrent;
using System.Text.Json;
using Hangfire;
using Hangfire.SqlServer;
using TenantJobScheduler.Shared;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("Hangfire")
    ?? Environment.GetEnvironmentVariable("HANGFIRE_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Hangfire baseline requires HANGFIRE_CONNECTION_STRING.");

builder.Services.AddHangfire(configuration => configuration
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
    {
        PrepareSchemaIfNecessary = true
    }));
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount = int.TryParse(Environment.GetEnvironmentVariable("HANGFIRE_WORKER_COUNT"), out var workers)
        ? workers
        : 20;
});
builder.Services.AddSingleton<HangfireJobRunner>();

var app = builder.Build();

app.MapPost("/jobs", (SubmitJobRequest request, IBackgroundJobClient jobs) =>
{
    if (string.IsNullOrWhiteSpace(request.TenantId))
    {
        return Results.BadRequest(new { error = "TenantId is required." });
    }

    var jobId = Guid.NewGuid();
    var payload = request.Payload.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web));
    HangfireJobRunner.Statuses[jobId] = new HangfireJobState(request.TenantId, JobStatus.Queued, null, null);

    jobs.Enqueue<HangfireJobRunner>(runner => runner.ExecuteAsync(jobId, request.TenantId, payload));
    return Results.Accepted($"/jobs/{jobId}", new SubmitJobResponse(jobId, JobStatus.Queued));
});

app.MapGet("/jobs/{jobId:guid}", (Guid jobId) =>
{
    if (!HangfireJobRunner.Statuses.TryGetValue(jobId, out var state))
    {
        return Results.NotFound();
    }

    return Results.Ok(new JobStatusResponse(
        jobId,
        state.TenantId,
        state.Status,
        1,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow,
        state.Result,
        state.Error));
});

app.MapHangfireDashboard("/hangfire");
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public sealed record HangfireJobState(string TenantId, JobStatus Status, string? Result, string? Error);

public sealed class HangfireJobRunner
{
    public static readonly ConcurrentDictionary<Guid, HangfireJobState> Statuses = new();

    public async Task ExecuteAsync(Guid jobId, string tenantId, string payload)
    {
        Statuses[jobId] = new HangfireJobState(tenantId, JobStatus.Running, null, null);

        try
        {
            await Task.Delay(ExtractDuration(payload));
            Statuses[jobId] = new HangfireJobState(tenantId, JobStatus.Completed, $"Job {jobId} completed.", null);
        }
        catch (Exception exception)
        {
            Statuses[jobId] = new HangfireJobState(tenantId, JobStatus.Failed, null, exception.Message);
            throw;
        }
    }

    private static int ExtractDuration(string payload)
    {
        const int defaultDuration = 500;
        const int maxDuration = 10_000;
        const string marker = "\"durationMs\":";

        var markerIndex = payload.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return defaultDuration;
        }

        var start = markerIndex + marker.Length;
        var end = start;
        while (end < payload.Length && char.IsDigit(payload[end]))
        {
            end++;
        }

        return int.TryParse(payload[start..end], out var duration)
            ? Math.Clamp(duration, 1, maxDuration)
            : defaultDuration;
    }
}
