[CmdletBinding()]
param()

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$head = (& git -C $repoRoot rev-parse HEAD | Select-Object -First 1).Trim()
if ($LASTEXITCODE -ne 0 -or $head -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Could not resolve exact Git HEAD for the temporary verifier smoke.'
}

function New-Distribution {
    param([int]$Count, [double]$P99)

    return [ordered]@{
        count = $Count
        p50Microseconds = $P99 * 0.25
        p95Microseconds = $P99 * 0.75
        p99Microseconds = $P99
        maximumMicroseconds = $P99 * 1.25
    }
}

function New-Thresholds {
    param([double]$Guidance, [int]$GuidanceCount)

    return [ordered]@{
        guidanceThresholdMicroseconds = $Guidance
        guidanceExceedanceCount = $GuidanceCount
        overOneMillisecondCount = 0
        overThreeMillisecondsCount = 0
    }
}

function New-Capture {
    param(
        [int]$RequestedMilliseconds,
        [int]$DpcCount,
        [int]$IsrCount,
        [double]$DpcP99,
        [double]$IsrP99,
        [DateTimeOffset]$StartedAtUtc
    )

    $dpc = New-Distribution $DpcCount $DpcP99
    $isr = New-Distribution $IsrCount $IsrP99
    $dpcThresholds = New-Thresholds 100 10
    $isrThresholds = New-Thresholds 25 0

    return [ordered]@{
        requestId = [Guid]::NewGuid().ToString()
        startedAtUtc = $StartedAtUtc.ToString('o')
        requestedDurationMilliseconds = $RequestedMilliseconds
        actualDurationMilliseconds = [double]$RequestedMilliseconds
        eventsLost = 0
        invalidEventCount = 0
        invalidImageEventCount = 0
        eventLimitReached = $false
        resolvedModuleEventCount = 0
        unresolvedModuleEventCount = 0
        moduleContributorListTruncated = $false
        unresolvedRoutineListTruncated = $false
        dpc = $dpc
        isr = $isr
        dpcThresholds = $dpcThresholds
        isrThresholds = $isrThresholds
        processors = @(
            [ordered]@{
                processorNumber = 0
                dpc = $dpc
                isr = $isr
                dpcThresholds = $dpcThresholds
                isrThresholds = $isrThresholds
            }
        )
        modules = @()
        unresolvedRoutines = @()
    }
}

function New-Environment {
    return [ordered]@{
        operatingSystem = 'Windows 11 smoke fixture'
        operatingSystemVersion = '10.0.26100.0'
        osArchitecture = 'X64'
        processArchitecture = 'X64'
        processAvailableProcessorCount = 8
        dotNetRuntimeVersion = '10.0.0'
        topology = [ordered]@{
            packageCount = 1
            physicalCoreCount = 4
            logicalProcessorCount = 8
            processorGroupCount = 1
            smtCoreCount = 4
        }
    }
}

function New-MeasurementContext {
    return [ordered]@{
        scenario = 'RealWorld'
        displayName = 'Real-world workload'
        guidance = 'Temporary verifier smoke fixture.'
    }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('LatencyPilot-verifier-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    $quickCapture = New-Capture 5000 500 500 90 20 ([DateTimeOffset]::UtcNow)
    $quick = [ordered]@{
        schema = 'latencypilot-evidence-v8'
        purpose = 'quick-diagnostic-snapshot'
        productVersion = '0.0.2'
        sourceRevisionId = $head
        protocolVersion = 6
        exportedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        environment = New-Environment
        measurementContext = New-MeasurementContext
        capture = $quickCapture
    }

    $quickPath = Join-Path $tempRoot 'quick.json'
    $quick | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $quickPath -Encoding UTF8
    & (Join-Path $PSScriptRoot 'Verify-Evidence.ps1') $quickPath -ExpectedCommit $head -RequireCleanCapture
    if ($LASTEXITCODE -ne 0) {
        throw "Quick evidence verifier smoke failed with exit code $LASTEXITCODE."
    }

    $dpcValues = @(100.0, 102.0, 101.0, 99.0, 100.0)
    $isrValues = @(20.0, 21.0, 20.0, 19.0, 20.0)
    $captures = @()
    $windows = @()
    $runtimeWindows = @()
    $baseTime = [DateTimeOffset]::UtcNow

    for ($index = 0; $index -lt 5; $index++) {
        $capture = New-Capture 20000 1000 1000 $dpcValues[$index] $isrValues[$index] ($baseTime.AddSeconds($index * 21))
        $captures += $capture
        $windows += [ordered]@{
            windowNumber = $index + 1
            startedAtUtc = $capture.startedAtUtc
            requestedDurationMilliseconds = 20000
            actualDurationMilliseconds = 20000.0
            captureIntegrityValid = $true
            dpcEventCount = 1000
            dpcP99Microseconds = $dpcValues[$index]
            isrEventCount = 1000
            isrP99Microseconds = $isrValues[$index]
        }
        $runtimeWindows += [ordered]@{ windowNumber = $index + 1 }
    }

    $quality = [ordered]@{
        methodVersion = 'baseline-quality-v2'
        status = 'Valid'
        totalWindowCount = 5
        validCaptureWindowCount = 5
        dpcP99 = [ordered]@{
            metricName = 'DPC p99'
            eligibleWindowCount = 5
            medianMicroseconds = 100.0
            p10Microseconds = 99.4
            p90Microseconds = 101.6
            relativeNoiseFloor = 0.022
            relativeDrift = 0.015
            extremeWindowNumbers = @()
            reasons = @()
            isStable = $true
        }
        isrP99 = [ordered]@{
            metricName = 'ISR p99'
            eligibleWindowCount = 5
            medianMicroseconds = 20.0
            p10Microseconds = 19.4
            p90Microseconds = 20.6
            relativeNoiseFloor = 0.06
            relativeDrift = 0.05
            extremeWindowNumbers = @()
            reasons = @()
            isStable = $true
        }
        reasons = @()
        isValidForComparison = $true
    }

    $baseline = [ordered]@{
        schema = 'latencypilot-evidence-v8'
        purpose = 'repeated-decision-baseline'
        productVersion = '0.0.2'
        sourceRevisionId = $head
        protocolVersion = 6
        exportedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        environment = New-Environment
        measurementContext = New-MeasurementContext
        baselineMethodVersion = 'baseline-quality-v2'
        captures = $captures
        windows = $windows
        runtimeWindows = $runtimeWindows
        quality = $quality
    }

    $baselinePath = Join-Path $tempRoot 'baseline.json'
    $baseline | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $baselinePath -Encoding UTF8
    & (Join-Path $PSScriptRoot 'Verify-Evidence.ps1') $baselinePath -ExpectedCommit $head -RequireCleanCapture -RequireValidBaseline
    if ($LASTEXITCODE -ne 0) {
        throw "Baseline verifier smoke failed with exit code $LASTEXITCODE."
    }

    Write-Host 'Temporary Verify-Evidence smoke passed.' -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
