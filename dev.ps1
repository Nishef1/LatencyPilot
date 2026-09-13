[CmdletBinding()]
param(
    [ValidateSet("watch", "run", "build", "restore")]
    [string]$Mode = "watch",

    [switch]$ForceRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectPath = Join-Path $repoRoot "src\LatencyPilot.App\LatencyPilot.App.csproj"
$assetsPath = Join-Path $repoRoot "src\LatencyPilot.App\obj\project.assets.json"

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

Push-Location $repoRoot
try {
    $needsRestore = $ForceRestore -or $Mode -eq "restore" -or -not (Test-Path -LiteralPath $assetsPath)

    if ($needsRestore) {
        Write-Host "Restoring LatencyPilot.App. NuGet will reuse the normal global package cache when packages are already present."
        Invoke-DotNet restore $projectPath
    }
    else {
        Write-Host "Using the existing restore state and NuGet global package cache. Use -ForceRestore after dependency changes."
    }

    if ($Mode -eq "restore") {
        return
    }

    if (-not (Test-Path -LiteralPath $assetsPath)) {
        throw "Restore did not produce '$assetsPath'."
    }

    switch ($Mode) {
        "build" {
            Invoke-DotNet build $projectPath --configuration Debug --no-restore
        }
        "run" {
            Invoke-DotNet run --project $projectPath --configuration Debug --no-restore
        }
        "watch" {
            Write-Host "Starting the App-only CLI Hot Reload loop. Use .\live.ps1 for the protected Service + App + live-log workflow."
            Write-Host "Visual Studio F5 remains the recommended path for the full WinUI XAML Hot Reload and Live Visual Tree experience."
            Invoke-DotNet watch --project $projectPath --non-interactive run --configuration Debug --no-restore
        }
    }
}
finally {
    Pop-Location
}
