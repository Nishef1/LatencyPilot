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
$isManagedSource = [string]::Equals(
    $sourceServiceDirectory,
    $managedServiceDirectory,
    [System.StringComparison]::OrdinalIgnoreCase)
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue

function Assert-RecoverySafeForReplacement {
    $installedServiceExe = Join-Path $managedServiceDirectory 'LatencyPilot.Service.exe'
    if (-not (Test-Path -LiteralPath $installedServiceExe -PathType Leaf)) {
        throw 'Existing LatencyPilot recovery tools are incomplete. Repair the current installation before replacing the Service.'
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $installedServiceExe
    $startInfo.Arguments = '--check-uninstall'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $checker = New-Object System.Diagnostics.Process
    $checker.StartInfo = $startInfo
    try {
        if (-not $checker.Start()) {
            throw 'Existing LatencyPilot recovery checker could not start.'
        }
        $outputTask = $checker.StandardOutput.ReadToEndAsync()
        $errorTask = $checker.StandardError.ReadToEndAsync()
        if (-not $checker.WaitForExit(15000)) {
            $checker.Kill()
            throw 'Existing LatencyPilot recovery checker timed out.'
        }
        $output = $outputTask.GetAwaiter().GetResult().Trim()
        $details = $errorTask.GetAwaiter().GetResult().Trim()
        if ($checker.ExitCode -ne 0 -or $output -ne 'LATENCYPILOT_UNINSTALL_SAFE_V1') {
            throw "Service replacement is blocked until all managed changes are restored and the mutation journal is healthy. $details"
        }
    }
    finally {
        $checker.Dispose()
    }
}

# Portable/manual upgrades copy a new Service over the protected recovery host.
# Prove the old recovery state is clean before stopping or replacing it. Inno
# Setup performs the equivalent pre-copy check when source and destination are
# already the managed installation directory.
if (-not $isManagedSource -and
    ((Test-Path -LiteralPath $managedServiceDirectory -PathType Container) -or $null -ne $existing)) {
    Assert-RecoverySafeForReplacement
}

if ($null -ne $existing -and $existing.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
}

if (-not $isManagedSource) {
    # Re-check after service shutdown so a journal update during stop cannot be missed.
    if ((Test-Path -LiteralPath $managedServiceDirectory -PathType Container) -or $null -ne $existing) {
        Assert-RecoverySafeForReplacement
    }

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
