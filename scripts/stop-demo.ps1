$ErrorActionPreference = "SilentlyContinue"

$targets = Get-CimInstance Win32_Process | Where-Object {
    ($_.Name -eq "dotnet.exe" -or $_.Name -like "TenantJobScheduler.*") -and
    (
        $_.CommandLine -like "*TenantJobScheduler.Api*" -or
        $_.CommandLine -like "*TenantJobScheduler.Worker*" -or
        $_.CommandLine -like "*TenantJobScheduler.QueueService*" -or
        $_.Name -like "TenantJobScheduler.Api*" -or
        $_.Name -like "TenantJobScheduler.Worker*" -or
        $_.Name -like "TenantJobScheduler.QueueService*"
    )
}

if (-not $targets) {
    Write-Host "No TenantJobScheduler demo processes were found."
    exit 0
}

foreach ($process in $targets) {
    Write-Host "Stopping $($process.Name) pid=$($process.ProcessId)"
    Stop-Process -Id $process.ProcessId -Force
}

Write-Host "Stopped demo services."
