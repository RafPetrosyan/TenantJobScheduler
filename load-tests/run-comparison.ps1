param(
    [string]$TenantAwareUrl = "http://localhost:5080",
    [string]$HangfireUrl = "http://localhost:5090",
    [string]$Duration = "1m",
    [int]$Rate = 40
)

$scenarios = @("all-tenants", "two-tenants", "activation-burst")

foreach ($scenario in $scenarios) {
    Write-Host "Tenant-aware scheduler: $scenario"
    $env:BASE_URL = $TenantAwareUrl
    $env:SCENARIO = $scenario
    $env:DURATION = $Duration
    $env:RATE = "$Rate"
    k6 run .\load-tests\k6-tenant-fairness.js

    Write-Host "Hangfire baseline: $scenario"
    $env:BASE_URL = $HangfireUrl
    k6 run .\load-tests\k6-tenant-fairness.js
}
