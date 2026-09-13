[CmdletBinding()]
param(
    [ValidateRange(2, 60)]
    [int]$PollSeconds = 5,

    [switch]$ForceRestore,

    [switch]$NoAutoPull
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

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw "Git is not available on PATH."
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

function Test-WorkingTreeClean {
    $status = & git status --porcelain --untracked-files=normal
    if ($LASTEXITCODE -ne 0) {
        throw "git status failed with exit code $LASTEXITCODE."
    }

    return [string]::IsNullOrWhiteSpace(($status -join "`n"))
}

function Get-HeadSha {
    $sha = & git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) {
        throw "git rev-parse HEAD failed with exit code $LASTEXITCODE."
    }

    return ($sha | Select-Object -First 1).Trim()
}

function Get-RemoteHeadSha {
    $sha = & git rev-parse origin/main 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return ($sha | Select-Object -First 1).Trim()
}

function Test-LocalHeadIsAncestorOfRemote {
    & git merge-base --is-ancestor HEAD origin/main 2>$null
    return $LASTEXITCODE -eq 0
}

function Get-IncomingPaths {
    $paths = & git diff --name-only HEAD..origin/main
    if ($LASTEXITCODE -ne 0) {
        throw "git diff HEAD..origin/main failed with exit code $LASTEXITCODE."
    }

    return @($paths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Test-RequiresControlledRestart {
    param([string[]]$Paths)

    foreach ($path in $Paths) {
        if ($path -match '^src/LatencyPilot\.(Service|Core|Benchmarking|Protocol|Platform\.Windows|Persistence)/' -or
            $path -match '\.(csproj|props|targets)$' -or
            $path -in @('global.json', 'LatencyPilot.slnx', 'Directory.Build.props', 'Directory.Build.targets')) {
            return $true
        }
    }

    return $false
}

function Test-RequiresRestore {
    param([string[]]$Paths)

    foreach ($path in $Paths) {
        if ($path -match '\.(csproj|props|targets)$' -or
            $path -in @('global.json', 'LatencyPilot.slnx', 'Directory.Build.props', 'Directory.Build.targets')) {
            return $true
        }
    }

    return $false
}

function Stop-ProcessTree {
    param(
        [System.Diagnostics.Process]$Process,
        [string]$Name
    )

    if ($null -eq $Process) {
        return
    }

    try {
        $Process.Refresh()
        if (-not $Process.HasExited) {
            Write-Host "Stopping $Name..."
            & taskkill.exe /PID $Process.Id /T /F 2>$null | Out-Null
        }
    }
    catch {
        Write-Warning "Could not stop $Name cleanly: $($_.Exception.Message)"
    }
}

function Start-ServiceProcess {
    Write-Host "Starting LatencyPilot Service..."
    $process = Start-Process dotnet -ArgumentList @(
        "run",
        "--project", ".\src\LatencyPilot.Service\LatencyPilot.Service.csproj",
        "--configuration", "Debug",
        "--no-build",
        "--no-restore"
    ) -NoNewWindow -PassThru

    Start-Sleep -Milliseconds 750
    $process.Refresh()
    if ($process.HasExited) {
        throw "LatencyPilot.Service exited during startup with code $($process.ExitCode)."
    }

    return $process
}

function Start-AppWatchProcess {
    Write-Host "Starting LatencyPilot App with Hot Reload..."
    Write-Host "App/XAML changes pulled from main will be detected by dotnet watch. Unsupported edits may trigger an automatic App restart."

    return Start-Process dotnet -ArgumentList @(
        "watch",
        "--project", ".\src\LatencyPilot.App\LatencyPilot.App.csproj",
        "--non-interactive",
        "run",
        "--configuration", "Debug",
        "--no-restore"
    ) -NoNewWindow -PassThru
}

function Invoke-RestoreIfNeeded {
    param([bool]$Required)

    if ($Required -or -not (Test-Path -LiteralPath $appAssets) -or -not (Test-Path -LiteralPath $serviceAssets)) {
        Write-Host "Restoring App and Service..."
        Invoke-DotNet restore $serviceProject
        Invoke-DotNet restore $appProject
    }
}

function Build-Service {
    Write-Host "Building Service..."
    Invoke-DotNet build $serviceProject --configuration Debug --no-restore
}

$serviceProcess = $null
$appWatchProcess = $null
$dirtyWarningShown = $false
$divergedWarningShown = $false
$fetchWarningShown = $false

Push-Location $repoRoot
try {
    $branch = (& git branch --show-current | Select-Object -First 1).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to determine the current Git branch."
    }

    if ($branch -ne "main") {
        throw "live.ps1 requires the local main branch. Current branch: '$branch'."
    }

    Invoke-RestoreIfNeeded -Required:$ForceRestore
    Build-Service

    $serviceProcess = Start-ServiceProcess
    $appWatchProcess = Start-AppWatchProcess

    Write-Host ""
    Write-Host "Live mode is running." -ForegroundColor Green
    if ($NoAutoPull) {
        Write-Host "Auto-pull is disabled. Local file edits will still Hot Reload."
    }
    else {
        Write-Host "Watching origin/main every $PollSeconds second(s). New commits will be fast-forwarded automatically when the working tree is clean."
    }
    Write-Host "Press Ctrl+C to stop App watch and the development Service."
    Write-Host ""

    while ($true) {
        Start-Sleep -Seconds $PollSeconds

        $appWatchProcess.Refresh()
        if ($appWatchProcess.HasExited) {
            throw "dotnet watch exited with code $($appWatchProcess.ExitCode)."
        }

        $serviceProcess.Refresh()
        if ($serviceProcess.HasExited) {
            throw "LatencyPilot.Service exited unexpectedly with code $($serviceProcess.ExitCode)."
        }

        if ($NoAutoPull) {
            continue
        }

        & git fetch --quiet origin main 2>$null
        if ($LASTEXITCODE -ne 0) {
            if (-not $fetchWarningShown) {
                Write-Warning "Could not fetch origin/main. Live mode will keep running and retry automatically."
                $fetchWarningShown = $true
            }
            continue
        }
        $fetchWarningShown = $false

        $localSha = Get-HeadSha
        $remoteSha = Get-RemoteHeadSha
        if ($null -eq $remoteSha -or $localSha -eq $remoteSha) {
            $dirtyWarningShown = $false
            $divergedWarningShown = $false
            continue
        }

        if (-not (Test-WorkingTreeClean)) {
            if (-not $dirtyWarningShown) {
                Write-Warning "origin/main has new commits, but the local working tree has changes. Auto-pull is paused until the tree is clean."
                $dirtyWarningShown = $true
            }
            continue
        }
        $dirtyWarningShown = $false

        if (-not (Test-LocalHeadIsAncestorOfRemote)) {
            if (-not $divergedWarningShown) {
                Write-Warning "Local main has diverged from origin/main. Auto-pull is paused; resolve the Git history manually."
                $divergedWarningShown = $true
            }
            continue
        }
        $divergedWarningShown = $false

        $incomingPaths = Get-IncomingPaths
        $requiresRestart = Test-RequiresControlledRestart -Paths $incomingPaths
        $requiresRestore = Test-RequiresRestore -Paths $incomingPaths

        if ($requiresRestart) {
            Write-Host "Incoming change affects the Service/shared build graph; performing a controlled development restart..." -ForegroundColor Yellow
            Stop-ProcessTree -Process $appWatchProcess -Name "App Hot Reload watcher"
            Stop-ProcessTree -Process $serviceProcess -Name "LatencyPilot Service"
            $appWatchProcess = $null
            $serviceProcess = $null
        }

        Write-Host "New main commit detected. Pulling $($localSha.Substring(0, 7)) -> $($remoteSha.Substring(0, 7))..." -ForegroundColor Cyan
        & git pull --ff-only --quiet origin main
        if ($LASTEXITCODE -ne 0) {
            throw "git pull --ff-only origin main failed with exit code $LASTEXITCODE."
        }

        if ($requiresRestart) {
            Invoke-RestoreIfNeeded -Required:$requiresRestore
            Build-Service
            $serviceProcess = Start-ServiceProcess
            $appWatchProcess = Start-AppWatchProcess
            Write-Host "Controlled restart complete." -ForegroundColor Green
        }
        else {
            Write-Host "Update applied. dotnet watch will Hot Reload or auto-restart the App as required." -ForegroundColor Green
        }
    }
}
finally {
    if ($null -ne $appWatchProcess) {
        Stop-ProcessTree -Process $appWatchProcess -Name "App Hot Reload watcher"
    }
    if ($null -ne $serviceProcess) {
        Stop-ProcessTree -Process $serviceProcess -Name "LatencyPilot Service"
    }

    Pop-Location
}
