using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TenantJobScheduler.Shared;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(new JobStoreOptions
{
    FilePath = builder.Configuration["JobStore:FilePath"]
        ?? Environment.GetEnvironmentVariable("JOB_STORE_PATH")
        ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "jobs.json")
});
builder.Services.AddSingleton(new SchedulerSettingsStore(
    Environment.GetEnvironmentVariable("SCHEDULER_SETTINGS_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "scheduler-settings.json")));
builder.Services.AddSingleton(new TenantPublicKeyStore(
    Environment.GetEnvironmentVariable("TENANT_PUBLIC_KEYS_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "tenant-public-keys.json")));
builder.Services.AddSingleton<TenantSignatureVerifier>();
builder.Services.AddSingleton<IJobStore>(_ => JobStoreFactory.CreateAsync().GetAwaiter().GetResult());
builder.Services.AddSingleton(new AesPayloadProtector(
    builder.Configuration["PayloadEncryptionKey"]
    ?? Environment.GetEnvironmentVariable("PAYLOAD_ENCRYPTION_KEY")
    ?? "development-only-key-change-me"));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPost("/jobs", () => Results.StatusCode(StatusCodes.Status410Gone));

app.MapPost("/tenants/register-key", async (
    RegisterTenantKeyRequest request,
    TenantPublicKeyStore keyStore,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.PublicKeyPem))
    {
        return Results.BadRequest(new { error = "TenantId and PublicKeyPem are required." });
    }

    await keyStore.RegisterAsync(request.TenantId.Trim(), request.PublicKeyPem, cancellationToken);
    return Results.Ok(new { request.TenantId, registered = true });
});

app.MapGet("/tenants/keys", async (TenantPublicKeyStore keyStore, CancellationToken cancellationToken) =>
{
    var keys = await keyStore.ListAsync(cancellationToken);
    return Results.Ok(keys.Select(key => new
    {
        key.TenantId,
        key.RegisteredAt,
        publicKeyPreview = key.PublicKeyPem[..Math.Min(48, key.PublicKeyPem.Length)] + "..."
    }));
});

app.MapPost("/jobs/signed", async (
    SignedSubmitJobRequest request,
    TenantPublicKeyStore keyStore,
    TenantSignatureVerifier signatureVerifier,
    IJobStore store,
    AesPayloadProtector protector,
    CancellationToken cancellationToken) =>
{
    var key = await keyStore.GetAsync(request.TenantId, cancellationToken);
    if (key is null)
    {
        return Results.Unauthorized();
    }

    if (!signatureVerifier.Verify(request, key.PublicKeyPem, TimeSpan.FromMinutes(5), out var error))
    {
        return Results.BadRequest(new { error });
    }

    var payloadJson = TenantSignatureVerifier.NormalizePayload(request.Payload);
    var job = new JobRecord
    {
        TenantId = request.TenantId.Trim(),
        EncryptedPayload = protector.Protect(payloadJson),
        Status = JobStatus.Queued
    };

    await store.AddAsync(job, cancellationToken);
    return Results.Accepted($"/jobs/{job.Id}", new SubmitJobResponse(job.Id, job.Status));
});

app.MapGet("/jobs/{jobId:guid}", async (Guid jobId, IJobStore store, CancellationToken cancellationToken) =>
{
    var job = await store.GetAsync(jobId, cancellationToken);
    return job is null ? Results.NotFound() : Results.Ok(ToResponse(job));
});

app.MapGet("/demo/jobs", async (IJobStore store, CancellationToken cancellationToken) =>
{
    var jobs = await store.ListAsync(cancellationToken);
    return Results.Ok(jobs.Select(ToDemoResponse));
});

app.MapGet("/tenants/{tenantId}/jobs", async (
    string tenantId,
    HttpRequest request,
    IJobStore store,
    CancellationToken cancellationToken) =>
{
    var callerTenant = request.Headers["X-Tenant-Id"].ToString();
    if (!string.Equals(callerTenant, tenantId, StringComparison.OrdinalIgnoreCase))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var jobs = await store.ListAsync(cancellationToken);
    return Results.Ok(jobs
        .Where(job => string.Equals(job.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
        .Select(ToDemoResponse));
});

app.MapPost("/demo/scenarios", async (
    DemoScenarioRequest request,
    IJobStore store,
    SchedulerSettingsStore settingsStore,
    AesPayloadProtector protector,
    CancellationToken cancellationToken) =>
{
    await store.ClearAsync(cancellationToken);

    var tenantCount = request.Scenario switch
    {
        "two-tenants" => 2,
        "activation-burst" => 20,
        _ => 20
    };
    var jobsPerTenant = request.Scenario switch
    {
        "two-tenants" => 10,
        "activation-burst" => 3,
        _ => 1
    };

    var created = new List<JobRecord>();
    for (var tenantIndex = 1; tenantIndex <= tenantCount; tenantIndex++)
    {
        var tenantId = $"tenant-{tenantIndex}";
        for (var jobIndex = 1; jobIndex <= jobsPerTenant; jobIndex++)
        {
            var delay = request.Scenario == "activation-burst"
                ? TimeSpan.FromMilliseconds(tenantIndex * 100)
                : TimeSpan.Zero;
            var payload = new JsonObject
            {
                ["durationMs"] = request.DurationMs,
                ["scenario"] = request.Scenario,
                ["tenantSequence"] = tenantIndex,
                ["jobSequence"] = jobIndex
            };
            var job = new JobRecord
            {
                TenantId = tenantId,
                EncryptedPayload = protector.Protect(payload.ToJsonString()),
                Status = JobStatus.Queued,
                AvailableAt = delay == TimeSpan.Zero ? null : DateTimeOffset.UtcNow.Add(delay)
            };

            await store.AddAsync(job, cancellationToken);
            created.Add(job);
        }
    }

    var totalSlots = (await settingsStore.GetAsync(cancellationToken)).TotalSlots;
    var preview = new TenantScheduler().SelectDispatchBatch(created, totalSlots, DateTimeOffset.UtcNow);
    return Results.Ok(new
    {
        created = created.Count,
        activeTenants = tenantCount,
        totalSlots,
        preview = BuildSchedulerPreview(created, preview)
    });
});

app.MapPost("/demo/reset", async (IJobStore store, CancellationToken cancellationToken) =>
{
    await store.ClearAsync(cancellationToken);
    return Results.NoContent();
});

app.MapPost("/demo/custom-jobs", async (
    CustomJobsRequest request,
    IJobStore store,
    AesPayloadProtector protector,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.TenantId))
    {
        return Results.BadRequest(new { error = "TenantId is required." });
    }

    var count = Math.Clamp(request.Count, 1, 200);
    var durationMs = Math.Clamp(request.DurationMs, 50, 10_000);
    var created = new List<Guid>();

    for (var index = 1; index <= count; index++)
    {
        var payload = new JsonObject
        {
            ["durationMs"] = durationMs,
            ["source"] = "custom-ui",
            ["sequence"] = index
        };
        var job = new JobRecord
        {
            TenantId = request.TenantId.Trim(),
            EncryptedPayload = protector.Protect(payload.ToJsonString()),
            Status = JobStatus.Queued
        };

        await store.AddAsync(job, cancellationToken);
        created.Add(job.Id);
    }

    return Results.Accepted("/demo/jobs", new { created = created.Count, jobIds = created });
});

app.MapPost("/demo/faults", async (
    FaultDemoRequest request,
    IJobStore store,
    AesPayloadProtector protector,
    CancellationToken cancellationToken) =>
{
    var mode = request.Mode.Equals("stuck", StringComparison.OrdinalIgnoreCase) ? "stuck" : "fail";
    var payload = new JsonObject
    {
        ["durationMs"] = request.DurationMs,
        ["source"] = "fault-demo"
    };
    if (mode == "fail")
    {
        payload["mode"] = "fail";
    }

    var job = new JobRecord
    {
        TenantId = string.IsNullOrWhiteSpace(request.TenantId) ? "tenant-fault-demo" : request.TenantId.Trim(),
        EncryptedPayload = protector.Protect(payload.ToJsonString()),
        Status = mode == "stuck" && request.CreateAsRunning
            ? JobStatus.Running
            : JobStatus.Queued,
        AttemptCount = mode == "stuck" && request.CreateAsRunning ? 1 : 0,
        LockedUntil = mode == "stuck" && request.CreateAsRunning
            ? DateTimeOffset.UtcNow.AddSeconds(-1)
            : null,
        Error = mode == "stuck" && request.CreateAsRunning
            ? "Demo expired worker lock."
            : null
    };

    await store.AddAsync(job, cancellationToken);
    return Results.Accepted("/demo/jobs", new { job.Id, mode, job.Status });
});

app.MapGet("/demo/scheduler-preview", async (
    int? slots,
    IJobStore store,
    SchedulerSettingsStore settingsStore,
    CancellationToken cancellationToken) =>
{
    var jobs = await store.ListAsync(cancellationToken);
    var effectiveSlots = slots ?? (await settingsStore.GetAsync(cancellationToken)).TotalSlots;
    var selected = new TenantScheduler().SelectDispatchBatch(jobs, effectiveSlots, DateTimeOffset.UtcNow);
    return Results.Ok(BuildSchedulerPreview(jobs, selected));
});

app.MapGet("/demo/settings", async (SchedulerSettingsStore settingsStore, CancellationToken cancellationToken) =>
{
    return Results.Ok(await settingsStore.GetAsync(cancellationToken));
});

app.MapPost("/demo/settings", async (
    SchedulerSettings request,
    SchedulerSettingsStore settingsStore,
    CancellationToken cancellationToken) =>
{
    var settings = new SchedulerSettings(Math.Clamp(request.TotalSlots, 1, 100));
    await settingsStore.SaveAsync(settings, cancellationToken);
    return Results.Ok(settings);
});

app.MapGet("/demo/security", (HttpRequest request, AesPayloadProtector protector) =>
{
    const string samplePayload = """{"tenantId":"tenant-a","amount":4500,"report":"private"}""";
    var encrypted = protector.Protect(samplePayload);

    return Results.Ok(new
    {
        https = new
        {
            enabledForThisRequest = request.IsHttps,
            scheme = request.Scheme,
            note = request.IsHttps
                ? "This request is protected by HTTPS transport."
                : "Run the API with an https:// URL to show transport encryption as active."
        },
        payloadEncryption = new
        {
            plaintextVisibleInStorage = false,
            encryptedPayloadPreview = encrypted[..Math.Min(48, encrypted.Length)] + "...",
            originalLength = samplePayload.Length,
            encryptedLength = encrypted.Length
        },
        tenantIsolation = new
        {
            model = "Tenant-scoped endpoint requires X-Tenant-Id to match the route tenant. Signed jobs require tenant private-key signatures.",
            allowedExample = "/tenants/tenant-a/jobs with X-Tenant-Id: tenant-a",
            deniedExample = "/tenants/tenant-b/jobs with X-Tenant-Id: tenant-a",
            signedJobExample = "POST /jobs/signed with tenant private-key signature"
        }
    });
});

app.MapPost("/internal/jobs/{jobId:guid}/status", async (
    Guid jobId,
    WorkerCallbackRequest callback,
    IJobStore store,
    CancellationToken cancellationToken) =>
{
    var job = await store.GetAsync(jobId, cancellationToken);
    if (job is null)
    {
        return Results.NotFound();
    }

    job.Status = callback.Status;
    job.Result = callback.Result;
    job.Error = callback.Error;
    job.AvailableAt = null;
    job.LockedUntil = null;

    if (!await store.TryUpdateAsync(job, cancellationToken))
    {
        return Results.NotFound();
    }
    return Results.NoContent();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

static JobStatusResponse ToResponse(JobRecord job)
{
    return new JobStatusResponse(
        job.Id,
        job.TenantId,
        job.Status,
        job.AttemptCount,
        job.CreatedAt,
        job.UpdatedAt,
        job.Result,
        job.Error);
}

static DemoJobResponse ToDemoResponse(JobRecord job)
{
    return new DemoJobResponse(
        job.Id,
        job.TenantId,
        job.Status,
        job.AttemptCount,
        job.CreatedAt,
        job.UpdatedAt,
        job.AvailableAt,
        job.LockedUntil,
        job.EncryptedPayload[..Math.Min(32, job.EncryptedPayload.Length)] + "...",
        job.EncryptedPayload.Length,
        job.Result,
        job.Error);
}

static object BuildSchedulerPreview(IReadOnlyList<JobRecord> jobs, IReadOnlyList<JobRecord> selected)
{
    return new
    {
        selected = selected.Select(job => new { job.Id, job.TenantId, job.Status }),
        perTenant = jobs
            .GroupBy(job => job.TenantId)
            .OrderBy(group => group.Key)
            .Select(group => new
            {
                tenantId = group.Key,
                queued = group.Count(job => job.Status == JobStatus.Queued),
                running = group.Count(job => job.Status == JobStatus.Running),
                selected = selected.Count(job => job.TenantId == group.Key)
            })
    };
}

public sealed record DemoScenarioRequest(string Scenario, int DurationMs = 500);

public sealed record CustomJobsRequest(string TenantId, int Count = 1, int DurationMs = 1000);

public sealed record FaultDemoRequest(
    string Mode,
    string TenantId = "tenant-fault-demo",
    int DurationMs = 500,
    bool CreateAsRunning = false);

public sealed record DemoJobResponse(
    Guid Id,
    string TenantId,
    JobStatus Status,
    int AttemptCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? AvailableAt,
    DateTimeOffset? LockedUntil,
    string EncryptedPayloadPreview,
    int EncryptedPayloadLength,
    string? Result,
    string? Error);
