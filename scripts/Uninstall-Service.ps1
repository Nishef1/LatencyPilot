#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$serviceName = 'LatencyPilot.Observation'
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -eq $existing) {
    Write-Host 'LatencyPilot observation service is not installed.'
    exit 0
}

if ($existing.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
}

& sc.exe delete $serviceName | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "sc.exe delete failed with exit code $LASTEXITCODE."
}

Write-Host 'LatencyPilot observation service was removed. Release files were not deleted.'
