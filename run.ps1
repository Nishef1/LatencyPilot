[CmdletBinding()]
param(
    [switch]$ForceRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $repoRoot 'src\LatencyPilot.App\LatencyPilot.App.csproj'
$serviceProject = Join-Path $repoRoot 'src\LatencyPilot.Service\LatencyPilot.Service.csproj'
$appAssets = Join-Path $repoRoot 'src\LatencyPilot.App\obj\project.assets.json'
$serviceAssets = Join-Path $repoRoot 'src\LatencyPilot.Service\obj\project.assets.json'
$serviceOutput = Join-Path $repoRoot 'src\LatencyPilot.Service\bin\Debug\net10.0-windows10.0.26100.0\win-x64'
$installServiceScript = Join-Path $repoRoot 'scripts\Install-Service.ps1'
$serviceStartupReadinessScript = Join-Path $repoRoot 'scripts\ServiceStartupReadiness.ps1'
$serviceName = 'LatencyPilot.Observation'
$serviceLogDirectory = Join-Path (
    [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)) 'LatencyPilot\Logs\Service'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is not available on PATH. Install the SDK pinned by global.json before running this script.'
}

if (-not (Test-Path -LiteralPath $serviceStartupReadinessScript -PathType Leaf)) {
    throw "Service startup readiness helper was not found: $serviceStartupReadinessScript"
}
. $serviceStartupReadinessScript

function Test-IsElevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (Test-IsElevated) {
    throw 'Run run.ps1 from a normal non-elevated terminal. The script requests UAC only for the protected observation Service; the WinUI App must remain non-elevated.'
}

function Invoke-DotNet {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repoRoot
try {
    $needsRestore = $ForceRestore -or
        -not (Test-Path -LiteralPath $appAssets) -or
        -not (Test-Path -LiteralPath $serviceAssets)

    if ($needsRestore) {
        Write-Host 'Restoring App and Service...'
        Invoke-DotNet restore $serviceProject
        Invoke-DotNet restore $appProject
    }

    Write-Host 'Building Service...'
    Invoke-DotNet build $serviceProject --configuration Debug --no-restore

    Write-Host 'Building App...'
    Invoke-DotNet build $appProject --configuration Debug --no-restore

    $serviceExe = Join-Path $serviceOutput 'LatencyPilot.Service.exe'
    if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
        throw "Service build succeeded but its executable was not found: $serviceExe"
    }

    Write-Host 'Updating the protected LocalSystem observation Service (UAC may prompt)...' -ForegroundColor Yellow
    $serviceInstallStartedAt = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$installServiceScript`" -SourceServiceDirectory `"$serviceOutput`""
    $installer = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    if ($installer.ExitCode -ne 0) {
        throw "Protected Service installation failed with exit code $($installer.ExitCode)."
    }

    $service = Get-Service -Name $serviceName -ErrorAction Stop
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        throw "Protected Service installation returned successfully, but $serviceName is $($service.Status), not Running."
    }
    Write-Host "Protected Service state: Running ($serviceName)" -ForegroundColor Green

    try {
        $readiness = Wait-LatencyPilotServiceStartupReadiness `
            -LogDirectory $serviceLogDirectory `
            -NotBeforeUtc $serviceInstallStartedAt `
            -Timeout ([TimeSpan]::FromSeconds(8))

        if ($readiness.UnresolvedCount -eq 0) {
            Write-Host 'Mutation journal startup inspection: READY (0 unresolved experiments; mutation remains unavailable).' -ForegroundColor Green
        }
        else {
            Write-Warning "Mutation journal startup inspection completed with $($readiness.UnresolvedCount) unresolved experiment(s). Observation can continue, but mutation must remain blocked until recovery is explicit."
        }
        Write-Host "Service readiness evidence: $($readiness.LogPath)" -ForegroundColor DarkGray
    }
    catch {
        Write-Warning "The Service is running, but current mutation-journal startup readiness could not be proven from structured logs: $($_.Exception.Message)"
        Write-Warning 'The App will still open for read-only diagnosis; do not treat this run as mutation-arming evidence.'
    }

    Write-Host 'Starting non-elevated LatencyPilot App...'
    Invoke-DotNet run --project $appProject --configuration Debug --no-build --no-restore
}
finally {
    Pop-Location
}
