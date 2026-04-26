$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")

dotnet build (Join-Path $root "TenantJobScheduler.sln")
dotnet run --project (Join-Path $root "TenantJobScheduler.Benchmarks")

Write-Host ""
Write-Host "Benchmark report:"
Write-Host (Join-Path $root "docs\benchmark-results.md")
