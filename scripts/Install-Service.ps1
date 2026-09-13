#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$SourceServiceDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$serviceName = 'LatencyPilot.Observation'
$displayName = 'LatencyPilot Observation Service'

if ([string]::IsNullOrWhiteSpace($SourceServiceDirectory)) {
    $SourceServiceDirectory = Join-Path $PSScriptRoot 'Service'
}

$sourceServiceDirectory = [System.IO.Path]::GetFullPath($SourceServiceDirectory).TrimEnd('\')
$sourceServiceExe = Join-Path $sourceServiceDirectory 'LatencyPilot.Service.exe'

if (-not (Test-Path -LiteralPath $sourceServiceExe -PathType Leaf)) {
    throw "Service executable not found: $sourceServiceExe"
}

if ([string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
    throw 'Program Files could not be resolved for the protected service installation path.'
}

$managedServiceDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $env:ProgramFiles 'LatencyPilot\Service')).TrimEnd('\')
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

if ($null -ne $existing -and $existing.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
}

if (-not [string]::Equals(
    $sourceServiceDirectory,
    $managedServiceDirectory,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    if (Test-Path -LiteralPath $managedServiceDirectory) {
        Remove-Item -LiteralPath $managedServiceDirectory -Recurse -Force
    }

    New-Item -ItemType Directory -Path $managedServiceDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $sourceServiceDirectory '*') -Destination $managedServiceDirectory -Recurse -Force
}

$serviceExe = Join-Path $managedServiceDirectory 'LatencyPilot.Service.exe'
if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
    throw "Protected service executable was not installed: $serviceExe"
}

$serviceExe = [System.IO.Path]::GetFullPath($serviceExe)
$quotedBinPath = '"' + $serviceExe + '"'

if ($null -ne $existing) {
    & sc.exe config $serviceName binPath= $quotedBinPath start= demand obj= LocalSystem DisplayName= $displayName | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe config failed with exit code $LASTEXITCODE."
    }
}
else {
    & sc.exe create $serviceName binPath= $quotedBinPath start= demand obj= LocalSystem DisplayName= $displayName | Out-Host
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

Write-Host "LatencyPilot observation service is running from protected path: $serviceExe"
