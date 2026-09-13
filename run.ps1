[CmdletBinding()]
param(
    [switch]$ForceRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$appProject = Join-Path $repoRoot "src\LatencyPilot.App\LatencyPilot.App.csproj"
$serviceProject = Join-Path $repoRoot "src\LatencyPilot.Service\LatencyPilot.Service.csproj"
$appAssets = Join-Path $repoRoot "src\LatencyPilot.App\obj\project.assets.json"
$serviceAssets = Join-Path $repoRoot "src\LatencyPilot.Service\obj\project.assets.json"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK is not available on PATH. Install the SDK pinned by global.json before running this script."
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

$serviceProcess = $null

Push-Location $repoRoot
try {
    $needsRestore = $ForceRestore -or
        -not (Test-Path -LiteralPath $appAssets) -or
        -not (Test-Path -LiteralPath $serviceAssets)

    if ($needsRestore) {
        Write-Host "Restoring App and Service..."
        Invoke-DotNet restore $serviceProject
        Invoke-DotNet restore $appProject
    }

    Write-Host "Building Service..."
    Invoke-DotNet build $serviceProject --configuration Debug --no-restore

    Write-Host "Building App..."
    Invoke-DotNet build $appProject --configuration Debug --no-restore

    Write-Host "Starting LatencyPilot Service..."
    $serviceProcess = Start-Process dotnet -ArgumentList @(
        "run",
        "--project", ".\src\LatencyPilot.Service\LatencyPilot.Service.csproj",
        "--configuration", "Debug",
        "--no-build",
        "--no-restore"
    ) -NoNewWindow -PassThru

    Start-Sleep -Milliseconds 750
    $serviceProcess.Refresh()
    if ($serviceProcess.HasExited) {
        throw "LatencyPilot.Service exited during startup with code $($serviceProcess.ExitCode)."
    }

    Write-Host "Starting LatencyPilot App..."
    Invoke-DotNet run --project $appProject --configuration Debug --no-build --no-restore
}
finally {
    if ($null -ne $serviceProcess) {
        $serviceProcess.Refresh()
        if (-not $serviceProcess.HasExited) {
            Write-Host "Stopping LatencyPilot Service..."
            & taskkill.exe /PID $serviceProcess.Id /T /F 2>$null | Out-Null
        }
    }

    Pop-Location
}
