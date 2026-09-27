#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [string]$SourceServiceDirectory,
    [string]$ResultPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$serviceName = 'LatencyPilot.Observation'
$displayName = 'LatencyPilot Observation Service'

function Write-InstallResult {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Status,
        [Parameter(Mandatory = $true)]
        [string]$Message
    )

    if ([string]::IsNullOrWhiteSpace($ResultPath)) {
        return
    }

    $resultDirectory = Split-Path -Parent $ResultPath
    if (-not [string]::IsNullOrWhiteSpace($resultDirectory)) {
        New-Item -ItemType Directory -Path $resultDirectory -Force | Out-Null
    }

    $payload = [ordered]@{
        schema = 'latencypilot-service-install-result-v1'
        status = $Status
        message = $Message
        serviceName = $serviceName
        timestampUtc = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json -Compress

    [System.IO.File]::WriteAllText(
        $ResultPath,
        $payload,
        [System.Text.UTF8Encoding]::new($false))
}

trap {
    $message = $_.Exception.Message
    try {
        Write-InstallResult -Status 'Failed' -Message $message
    }
    catch {
        # Installation failure remains authoritative even if diagnostics cannot be persisted.
    }

    [Console]::Error.WriteLine($message)
    exit 1
}

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
$programDataStateDirectory = Join-Path $env:ProgramData 'LatencyPilot'
$hasProgramDataState = Test-Path -LiteralPath $programDataStateDirectory -PathType Container

# A stale protected directory without a Service registration or any
# ProgramData state is an incomplete first-install residue, not an existing
# recovery installation. There is no journal-owned mutation to recover in
# this state, so it may be replaced. Once either the Service or its state
# footprint exists, the recovery checker remains mandatory.
$isFreshInstall = $null -eq $existing -and -not $hasProgramDataState

function Assert-RecoverySafeForReplacement {
    $installedServiceExe = Join-Path $managedServiceDirectory 'LatencyPilot.Service.exe'
    $checkerExecutable = $installedServiceExe
    $checkerDescription = 'installed recovery host'

    if (-not (Test-Path -LiteralPath $installedServiceExe -PathType Leaf)) {
        # A partially removed/corrupted protected directory cannot repair itself
        # because its old recovery host is gone. The freshly built Service exposes
        # the same read-only --check-uninstall contract, so use it only to inspect
        # the shared ProgramData journal. Replacement is still blocked unless that
        # checker proves there are no retained or unresolved managed changes.
        $checkerExecutable = $sourceServiceExe
        $checkerDescription = 'freshly built read-only recovery checker'
        Write-Warning 'Installed LatencyPilot recovery host is missing. Using the freshly built Service only to verify the mutation journal before repairing the protected installation.'
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $checkerExecutable
    $startInfo.Arguments = '--check-uninstall'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $checker = New-Object System.Diagnostics.Process
    $checker.StartInfo = $startInfo
    try {
        if (-not $checker.Start()) {
            throw "LatencyPilot $checkerDescription could not start."
        }
        $outputTask = $checker.StandardOutput.ReadToEndAsync()
        $errorTask = $checker.StandardError.ReadToEndAsync()
        if (-not $checker.WaitForExit(15000)) {
            $checker.Kill()
            throw "LatencyPilot $checkerDescription timed out."
        }
        $output = $outputTask.GetAwaiter().GetResult().Trim()
        $details = $errorTask.GetAwaiter().GetResult().Trim()
        if ($checker.ExitCode -ne 0 -or $output -ne 'LATENCYPILOT_UNINSTALL_SAFE_V1') {
            $detailSuffix = if ([string]::IsNullOrWhiteSpace($details)) {
                "The $checkerDescription did not return the expected safety token."
            }
            else {
                $details
            }
            throw "Service replacement is blocked until all managed changes are restored and the mutation journal is healthy. $detailSuffix"
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
if (-not $isManagedSource -and -not $isFreshInstall -and
    ((Test-Path -LiteralPath $managedServiceDirectory -PathType Container) -or $null -ne $existing)) {
    Assert-RecoverySafeForReplacement
}

if ($null -ne $existing -and $existing.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
}

if (-not $isManagedSource) {
    # Re-check after service shutdown so a journal update during stop cannot be missed.
    if (-not $isFreshInstall -and
        ((Test-Path -LiteralPath $managedServiceDirectory -PathType Container) -or $null -ne $existing)) {
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

$successMessage = "LatencyPilot observation service is running from protected path: $serviceExe"
Write-Host $successMessage
try {
    Write-InstallResult -Status 'Succeeded' -Message $successMessage
}
catch {
    Write-Warning "Service installation succeeded, but the diagnostic result could not be persisted: $($_.Exception.Message)"
}
