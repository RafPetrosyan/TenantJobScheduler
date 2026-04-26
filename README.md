# Tenant Job Scheduler

Implementation for a multi-tenant asynchronous job scheduling system.

## Projects

- `TenantJobScheduler.Api` - Tenant API layer with signed `POST /jobs/signed`, `GET /jobs/{jobId}`, payload encryption, and worker callback endpoint.
- `TenantJobScheduler.QueueService` - Queue intake/scheduler/dispatcher loop with tenant-aware slot allocation, retry, exponential backoff, and dead-letter handling.
- `TenantJobScheduler.Worker` - Worker microservice that accepts dispatched jobs and reports completion through callback.
- `TenantJobScheduler.Shared` - Shared contracts, file-backed job store, AES payload protector, and scheduling algorithm.
- `TenantJobScheduler.Tests` - xUnit unit tests and SQL Server integration test using Testcontainers.
- `TenantJobScheduler.HangfireBaseline` - Hangfire baseline API used for comparison under identical load.

## Run

Use the same storage and encryption settings for all tenant-aware services.

The demo dashboard is served by the API at `http://localhost:5080/` or the HTTPS URL shown by `dotnet run`.

File-backed local mode:

```powershell
$env:JOB_STORE_PATH="C:\Users\rafpe\Documents\Codex\2026-04-26\files-mentioned-by-the-user-diplom\App_Data\jobs.json"
$env:PAYLOAD_ENCRYPTION_KEY="local-development-key"
dotnet run --project TenantJobScheduler.Api --urls http://localhost:5080
```

```powershell
$env:JOB_STORE_PATH="C:\Users\rafpe\Documents\Codex\2026-04-26\files-mentioned-by-the-user-diplom\App_Data\jobs.json"
$env:PAYLOAD_ENCRYPTION_KEY="local-development-key"
$env:WORKER_LISTEN_URL="http://localhost:5081"
$env:API_CALLBACK_BASE_URL="http://localhost:5080"
dotnet run --project TenantJobScheduler.Worker
```

```powershell
$env:JOB_STORE_PATH="C:\Users\rafpe\Documents\Codex\2026-04-26\files-mentioned-by-the-user-diplom\App_Data\jobs.json"
$env:PAYLOAD_ENCRYPTION_KEY="local-development-key"
$env:WORKER_URL="http://localhost:5081/worker/jobs"
$env:TOTAL_SLOTS="20"
dotnet run --project TenantJobScheduler.QueueService
```

SQL Server mode:

```powershell
$env:JOB_STORE_PROVIDER="SqlServer"
$env:JOB_STORE_CONNECTION_STRING="Server=localhost,1433;Database=TenantJobs;User Id=sa;Password=Your_password123;TrustServerCertificate=True"
$env:PAYLOAD_ENCRYPTION_KEY="local-development-key"
dotnet run --project TenantJobScheduler.Api --urls http://localhost:5080
```

The unsigned `POST /jobs` endpoint is disabled and returns `410 Gone`. Tenant-facing submissions use signed requests through `POST /jobs/signed`.

Signed job API flow:

- Generate a tenant key pair.
- Register only the tenant public key with `POST /tenants/register-key`.
- Submit jobs with `POST /jobs/signed`, including `tenantId`, `payload`, `timestamp`, `nonce`, and `signature`.

Demo-only custom jobs can still be added from the **Add Custom Jobs** panel for scheduling experiments.

```powershell
Invoke-RestMethod -Method Get -Uri http://localhost:5080/tenants/keys
```

Check status:

```powershell
Invoke-RestMethod -Method Get -Uri http://localhost:5080/jobs/{jobId}
```

Run verification:

```powershell
dotnet build TenantJobScheduler.sln
dotnet test TenantJobScheduler.sln --no-build
```

Run SQL Server integration tests with Testcontainers:

```powershell
$env:RUN_SQLSERVER_TESTCONTAINERS="true"
dotnet test TenantJobScheduler.Tests
```

This requires Docker to be running.

## Load Testing

Install `k6`, start the tenant-aware API, Worker, and Queue Service, then run all load scenarios.

Windows install options:

```powershell
winget install k6 --source winget
```

or:

```powershell
choco install k6
```

Official installation guide: https://grafana.com/docs/k6/latest/set-up/install-k6/

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-k6-load-tests.ps1 -BaseUrl http://localhost:5080 -Rate 40 -Duration 1m -JobDurationMs 500
```

Supported scenarios:

- `all-tenants` - 20 active tenants, expected near-even distribution.
- `two-tenants` - 2 active tenants, expected work-conserving slot usage.
- `activation-burst` - tenant activation burst, expected next-cycle scheduler response.

The script writes a Markdown report to `docs\k6-load-test-results.md` and raw k6 JSON summaries to `docs\k6-results`.

If PowerShell blocks local scripts, use the same `powershell -ExecutionPolicy Bypass -File ...` form shown above.

## Hangfire Baseline

Start Hangfire with the same SQL Server capacity and 20 workers:

```powershell
$env:HANGFIRE_CONNECTION_STRING="Server=localhost,1433;Database=HangfireJobs;User Id=sa;Password=Your_password123;TrustServerCertificate=True"
$env:HANGFIRE_WORKER_COUNT="20"
dotnet run --project TenantJobScheduler.HangfireBaseline --urls http://localhost:5090
```

Run the comparison script:

```powershell
.\load-tests\run-comparison.ps1 -TenantAwareUrl http://localhost:5080 -HangfireUrl http://localhost:5090
```

The comparison criteria are summarized in `docs/comparison-matrix.md`.

## Demo Dashboard

The UI can demonstrate:

- Different tenant activity scenarios: 20 active tenants, 2 active tenants, and activation bursts.
- Different worker capacity conditions by restarting the demo with another `-TotalSlots` value.
- HTTPS state for the current request.
- Job payload encryption through encrypted storage previews.
- Tenant isolation through `/tenants/{tenantId}/jobs`, which requires the `X-Tenant-Id` header to match the requested tenant.
- Retry/dead-letter behavior and expired worker lock recovery.
- Signed tenant submission through `/jobs/signed`; unsigned `/jobs` is disabled.

## Run On Another Computer

Prerequisites:

- Install .NET SDK 8 or newer.
- Install k6 if you want to run real HTTP load tests.
- Copy this whole folder to the target computer.
- Open PowerShell in the copied folder.

Start the complete demo with one command:

```powershell
.\scripts\run-demo.ps1 -TotalSlots 20
```

Open the UI:

```text
http://localhost:5080/
```

Stop all demo services:

```powershell
.\scripts\stop-demo.ps1
```

Run the scheduler benchmark used for chapter 4:

```powershell
.\scripts\run-benchmarks.ps1
```

The benchmark report is written to:

```text
docs\benchmark-results.md
```

Run real HTTP load tests with k6:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\run-k6-load-tests.ps1 -BaseUrl http://localhost:5080 -Rate 40 -Duration 1m -JobDurationMs 500
```

This requires k6 to be installed and the demo services to be running. The report is written to:

```text
docs\k6-load-test-results.md
```

To simulate a smaller worker pool, start the demo with another slot count:

```powershell
.\scripts\run-demo.ps1 -TotalSlots 3
```

For the simplest demo mode, no SQL Server is required; the system uses `App_Data\jobs.json`. SQL Server can still be enabled separately with `JOB_STORE_PROVIDER=SqlServer` and `JOB_STORE_CONNECTION_STRING`.
