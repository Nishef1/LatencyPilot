[CmdletBinding()]
param(
    [ValidateRange(2, 60)]
    [int]$PollSeconds = 5,

    [switch]$ForceRestore,

    [switch]$NoAutoPull,

    [switch]$NoLogs
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
$logWatcherScript = Join-Path $repoRoot 'scripts\Watch-LogStream.ps1'
$serviceName = 'LatencyPilot.Observation'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is not available on PATH. Install the SDK pinned by global.json before running this script.'
}

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    throw 'Git is not available on PATH.'
}

function Test-IsElevated {
    $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (Test-IsElevated) {
    throw 'Run live.ps1 from a normal non-elevated terminal. The script requests UAC only for the protected observation Service; the WinUI App must remain non-elevated.'
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

function Test-RequiresServiceUpdate {
    param([string[]]$Paths)

    foreach ($path in $Paths) {
        if ($path -match '^src/LatencyPilot\.(Service|Core|Benchmarking|Protocol|Platform\.Windows|Persistence)/' -or
            $path -in @('global.json', 'Directory.Build.props', 'Directory.Build.targets')) {
            return $true
        }
    }

    return $false
}

function Test-RequiresRestore {
    param([string[]]$Paths)

    foreach ($path in $Paths) {
        if ($path -match '\.(csproj|props|targets)$' -or
            $path -eq 'global.json') {
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

function Invoke-RestoreIfNeeded {
    param([bool]$Required)

    if ($Required -or -not (Test-Path -LiteralPath $appAssets) -or -not (Test-Path -LiteralPath $serviceAssets)) {
        Write-Host 'Restoring App and Service...'
        Invoke-DotNet restore $serviceProject
        Invoke-DotNet restore $appProject
    }
}

function Build-Service {
    Write-Host 'Building privileged observation Service...'
    Invoke-DotNet build $serviceProject --configuration Debug --no-restore

    $serviceExe = Join-Path $serviceOutput 'LatencyPilot.Service.exe'
    if (-not (Test-Path -LiteralPath $serviceExe -PathType Leaf)) {
        throw "Service build succeeded but its executable was not found: $serviceExe"
    }
}

function Install-ProtectedService {
    Write-Host 'Updating the protected LocalSystem observation Service (UAC may prompt)...' -ForegroundColor Yellow
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$installServiceScript`" -SourceServiceDirectory `"$serviceOutput`""
    $installer = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    if ($installer.ExitCode -ne 0) {
        throw "Protected Service installation failed with exit code $($installer.ExitCode)."
    }

    $service = Get-Service -Name $serviceName -ErrorAction Stop
    if ($service.Status -ne 'Running') {
        $service.WaitForStatus('Running', [TimeSpan]::FromSeconds(15))
    }

    Write-Host 'Protected observation Service is running under the Windows Service Control Manager.' -ForegroundColor Green
}

function Start-AppWatchProcess {
    Write-Host 'Starting non-elevated LatencyPilot App with dotnet watch...'
    Write-Host 'C# edits use .NET Hot Reload where supported; unsupported edits cause the watcher to restart the App.'
    Write-Host 'For the full WinUI XAML Hot Reload/Live Visual Tree experience, use Visual Studio F5.' -ForegroundColor DarkGray

    return Start-Process dotnet -ArgumentList @(
        'watch',
        '--project', '.\src\LatencyPilot.App\LatencyPilot.App.csproj',
        '--non-interactive',
        'run',
        '--configuration', 'Debug',
        '--no-restore'
    ) -NoNewWindow -PassThru
}

function Start-LogWatcher {
    param(
        [ValidateSet('APP', 'SERVICE')]
        [string]$Component,
        [string]$Directory,
        [string]$Pattern
    )

    if ($NoLogs) {
        return $null
    }

    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$logWatcherScript`" -Component $Component -Directory `"$Directory`" -Pattern `"$Pattern`""
    return Start-Process powershell.exe -ArgumentList $arguments -NoNewWindow -PassThru
}

$appWatchProcess = $null
$appLogProcess = $null
$serviceLogProcess = $null
$dirtyWarningShown = $false
$divergedWarningShown = $false
$fetchWarningShown = $false

Push-Location $repoRoot
try {
    $branch = (& git branch --show-current | Select-Object -First 1).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to determine the current Git branch.'
    }

    if ($branch -ne 'main') {
        throw "live.ps1 requires the local main branch. Current branch: '$branch'."
    }

    Invoke-RestoreIfNeeded -Required:$ForceRestore
    Build-Service
    Install-ProtectedService

    if (-not $NoLogs) {
        $appLogDirectory = Join-Path $env:LOCALAPPDATA 'LatencyPilot\Logs\App'
        $serviceLogDirectory = Join-Path $env:ProgramData 'LatencyPilot\Logs\Service'
        $appLogProcess = Start-LogWatcher -Component APP -Directory $appLogDirectory -Pattern 'latencypilot-app-*.json'
        $serviceLogProcess = Start-LogWatcher -Component SERVICE -Directory $serviceLogDirectory -Pattern 'latencypilot-service-*.json'
    }

    $appWatchProcess = Start-AppWatchProcess

    Write-Host ''
    Write-Host 'Live mode is running.' -ForegroundColor Green
    Write-Host 'The App stays non-elevated; privileged ETW observation stays inside the installed LocalSystem Service.'
    if (-not $NoLogs) {
        Write-Host 'App and Service structured logs are streamed into this terminal with [APP]/[SERVICE] prefixes.'
    }
    if ($NoAutoPull) {
        Write-Host 'Auto-pull is disabled. Local file edits will still be watched.'
    }
    else {
        Write-Host "Watching origin/main every $PollSeconds second(s). Clean fast-forward updates are applied automatically."
    }
    Write-Host 'Press Ctrl+C to stop the App watcher and live log viewers. The installed observation Service remains available.'
    Write-Host ''

    while ($true) {
        Start-Sleep -Seconds $PollSeconds

        $appWatchProcess.Refresh()
        if ($appWatchProcess.HasExited) {
            throw "dotnet watch exited with code $($appWatchProcess.ExitCode)."
        }

        $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if ($null -eq $service -or $service.Status -ne 'Running') {
            throw 'The protected LatencyPilot observation Service is not running. Re-run live.ps1 to rebuild/install it and inspect the [SERVICE] log stream.'
        }

        if ($NoAutoPull) {
            continue
        }

        & git fetch --quiet origin '+refs/heads/main:refs/remotes/origin/main' 2>$null
        if ($LASTEXITCODE -ne 0) {
            if (-not $fetchWarningShown) {
                Write-Warning 'Could not fetch origin/main. Live mode will keep running and retry automatically.'
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
                Write-Warning 'origin/main has new commits, but the local working tree has changes. Auto-pull is paused until the tree is clean.'
                $dirtyWarningShown = $true
            }
            continue
        }
        $dirtyWarningShown = $false

        if (-not (Test-LocalHeadIsAncestorOfRemote)) {
            if (-not $divergedWarningShown) {
                Write-Warning 'Local main has diverged from origin/main. Auto-pull is paused; resolve the Git history manually.'
                $divergedWarningShown = $true
            }
            continue
        }
        $divergedWarningShown = $false

        $incomingPaths = Get-IncomingPaths
        $requiresServiceUpdate = Test-RequiresServiceUpdate -Paths $incomingPaths
        $requiresRestore = Test-RequiresRestore -Paths $incomingPaths
        $requiresControlledAppRestart = $requiresServiceUpdate -or $requiresRestore

        if ($requiresControlledAppRestart) {
            Write-Host 'Incoming change touches the Service/shared build graph; pausing the App watcher for a controlled update...' -ForegroundColor Yellow
            Stop-ProcessTree -Process $appWatchProcess -Name 'App Hot Reload watcher'
            $appWatchProcess = $null
        }

        Write-Host "New main commit detected. Pulling $($localSha.Substring(0, 7)) -> $($remoteSha.Substring(0, 7))..." -ForegroundColor Cyan
        & git pull --ff-only --quiet origin main
        if ($LASTEXITCODE -ne 0) {
            throw "git pull --ff-only origin main failed with exit code $LASTEXITCODE."
        }

        if ($requiresRestore) {
            Invoke-RestoreIfNeeded -Required:$true
        }

        if ($requiresServiceUpdate) {
            Build-Service
            Install-ProtectedService
        }

        if ($requiresControlledAppRestart) {
            $appWatchProcess = Start-AppWatchProcess
            Write-Host 'Controlled update complete.' -ForegroundColor Green
        }
        else {
            Write-Host 'Update applied. dotnet watch will Hot Reload or restart the App if the edit requires it.' -ForegroundColor Green
        }
    }
}
finally {
    if ($null -ne $appWatchProcess) {
        Stop-ProcessTree -Process $appWatchProcess -Name 'App Hot Reload watcher'
    }
    if ($null -ne $appLogProcess) {
        Stop-ProcessTree -Process $appLogProcess -Name 'App log viewer'
    }
    if ($null -ne $serviceLogProcess) {
        Stop-ProcessTree -Process $serviceLogProcess -Name 'Service log viewer'
    }

    Pop-Location
}
