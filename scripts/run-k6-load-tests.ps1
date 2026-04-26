param(
    [string]$BaseUrl = "http://localhost:5080",
    [int]$Rate = 40,
    [string]$Duration = "1m",
    [int]$JobDurationMs = 500
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$scriptPath = Join-Path $root "load-tests\k6-tenant-fairness.js"
$resultsDir = Join-Path $root "docs\k6-results"
$reportPath = Join-Path $root "docs\k6-load-test-results.md"

if (-not (Get-Command k6 -ErrorAction SilentlyContinue)) {
    throw "k6 is not installed or is not available in PATH. Install it from https://grafana.com/docs/k6/latest/set-up/install-k6/ and run this script again."
}

New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$scenarios = @("all-tenants", "two-tenants", "activation-burst")
$rows = @()

foreach ($scenario in $scenarios) {
    $summaryPath = Join-Path $resultsDir "$scenario-summary.json"

    Write-Host ""
    Write-Host "Running k6 scenario: $scenario"
    k6 run `
        -e BASE_URL=$BaseUrl `
        -e SCENARIO=$scenario `
        -e RATE=$Rate `
        -e DURATION=$Duration `
        -e JOB_DURATION_MS=$JobDurationMs `
        --summary-export $summaryPath `
        $scriptPath

    $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
    $metrics = $summary.metrics
    $durationValues = $metrics.http_req_duration.values
    $submitValues = $metrics.job_submit_latency.values
    $failedValues = $metrics.http_req_failed.values
    $submittedValues = $metrics.jobs_submitted.values

    $rows += [PSCustomObject]@{
        Scenario = $scenario
        SubmittedJobs = [int]$submittedValues.count
        HttpFailedRate = [double]$failedValues.rate
        HttpAvgMs = [double]$durationValues.avg
        HttpP95Ms = [double]$durationValues.'p(95)'
        SubmitAvgMs = [double]$submitValues.avg
        SubmitP95Ms = [double]$submitValues.'p(95)'
        Summary = $summaryPath
    }
}

$lines = @(
    "# k6 Load Test Results",
    "",
    "This report was produced by sending real HTTP requests to the Tenant API.",
    "",
    "Parameters: BaseUrl=$BaseUrl, Rate=$Rate req/s, Duration=$Duration, JobDurationMs=$JobDurationMs.",
    "",
    "| Scenario | Submitted jobs | HTTP failed rate | HTTP avg ms | HTTP p95 ms | Submit avg ms | Submit p95 ms |",
    "| --- | ---: | ---: | ---: | ---: | ---: | ---: |"
)

foreach ($row in $rows) {
    $lines += "| $($row.Scenario) | $($row.SubmittedJobs) | $($row.HttpFailedRate.ToString("P2")) | $($row.HttpAvgMs.ToString("F2")) | $($row.HttpP95Ms.ToString("F2")) | $($row.SubmitAvgMs.ToString("F2")) | $($row.SubmitP95Ms.ToString("F2")) |"
}

$lines += @(
    "",
    "Notes:",
    "",
    "- all-tenants checks concurrent activity from 20 tenants.",
    "- two-tenants checks work-conserving behavior when only 2 tenants are active.",
    "- activation-burst checks API and queue response when tenants become active in bursts.",
    "",
    "Raw JSON summaries are stored in the docs\k6-results folder."
)

Set-Content -Path $reportPath -Value ($lines -join [Environment]::NewLine) -Encoding UTF8

Write-Host ""
Write-Host "k6 report:"
Write-Host $reportPath
