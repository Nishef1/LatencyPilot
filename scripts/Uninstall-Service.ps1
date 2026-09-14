#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [switch]$CheckOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$serviceName = 'LatencyPilot.Observation'
if ([string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
    throw 'Program Files could not be resolved. Recovery tools must remain installed.'
}
$protectedRoot = [System.IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\')
$managedServiceDirectory = [System.IO.Path]::GetFullPath((Join-Path $protectedRoot 'LatencyPilot\Service'))
if (-not $managedServiceDirectory.StartsWith($protectedRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The managed Service directory is outside Program Files.'
}

function Assert-UninstallSafe {
    $serviceExe = Join-Path $managedServiceDirectory 'LatencyPilot.Service.exe'
    if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
        throw 'The Service uninstall checker is unavailable. Repair the Service installation before removing recovery tools.'
    }

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $serviceExe
    $startInfo.Arguments = '--check-uninstall'
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $checker = New-Object System.Diagnostics.Process
    $checker.StartInfo = $startInfo
    try {
        if (-not $checker.Start()) {
            throw 'The Service uninstall checker could not start.'
        }
        $outputTask = $checker.StandardOutput.ReadToEndAsync()
        $errorTask = $checker.StandardError.ReadToEndAsync()
        if (-not $checker.WaitForExit(15000)) {
            $checker.Kill()
            throw 'The Service uninstall checker timed out. Recovery tools must remain installed.'
        }
        $output = $outputTask.GetAwaiter().GetResult().Trim()
        $details = $errorTask.GetAwaiter().GetResult().Trim()
        if ($checker.ExitCode -ne 0 -or $output -ne 'LATENCYPILOT_UNINSTALL_SAFE_V1') {
            throw "Uninstall safety could not be established. Recovery tools must remain installed. $details"
        }
    }
    finally {
        $checker.Dispose()
    }
}

Assert-UninstallSafe
if ($CheckOnly) {
    Write-Host 'The mutation journal permits Service removal.'
    return
}

$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    $wasRunning = $existing.Status -ne 'Stopped'
    if ($existing.Status -ne 'Stopped') {
        Stop-Service -Name $serviceName -Force
        $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    }

    try {
        # Re-read after shutdown so a journal update during stop cannot be missed.
        Assert-UninstallSafe
    }
    catch {
        if ($wasRunning) {
            Start-Service -Name $serviceName -ErrorAction Continue
        }
        throw
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

if (Test-Path -LiteralPath $managedServiceDirectory) {
    Remove-Item -LiteralPath $managedServiceDirectory -Recurse -Force
    Write-Host "Removed protected service payload: $managedServiceDirectory"
}
