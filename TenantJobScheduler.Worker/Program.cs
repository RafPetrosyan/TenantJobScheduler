using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using TenantJobScheduler.Shared;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("WORKER_LISTEN_URL") ?? "http://localhost:5081");

builder.Services.AddSingleton<IJobStore>(_ => JobStoreFactory.CreateAsync().GetAwaiter().GetResult());
builder.Services.AddHttpClient();

var app = builder.Build();

app.MapPost("/worker/jobs", async (
    WorkerJobRequest request,
    IJobStore store,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    await Task.Yield();
    _ = Task.Run(async () => await ExecuteJobAsync(request, store, httpClientFactory), cancellationToken);
    return Results.Accepted();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static async Task ExecuteJobAsync(
    WorkerJobRequest request,
    IJobStore store,
    IHttpClientFactory httpClientFactory)
{
    try
    {
        if (ContainsMode(request.Payload, "stuck"))
        {
            return;
        }

        if (ContainsMode(request.Payload, "fail"))
        {
            throw new InvalidOperationException("Demo failure requested by payload.");
        }

        var durationMs = ExtractDuration(request.Payload);
        await Task.Delay(durationMs);

        var result = $"Job {request.JobId} completed for tenant {request.TenantId}.";
        await SendCallbackOrUpdateStoreAsync(
            request.JobId,
            new WorkerCallbackRequest(JobStatus.Completed, result, null),
            store,
            httpClientFactory,
            CancellationToken.None);
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        await SendCallbackOrUpdateStoreAsync(
            request.JobId,
            new WorkerCallbackRequest(JobStatus.Failed, null, exception.Message),
            store,
            httpClientFactory,
            CancellationToken.None);
    }
}

static bool ContainsMode(string payload, string mode)
{
    var marker = $"\"mode\":\"{mode}\"";
    return payload.Contains(marker, StringComparison.OrdinalIgnoreCase);
}

static int ExtractDuration(string payload)
{
    const int defaultDuration = 500;
    const int maxDuration = 10_000;

    var marker = "\"durationMs\":";
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

static async Task SendCallbackOrUpdateStoreAsync(
    Guid jobId,
    WorkerCallbackRequest callback,
    IJobStore store,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken)
{
    var callbackBaseUrl = Environment.GetEnvironmentVariable("API_CALLBACK_BASE_URL") ?? "http://localhost:5080";
    var callbackUrl = $"{callbackBaseUrl.TrimEnd('/')}/internal/jobs/{jobId}/status";

    try
    {
        var httpClient = httpClientFactory.CreateClient();
        var response = await httpClient.PostAsJsonAsync(callbackUrl, callback, cancellationToken);
        response.EnsureSuccessStatusCode();
        return;
    }
    catch
    {
        var job = await store.GetAsync(jobId, cancellationToken);
        if (job is null)
        {
            return;
        }

        job.Status = callback.Status;
        job.Result = callback.Result;
        job.Error = callback.Error;
        job.AvailableAt = null;
        job.LockedUntil = null;
        await store.TryUpdateAsync(job, cancellationToken);
    }
}
