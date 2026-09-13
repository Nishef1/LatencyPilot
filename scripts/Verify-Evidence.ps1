[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Path,

    [string]$ExpectedCommit,

    [string]$ExpectedSha256
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Percent {
    param(
        [int]$Numerator,
        [int]$Denominator
    )

    if ($Denominator -le 0) {
        return $null
    }

    return [Math]::Round(($Numerator * 100.0) / $Denominator, 3)
}

function Format-Value {
    param(
        $Value,
        [string]$Suffix = ''
    )

    if ($null -eq $Value) {
        return '—'
    }

    if ($Value -is [double] -or $Value -is [float] -or $Value -is [decimal]) {
        return ('{0:N3}{1}' -f [double]$Value, $Suffix)
    }

    return "$Value$Suffix"
}

$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
    throw "Evidence file was not found: $resolvedPath"
}

$document = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
if ($document.schema -ne 'latencypilot-evidence-v7') {
    throw "Unexpected evidence schema '$($document.schema)'. Expected 'latencypilot-evidence-v7'."
}

$sourceRevision = [string]$document.sourceRevisionId
if ([string]::IsNullOrWhiteSpace($sourceRevision) -or $sourceRevision -notmatch '^[0-9a-fA-F]{7,40}$') {
    throw 'Evidence does not carry an exact clean sourceRevisionId. Rebuild from a clean working tree before using this artifact for Phase 2 closure.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ExpectedCommit) -and
    (Get-Command git -ErrorAction SilentlyContinue) -and
    (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))) {
    $workingTree = @(& git -C $repoRoot status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the Git working tree.'
    }
    if ($workingTree.Count -ne 0) {
        throw 'The repository working tree is dirty. Supply -ExpectedCommit explicitly or return to the exact clean revision used for this evidence.'
    }

    $ExpectedCommit = (& git -C $repoRoot rev-parse HEAD | Select-Object -First 1).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to resolve the current Git HEAD.'
    }
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{7,40}$') {
        throw "Expected commit '$ExpectedCommit' is not a valid Git revision id."
    }

    $matches = $sourceRevision.StartsWith($ExpectedCommit, [StringComparison]::OrdinalIgnoreCase) -or
        $ExpectedCommit.StartsWith($sourceRevision, [StringComparison]::OrdinalIgnoreCase)
    if (-not $matches) {
        throw "Evidence source revision '$sourceRevision' does not match expected commit '$ExpectedCommit'."
    }
}

$captureProperty = $document.PSObject.Properties['capture']
$capturesProperty = $document.PSObject.Properties['captures']
$requestIds = @()
$evidenceType = 'unknown'

if ($null -ne $captureProperty -and $null -ne $captureProperty.Value) {
    $requestIds = @([string]$captureProperty.Value.requestId)
    $evidenceType = 'observation'
}
elseif ($null -ne $capturesProperty -and $null -ne $capturesProperty.Value) {
    $requestIds = @($capturesProperty.Value | ForEach-Object { [string]$_.requestId })
    $evidenceType = 'baseline'
}

$missingRequestIds = @($requestIds | Where-Object { [string]::IsNullOrWhiteSpace($_) })
if ($requestIds.Count -eq 0 -or $missingRequestIds.Count -ne 0) {
    throw 'Evidence is missing one or more capture RequestId values.'
}

$duplicateRequestIds = @($requestIds | Group-Object | Where-Object Count -gt 1)
if ($duplicateRequestIds.Count -ne 0) {
    throw 'Evidence contains duplicate capture RequestId values.'
}

$sha256 = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not [string]::IsNullOrWhiteSpace($ExpectedSha256)) {
    $normalizedExpectedSha256 = $ExpectedSha256.Trim().ToLowerInvariant()
    if ($normalizedExpectedSha256 -notmatch '^[0-9a-f]{64}$') {
        throw "Expected SHA-256 '$ExpectedSha256' must be exactly 64 hexadecimal characters."
    }
    if ($sha256 -ne $normalizedExpectedSha256) {
        throw "Evidence SHA-256 '$sha256' does not match expected '$normalizedExpectedSha256'."
    }
}

$measurementContextProperty = $document.PSObject.Properties['measurementContext']
$scenario = if ($null -ne $measurementContextProperty -and $null -ne $measurementContextProperty.Value) {
    [string]$measurementContextProperty.Value.displayName
}
else {
    'unknown'
}

Write-Host 'LatencyPilot evidence verification passed.' -ForegroundColor Green
Write-Host "Path:            $resolvedPath"
Write-Host "Type:            $evidenceType"
Write-Host "Schema:          $($document.schema)"
Write-Host "Product version: $($document.productVersion)"
Write-Host "Scenario:        $scenario"
Write-Host "Source revision: $sourceRevision"
Write-Host "Capture IDs:     $($requestIds.Count) unique"
Write-Host "SHA-256:         $sha256"

if ($evidenceType -eq 'observation') {
    $capture = $captureProperty.Value
    $dpcRate = Get-Percent ([int]$capture.dpcThresholds.guidanceExceedanceCount) ([int]$capture.dpc.count)
    $isrRate = Get-Percent ([int]$capture.isrThresholds.guidanceExceedanceCount) ([int]$capture.isr.count)
    $attributedTotal = [int]$capture.resolvedModuleEventCount + [int]$capture.unresolvedModuleEventCount
    $coverage = Get-Percent ([int]$capture.resolvedModuleEventCount) $attributedTotal

    $topDpcProcessor = @($capture.processors | Sort-Object { [int]$_.dpc.count } -Descending | Select-Object -First 1)
    $topIsrProcessor = @($capture.processors | Sort-Object { [int]$_.isr.count } -Descending | Select-Object -First 1)
    $topDpcModule = @(
        $capture.modules |
            Where-Object { [int]$_.dpcThresholds.guidanceExceedanceCount -gt 0 } |
            Sort-Object { [int]$_.dpcThresholds.guidanceExceedanceCount } -Descending |
            Select-Object -First 1
    )
    $topIsrModule = @(
        $capture.modules |
            Where-Object { [int]$_.isrThresholds.guidanceExceedanceCount -gt 0 } |
            Sort-Object { [int]$_.isrThresholds.guidanceExceedanceCount } -Descending |
            Select-Object -First 1
    )

    Write-Host ''
    Write-Host 'Observation summary'
    Write-Host "Integrity:       lost=$($capture.eventsLost), invalid=$($capture.invalidEventCount), invalid-images=$($capture.invalidImageEventCount), event-limit=$($capture.eventLimitReached)"
    Write-Host "DPC:             count=$($capture.dpc.count), p99=$(Format-Value $capture.dpc.p99Microseconds ' us'), p99.9=$(Format-Value $capture.dpc.p999Microseconds ' us'), max=$(Format-Value $capture.dpc.maximumMicroseconds ' us'), >100 us=$($capture.dpcThresholds.guidanceExceedanceCount) ($(Format-Value $dpcRate '%'))"
    Write-Host "ISR:             count=$($capture.isr.count), p99=$(Format-Value $capture.isr.p99Microseconds ' us'), p99.9=$(Format-Value $capture.isr.p999Microseconds ' us'), max=$(Format-Value $capture.isr.maximumMicroseconds ' us'), >25 us=$($capture.isrThresholds.guidanceExceedanceCount) ($(Format-Value $isrRate '%'))"
    Write-Host "Long tail:       >1 ms DPC/ISR=$($capture.dpcThresholds.overOneMillisecondCount)/$($capture.isrThresholds.overOneMillisecondCount), >3 ms=$($capture.dpcThresholds.overThreeMillisecondsCount)/$($capture.isrThresholds.overThreeMillisecondsCount)"
    Write-Host "Attribution:     $(Format-Value $coverage '%') coverage; resolved=$($capture.resolvedModuleEventCount), unresolved=$($capture.unresolvedModuleEventCount)"

    if ($topDpcProcessor.Count -ne 0) {
        $dpcCpuShare = Get-Percent ([int]$topDpcProcessor[0].dpc.count) ([int]$capture.dpc.count)
        Write-Host "Top DPC CPU:     CPU $($topDpcProcessor[0].processorNumber) · $(Format-Value $dpcCpuShare '%') of DPC events"
    }
    if ($topIsrProcessor.Count -ne 0) {
        $isrCpuShare = Get-Percent ([int]$topIsrProcessor[0].isr.count) ([int]$capture.isr.count)
        Write-Host "Top ISR CPU:     CPU $($topIsrProcessor[0].processorNumber) · $(Format-Value $isrCpuShare '%') of ISR events"
    }
    if ($topDpcModule.Count -ne 0) {
        Write-Host "Top >100 us DPC: $($topDpcModule[0].moduleName) · $($topDpcModule[0].dpcThresholds.guidanceExceedanceCount) event(s)"
    }
    if ($topIsrModule.Count -ne 0) {
        Write-Host "Top >25 us ISR:  $($topIsrModule[0].moduleName) · $($topIsrModule[0].isrThresholds.guidanceExceedanceCount) event(s)"
    }

    $runtimeProperty = $document.PSObject.Properties['runtimeContext']
    if ($null -ne $runtimeProperty -and $null -ne $runtimeProperty.Value) {
        $runtime = $runtimeProperty.Value
        Write-Host "Runtime context: CPU busy=$(Format-Value $runtime.systemCpuBusyPercent '%'), power=$($runtime.startPower.lineState), configured-mode=$($runtime.startPower.userConfiguredPowerMode), changed=$($runtime.powerContextChanged)"
    }
}
