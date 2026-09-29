[CmdletBinding()]
param(
    [switch]$ForceRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $repoRoot 'src\LatencyPilot.App\LatencyPilot.App.csproj'
$serviceProject = Join-Path $repoRoot 'src\LatencyPilot.Service\LatencyPilot.Service.csproj'
$gateAProject = Join-Path $repoRoot 'tools\LatencyPilot.GateAValidation\LatencyPilot.GateAValidation.csproj'
$appAssets = Join-Path $repoRoot 'src\LatencyPilot.App\obj\project.assets.json'
$serviceAssets = Join-Path $repoRoot 'src\LatencyPilot.Service\obj\project.assets.json'
$gateAAssets = Join-Path $repoRoot 'tools\LatencyPilot.GateAValidation\obj\project.assets.json'
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
        -not (Test-Path -LiteralPath $serviceAssets) -or
        -not (Test-Path -LiteralPath $gateAAssets)

    if ($needsRestore) {
        Write-Host 'Restoring App, Service, and Gate A helper...'
        Invoke-DotNet restore $serviceProject
        Invoke-DotNet restore $appProject
        Invoke-DotNet restore $gateAProject
    }

    Write-Host 'Building Service...'
    Invoke-DotNet build $serviceProject --configuration Debug --no-restore

    Write-Host 'Building App...'
    Invoke-DotNet build $appProject --configuration Debug --no-restore

    Write-Host 'Building Gate A helper...'
    Invoke-DotNet build $gateAProject --configuration Release --no-restore

    $serviceExe = Join-Path $serviceOutput 'LatencyPilot.Service.exe'
    if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
        throw "Service build succeeded but its executable was not found: $serviceExe"
    }

    Write-Host 'Updating the protected LocalSystem observation Service (UAC may prompt)...' -ForegroundColor Yellow
    $serviceInstallStartedAt = [DateTimeOffset]::UtcNow.AddSeconds(-1)
    $serviceUpdateDeferredForRecovery = $false
    $serviceInstallResultPath = Join-Path (
        [System.IO.Path]::GetTempPath()) "latencypilot-service-install-$([Guid]::NewGuid().ToString('N')).json"
    try {
        $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$installServiceScript`" -SourceServiceDirectory `"$serviceOutput`" -ResultPath `"$serviceInstallResultPath`""
        $installer = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru

        $installDetail = $null
        $installStatus = $null
        if (Test-Path -LiteralPath $serviceInstallResultPath -PathType Leaf) {
            try {
                $installResult = Get-Content -LiteralPath $serviceInstallResultPath -Raw -ErrorAction Stop |
                    ConvertFrom-Json -ErrorAction Stop
                $installDetail = [string]$installResult.message
                $installStatus = [string]$installResult.status
            }
            catch {
                $installDetail = Get-Content -LiteralPath $serviceInstallResultPath -Raw -ErrorAction SilentlyContinue
            }
        }

        if ($installer.ExitCode -ne 0) {
            $replacementBlockedForRecovery =
                -not [string]::IsNullOrWhiteSpace($installDetail) -and
                $installDetail.StartsWith(
                    'Service replacement is blocked until all managed changes are restored and the mutation journal is healthy.',
                    [System.StringComparison]::Ordinal)

            if ($replacementBlockedForRecovery) {
                $existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
                if ($null -ne $existingService -and
                    $existingService.Status -eq [System.ServiceProcess.ServiceControllerStatus]::Running) {
                    $serviceUpdateDeferredForRecovery = $true
                    Write-Warning "Protected Service update was deferred because LatencyPilot owns an unresolved journaled change. The existing running recovery host is preserved so the App can resume or restore that experiment. Detail: $installDetail"
                }
                else {
                    throw "Protected Service replacement is blocked by recovery work and no existing Running recovery host is available. $installDetail"
                }
            }
            elseif (-not [string]::IsNullOrWhiteSpace($installDetail)) {
                throw "Protected Service installation failed: $installDetail"
            }
            else {
                throw "Protected Service installation failed with exit code $($installer.ExitCode). The elevated installer returned no diagnostic result."
            }
        }
        elseif ([string]::Equals($installStatus, 'Deferred', [System.StringComparison]::Ordinal)) {
            $serviceUpdateDeferredForRecovery = $true
            Write-Warning "Protected Service replacement was deferred because LatencyPilot started the existing recovery host. $installDetail"
        }
    }
    finally {
        Remove-Item -LiteralPath $serviceInstallResultPath -Force -ErrorAction SilentlyContinue
    }

    $service = Get-Service -Name $serviceName -ErrorAction Stop
    if ($service.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Running) {
        throw "Protected Service is $($service.Status), not Running."
    }

    if ($serviceUpdateDeferredForRecovery) {
        Write-Host "Protected Service state: Running ($serviceName; update deferred for journal recovery)" -ForegroundColor Yellow
        Write-Warning 'Mutation remains fail-closed until the pending experiment is resumed or restored.'
    }
    else {
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
    }

    Write-Host 'Starting non-elevated LatencyPilot App...'
    Invoke-DotNet run --project $appProject --configuration Debug --no-build --no-restore
}
finally {
    Pop-Location
}
