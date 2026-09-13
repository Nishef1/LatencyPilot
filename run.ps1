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

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'The .NET SDK is not available on PATH. Install the SDK pinned by global.json before running this script.'
}

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
    $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$installServiceScript`" -SourceServiceDirectory `"$serviceOutput`""
    $installer = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    if ($installer.ExitCode -ne 0) {
        throw "Protected Service installation failed with exit code $($installer.ExitCode)."
    }

    Write-Host 'Starting non-elevated LatencyPilot App...'
    Invoke-DotNet run --project $appProject --configuration Debug --no-build --no-restore
}
finally {
    Pop-Location
}
