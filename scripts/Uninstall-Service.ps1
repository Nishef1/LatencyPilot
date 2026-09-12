#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'LatencyPilot.Observation'
$managedServiceDirectory = if ([string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
    $null
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'LatencyPilot\Service'))
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    }

    & sc.exe delete $serviceName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe delete failed with exit code $LASTEXITCODE."
    }

    Write-Host 'LatencyPilot observation service registration was removed.'
}
else {
    Write-Host 'LatencyPilot observation service is not installed.'
}

if ($null -ne $managedServiceDirectory -and (Test-Path -LiteralPath $managedServiceDirectory)) {
    Remove-Item -LiteralPath $managedServiceDirectory -Recurse -Force
    Write-Host "Removed protected service payload: $managedServiceDirectory"
}
