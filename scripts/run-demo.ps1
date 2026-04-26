param(
    [int]$TotalSlots = 20,
    [string]$ApiUrl = "http://localhost:5080",
    [string]$WorkerUrl = "http://localhost:5081"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$appData = Join-Path $root "App_Data"
$logs = Join-Path $root "logs"

New-Item -ItemType Directory -Force -Path $appData | Out-Null
New-Item -ItemType Directory -Force -Path $logs | Out-Null

$env:JOB_STORE_PATH = Join-Path $appData "jobs.json"
$env:SCHEDULER_SETTINGS_PATH = Join-Path $appData "scheduler-settings.json"
$env:TENANT_PUBLIC_KEYS_PATH = Join-Path $appData "tenant-public-keys.json"
$env:PAYLOAD_ENCRYPTION_KEY = "local-development-key-change-for-production"
$env:WORKER_LISTEN_URL = $WorkerUrl
$env:API_CALLBACK_BASE_URL = $ApiUrl
$env:WORKER_URL = "$WorkerUrl/worker/jobs"
$env:TOTAL_SLOTS = "$TotalSlots"

Write-Host "Building solution..."
dotnet build (Join-Path $root "TenantJobScheduler.sln")

Write-Host "Starting API..."
Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--no-build", "--project", "TenantJobScheduler.Api", "--urls", $ApiUrl) `
    -WorkingDirectory $root `
    -RedirectStandardOutput (Join-Path $logs "api.log") `
    -RedirectStandardError (Join-Path $logs "api.err.log")

Write-Host "Starting Worker..."
Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--no-build", "--project", "TenantJobScheduler.Worker") `
    -WorkingDirectory $root `
    -RedirectStandardOutput (Join-Path $logs "worker.log") `
    -RedirectStandardError (Join-Path $logs "worker.err.log")

Write-Host "Starting Queue Service..."
Start-Process -FilePath "dotnet" `
    -ArgumentList @("run", "--no-build", "--project", "TenantJobScheduler.QueueService") `
    -WorkingDirectory $root `
    -RedirectStandardOutput (Join-Path $logs "queue.log") `
    -RedirectStandardError (Join-Path $logs "queue.err.log")

Write-Host ""
Write-Host "Demo is starting."
Write-Host "UI: $ApiUrl/"
Write-Host "Worker: $WorkerUrl"
Write-Host "Total slots: $TotalSlots"
Write-Host "Logs: $logs"
Write-Host ""
Write-Host "If the browser does not load immediately, wait a few seconds and refresh."
