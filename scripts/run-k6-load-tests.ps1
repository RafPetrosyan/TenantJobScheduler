param(
    [string]$BaseUrl = "http://localhost:5080",
    [int]$Rate = 40,
    [string]$Duration = "1m",
    [int]$JobDurationMs = 500,
    [int]$TotalSlots = 20
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$scriptPath = Join-Path $root "load-tests\k6-tenant-fairness.js"
$resultsDir = Join-Path $root "docs\k6-results"
$reportPath = Join-Path $root "docs\k6-load-test-results.md"

function Get-Metric($metrics, [string]$name) {
    $property = $metrics.PSObject.Properties[$name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-PropertyValue($object, [string]$name) {
    if ($null -eq $object) {
        return 0
    }

    $property = $object.PSObject.Properties[$name]
    if ($null -eq $property) {
        return 0
    }

    return $property.Value
}

function Get-SafeCommandOutput([string]$command, [string[]]$arguments) {
    try {
        $output = & $command @arguments 2>$null
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($output)) {
            return "unknown"
        }

        return ($output -join " ").Trim()
    }
    catch {
        return "unknown"
    }
}

function Get-SystemInfo {
    $osCaption = "unknown"
    $osVersion = "unknown"
    $cpuName = "unknown"
    $logicalProcessors = "unknown"
    $memoryGb = "unknown"

    try {
        $os = Get-CimInstance Win32_OperatingSystem
        $osCaption = $os.Caption
        $osVersion = $os.Version
        $memoryGb = [math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
    }
    catch {
    }

    try {
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $computer = Get-CimInstance Win32_ComputerSystem
        $cpuName = $cpu.Name
        $logicalProcessors = $computer.NumberOfLogicalProcessors
    }
    catch {
    }

    return [PSCustomObject]@{
        Timestamp = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        MachineName = $env:COMPUTERNAME
        UserName = $env:USERNAME
        OS = "$osCaption ($osVersion)"
        CPU = $cpuName
        LogicalProcessors = $logicalProcessors
        MemoryGb = $memoryGb
        DotNetVersion = Get-SafeCommandOutput "dotnet" @("--version")
        K6Version = Get-SafeCommandOutput "k6" @("version")
    }
}

if (-not (Get-Command k6 -ErrorAction SilentlyContinue)) {
    throw "k6 is not installed or is not available in PATH. Install it from https://grafana.com/docs/k6/latest/set-up/install-k6/ and run this script again."
}

New-Item -ItemType Directory -Force -Path $resultsDir | Out-Null

$scenarios = @("all-tenants", "two-tenants", "activation-burst")
$rows = @()
$environment = Get-SystemInfo

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
    $durationValues = Get-Metric $metrics "http_req_duration"
    if ($null -eq $durationValues) {
        $durationValues = Get-Metric $metrics "http_req_duration{expected_response:true}"
    }

    $submitValues = Get-Metric $metrics "job_submit_latency"
    $failedValues = Get-Metric $metrics "http_req_failed"
    $submittedValues = Get-Metric $metrics "jobs_submitted"
    $failedRate = if ($null -ne $failedValues -and $null -ne $failedValues.PSObject.Properties["rate"]) {
        [double](Get-PropertyValue $failedValues "rate")
    } else {
        [double](Get-PropertyValue $failedValues "value")
    }

    $rows += [PSCustomObject]@{
        Scenario = $scenario
        SubmittedJobs = [int](Get-PropertyValue $submittedValues "count")
        HttpFailedRate = $failedRate
        HttpAvgMs = [double](Get-PropertyValue $durationValues "avg")
        HttpP95Ms = [double](Get-PropertyValue $durationValues "p(95)")
        SubmitAvgMs = [double](Get-PropertyValue $submitValues "avg")
        SubmitP95Ms = [double](Get-PropertyValue $submitValues "p(95)")
        Summary = $summaryPath
    }
}

$lines = @(
    "# k6 Load Test Results",
    "",
    "This report was produced by sending real HTTP requests to the Tenant API.",
    "",
    "## Test Environment",
    "",
    "| Field | Value |",
    "| --- | --- |",
    "| Timestamp | $($environment.Timestamp) |",
    "| Machine | $($environment.MachineName) |",
    "| User | $($environment.UserName) |",
    "| OS | $($environment.OS) |",
    "| CPU | $($environment.CPU) |",
    "| Logical processors | $($environment.LogicalProcessors) |",
    "| Memory GB | $($environment.MemoryGb) |",
    "| .NET SDK | $($environment.DotNetVersion) |",
    "| k6 | $($environment.K6Version) |",
    "",
    "## Load Conditions",
    "",
    "| Field | Value |",
    "| --- | --- |",
    "| Base URL | $BaseUrl |",
    "| Arrival rate | $Rate req/s |",
    "| Duration per scenario | $Duration |",
    "| Job duration | $JobDurationMs ms |",
    "| Queue worker slots | $TotalSlots |",
    "| Scenarios | all-tenants, two-tenants, activation-burst |",
    "",
    "## Results",
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
