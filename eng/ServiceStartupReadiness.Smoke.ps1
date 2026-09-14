Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $repoRoot 'scripts\ServiceStartupReadiness.ps1'
. $helper

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Expected -ne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("LatencyPilot-ServiceStartupReadiness-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    $now = [DateTimeOffset]::UtcNow

    $readyDirectory = Join-Path $tempRoot 'ready'
    New-Item -ItemType Directory -Path $readyDirectory -Force | Out-Null
    @{
        '@t' = $now.ToString('O')
        '@m' = 'Mutation journal initialized. Unresolved experiment count: 2. Mutation remains disabled by protocol.'
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $readyDirectory 'latencypilot-service-ready.json') -Encoding UTF8

    $ready = Wait-LatencyPilotServiceStartupReadiness `
        -LogDirectory $readyDirectory `
        -NotBeforeUtc $now.AddSeconds(-1) `
        -Timeout ([TimeSpan]::FromMilliseconds(250))
    Assert-Equal -Expected 2 -Actual $ready.UnresolvedCount -Message 'Fresh journal readiness event was not parsed.'

    $staleDirectory = Join-Path $tempRoot 'stale'
    New-Item -ItemType Directory -Path $staleDirectory -Force | Out-Null
    @{
        '@t' = $now.AddMinutes(-5).ToString('O')
        '@m' = 'Mutation journal initialized. Unresolved experiment count: 0. Mutation remains disabled by protocol.'
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $staleDirectory 'latencypilot-service-stale.json') -Encoding UTF8

    try {
        $null = Wait-LatencyPilotServiceStartupReadiness `
            -LogDirectory $staleDirectory `
            -NotBeforeUtc $now.AddSeconds(-1) `
            -Timeout ([TimeSpan]::FromMilliseconds(100))
        throw 'A stale journal event was incorrectly accepted as current startup evidence.'
    }
    catch [TimeoutException] {
    }

    $failureDirectory = Join-Path $tempRoot 'failure'
    New-Item -ItemType Directory -Path $failureDirectory -Force | Out-Null
    @{
        '@t' = $now.ToString('O')
        '@m' = 'Mutation journal startup inspection failed: IOException: fixture failure. Read-only observation can continue, but mutation must remain blocked.'
    } | ConvertTo-Json -Compress | Set-Content -LiteralPath (Join-Path $failureDirectory 'latencypilot-service-failure.json') -Encoding UTF8

    try {
        $null = Wait-LatencyPilotServiceStartupReadiness `
            -LogDirectory $failureDirectory `
            -NotBeforeUtc $now.AddSeconds(-1) `
            -Timeout ([TimeSpan]::FromMilliseconds(250))
        throw 'A journal startup failure event was not rejected.'
    }
    catch [InvalidOperationException] {
        if ($_.Exception.Message -notlike '*fixture failure*') {
            throw
        }
    }

    Write-Host 'Temporary Service startup readiness smoke passed.'
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
