#Requires -RunAsAdministrator
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$serviceName = 'LatencyPilot.Observation'
$displayName = 'LatencyPilot Observation Service'
$serviceExe = Join-Path $PSScriptRoot 'Service\LatencyPilot.Service.exe'

if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
    throw "Service executable not found: $serviceExe"
}

$serviceExe = [System.IO.Path]::GetFullPath($serviceExe)
$quotedBinPath = '"' + $serviceExe + '"'
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -ne $existing) {
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    }

    & sc.exe config $serviceName binPath= $quotedBinPath start= demand DisplayName= $displayName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe config failed with exit code $LASTEXITCODE."
    }
}
else {
    & sc.exe create $serviceName binPath= $quotedBinPath start= demand DisplayName= $displayName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe create failed with exit code $LASTEXITCODE."
    }
}

& sc.exe description $serviceName 'Privileged read-only kernel observation host for LatencyPilot.' | Out-Host
if ($LASTEXITCODE -ne 0) {
    throw "sc.exe description failed with exit code $LASTEXITCODE."
}

Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus('Running', [TimeSpan]::FromSeconds(15))

Write-Host "LatencyPilot observation service is running from: $serviceExe"
Write-Warning 'Keep this extracted release directory in place while the service is installed.'
