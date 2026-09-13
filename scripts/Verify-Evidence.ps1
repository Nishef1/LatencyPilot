[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Path,

    [string]$ExpectedCommit,

    [string]$ExpectedSha256,

    [switch]$RequireCleanCapture,

    [switch]$RequireValidBaseline
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExpectedSchema = 'latencypilot-evidence-v8'
$ExpectedBaselineMethod = 'baseline-quality-v2'
$QuickSnapshotPurpose = 'quick-diagnostic-snapshot'
$DecisionBaselinePurpose = 'repeated-decision-baseline'
$RequiredBaselineWindows = 5
$MinimumBaselineRequestedMilliseconds = 20_000
$MinimumBaselineDurationRatio = 0.95
$MinimumBaselineEventsPerMetricWindow = 1_000
$MinimumSamplesForP999 = 10_000

function Get-Percent {
    param([int]$Numerator, [int]$Denominator)
    if ($Denominator -le 0) { return $null }
    return [Math]::Round(($Numerator * 100.0) / $Denominator, 3)
}

function Format-Value {
    param($Value, [string]$Suffix = '')
    if ($null -eq $Value) { return '—' }
    if ($Value -is [double] -or $Value -is [float] -or $Value -is [decimal]) {
        return ('{0:N3}{1}' -f [double]$Value, $Suffix)
    }
    return "$Value$Suffix"
}

function Get-RequiredPropertyValue {
    param($Object, [string]$Name, [string]$Context)
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Context is missing required property '$Name'."
    }
    return $property.Value
}

function Get-CaptureIntegrityIssues {
    param($Capture)
    $issues = [System.Collections.Generic.List[string]]::new()

    if ([int]$Capture.eventsLost -lt 0) {
        $issues.Add('ETW loss count is unavailable')
    }
    elseif ([int]$Capture.eventsLost -gt 0) {
        $issues.Add("ETW events lost: $($Capture.eventsLost)")
    }
    if ([int]$Capture.invalidEventCount -ne 0) {
        $issues.Add("invalid latency events: $($Capture.invalidEventCount)")
    }
    if ([int]$Capture.invalidImageEventCount -ne 0) {
        $issues.Add("invalid image events: $($Capture.invalidImageEventCount)")
    }
    if ([bool]$Capture.eventLimitReached) {
        $issues.Add('capture event limit reached')
    }

    return @($issues)
}

function Assert-P999Adequacy {
    param($Distribution, [string]$Context)

    $count = [int]$Distribution.count
    $p999Property = $Distribution.PSObject.Properties['p999Microseconds']
    $p999 = if ($null -eq $p999Property) { $null } else { $p999Property.Value }

    if ($count -lt $MinimumSamplesForP999 -and $null -ne $p999) {
        throw "$Context exposes p99.9 from only $count samples; v6 requires at least $MinimumSamplesForP999."
    }
    if ($count -ge $MinimumSamplesForP999 -and $null -eq $p999) {
        throw "$Context omits p99.9 despite $count samples; v6 expects it at $MinimumSamplesForP999 or more samples."
    }
}

function Assert-CaptureShape {
    param($Capture, [string]$Context)

    if ([string]::IsNullOrWhiteSpace([string]$Capture.requestId)) {
        throw "$Context has an empty RequestId."
    }
    if ([int]$Capture.requestedDurationMilliseconds -lt 100) {
        throw "$Context has an invalid requested duration."
    }
    if (-not [double]::IsFinite([double]$Capture.actualDurationMilliseconds) -or [double]$Capture.actualDurationMilliseconds -le 0) {
        throw "$Context has an invalid actual duration."
    }

    Assert-P999Adequacy $Capture.dpc "$Context DPC"
    Assert-P999Adequacy $Capture.isr "$Context ISR"
}

function Write-CaptureSummary {
    param($Capture, [string]$Prefix = '')

    $dpcRate = Get-Percent ([int]$Capture.dpcThresholds.guidanceExceedanceCount) ([int]$Capture.dpc.count)
    $isrRate = Get-Percent ([int]$Capture.isrThresholds.guidanceExceedanceCount) ([int]$Capture.isr.count)
    $attributedTotal = [int]$Capture.resolvedModuleEventCount + [int]$Capture.unresolvedModuleEventCount
    $coverage = Get-Percent ([int]$Capture.resolvedModuleEventCount) $attributedTotal

    $topDpcProcessor = @($Capture.processors | Sort-Object { [int]$_.dpc.count } -Descending | Select-Object -First 1)
    $topIsrProcessor = @($Capture.processors | Sort-Object { [int]$_.isr.count } -Descending | Select-Object -First 1)
    $topDpcModule = @($Capture.modules | Where-Object { [int]$_.dpcThresholds.guidanceExceedanceCount -gt 0 } | Sort-Object { [int]$_.dpcThresholds.guidanceExceedanceCount } -Descending | Select-Object -First 1)
    $topIsrModule = @($Capture.modules | Where-Object { [int]$_.isrThresholds.guidanceExceedanceCount -gt 0 } | Sort-Object { [int]$_.isrThresholds.guidanceExceedanceCount } -Descending | Select-Object -First 1)

    Write-Host "${Prefix}RequestId:       $($Capture.requestId)"
    Write-Host "${Prefix}Duration:        requested=$($Capture.requestedDurationMilliseconds) ms, actual=$(Format-Value $Capture.actualDurationMilliseconds ' ms')"
    Write-Host "${Prefix}Integrity:       lost=$($Capture.eventsLost), invalid=$($Capture.invalidEventCount), invalid-images=$($Capture.invalidImageEventCount), event-limit=$($Capture.eventLimitReached)"
    Write-Host "${Prefix}DPC:             count=$($Capture.dpc.count), p99=$(Format-Value $Capture.dpc.p99Microseconds ' us'), p99.9=$(Format-Value $Capture.dpc.p999Microseconds ' us'), max=$(Format-Value $Capture.dpc.maximumMicroseconds ' us'), >100 us=$($Capture.dpcThresholds.guidanceExceedanceCount) ($(Format-Value $dpcRate '%'))"
    Write-Host "${Prefix}ISR:             count=$($Capture.isr.count), p99=$(Format-Value $Capture.isr.p99Microseconds ' us'), p99.9=$(Format-Value $Capture.isr.p999Microseconds ' us'), max=$(Format-Value $Capture.isr.maximumMicroseconds ' us'), >25 us=$($Capture.isrThresholds.guidanceExceedanceCount) ($(Format-Value $isrRate '%'))"
    Write-Host "${Prefix}Long tail:       >1 ms DPC/ISR=$($Capture.dpcThresholds.overOneMillisecondCount)/$($Capture.isrThresholds.overOneMillisecondCount), >3 ms=$($Capture.dpcThresholds.overThreeMillisecondsCount)/$($Capture.isrThresholds.overThreeMillisecondsCount)"
    Write-Host "${Prefix}Attribution:     $(Format-Value $coverage '%') coverage; resolved=$($Capture.resolvedModuleEventCount), unresolved=$($Capture.unresolvedModuleEventCount)"

    if ($topDpcProcessor.Count -ne 0) {
        $share = Get-Percent ([int]$topDpcProcessor[0].dpc.count) ([int]$Capture.dpc.count)
        Write-Host "${Prefix}Top DPC CPU:     CPU $($topDpcProcessor[0].processorNumber) · $(Format-Value $share '%')"
    }
    if ($topIsrProcessor.Count -ne 0) {
        $share = Get-Percent ([int]$topIsrProcessor[0].isr.count) ([int]$Capture.isr.count)
        Write-Host "${Prefix}Top ISR CPU:     CPU $($topIsrProcessor[0].processorNumber) · $(Format-Value $share '%')"
    }
    if ($topDpcModule.Count -ne 0) {
        Write-Host "${Prefix}Top >100 us DPC: $($topDpcModule[0].moduleName) · $($topDpcModule[0].dpcThresholds.guidanceExceedanceCount) event(s)"
    }
    if ($topIsrModule.Count -ne 0) {
        Write-Host "${Prefix}Top >25 us ISR:  $($topIsrModule[0].moduleName) · $($topIsrModule[0].isrThresholds.guidanceExceedanceCount) event(s)"
    }
}

$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
    throw "Evidence file was not found: $resolvedPath"
}

$document = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
if ([string]$document.schema -ne $ExpectedSchema) {
    throw "Unexpected evidence schema '$($document.schema)'. Expected '$ExpectedSchema'."
}

$purpose = [string](Get-RequiredPropertyValue $document 'purpose' 'Evidence')
$sourceRevision = [string](Get-RequiredPropertyValue $document 'sourceRevisionId' 'Evidence')
if ([string]::IsNullOrWhiteSpace($sourceRevision) -or $sourceRevision -notmatch '^[0-9a-fA-F]{7,40}$') {
    throw 'Evidence does not carry an exact clean sourceRevisionId. Rebuild from a clean working tree before using this artifact for closure.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ExpectedCommit) -and
    (Get-Command git -ErrorAction SilentlyContinue) -and
    (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))) {
    $workingTree = @(& git -C $repoRoot status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect the Git working tree.' }
    if ($workingTree.Count -ne 0) {
        throw 'The repository working tree is dirty. Supply -ExpectedCommit explicitly or return to the exact clean revision used for this evidence.'
    }
    $ExpectedCommit = (& git -C $repoRoot rev-parse HEAD | Select-Object -First 1).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to resolve the current Git HEAD.' }
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
$captures = @()
$evidenceType = 'unknown'
if ($null -ne $captureProperty -and $null -ne $captureProperty.Value) {
    $captures = @($captureProperty.Value)
    $evidenceType = 'observation'
}
elseif ($null -ne $capturesProperty -and $null -ne $capturesProperty.Value) {
    $captures = @($capturesProperty.Value)
    $evidenceType = 'baseline'
}
if ($captures.Count -eq 0) {
    throw 'Evidence contains neither an observation capture nor a baseline capture sequence.'
}

if ($evidenceType -eq 'observation' -and $purpose -ne $QuickSnapshotPurpose) {
    throw "Observation purpose '$purpose' is invalid; expected '$QuickSnapshotPurpose'."
}
if ($evidenceType -eq 'baseline' -and $purpose -ne $DecisionBaselinePurpose) {
    throw "Baseline purpose '$purpose' is invalid; expected '$DecisionBaselinePurpose'."
}
if ($RequireValidBaseline -and $evidenceType -ne 'baseline') {
    throw '-RequireValidBaseline can only be used with repeated decision-baseline evidence.'
}

$requestIds = @()
for ($index = 0; $index -lt $captures.Count; $index++) {
    Assert-CaptureShape $captures[$index] "capture $($index + 1)"
    $requestIds += [string]$captures[$index].requestId
}
if (@($requestIds | Where-Object { [string]::IsNullOrWhiteSpace($_) }).Count -ne 0) {
    throw 'Evidence is missing one or more capture RequestId values.'
}
if (@($requestIds | Group-Object | Where-Object Count -gt 1).Count -ne 0) {
    throw 'Evidence contains duplicate capture RequestId values.'
}

$windows = @()
if ($evidenceType -eq 'baseline') {
    $windows = @((Get-RequiredPropertyValue $document 'windows' 'Baseline evidence'))
    $runtimeWindows = @((Get-RequiredPropertyValue $document 'runtimeWindows' 'Baseline evidence'))
    if ($captures.Count -ne $windows.Count -or $captures.Count -ne $runtimeWindows.Count) {
        throw "Baseline evidence is misaligned: captures=$($captures.Count), windows=$($windows.Count), runtimeWindows=$($runtimeWindows.Count)."
    }

    for ($index = 0; $index -lt $captures.Count; $index++) {
        $expectedWindowNumber = $index + 1
        $window = $windows[$index]
        $capture = $captures[$index]
        if ([int]$window.windowNumber -ne $expectedWindowNumber -or [int]$runtimeWindows[$index].windowNumber -ne $expectedWindowNumber) {
            throw "Baseline sequence mismatch at position $expectedWindowNumber."
        }
        if ([string]$capture.startedAtUtc -ne [string]$window.startedAtUtc) {
            throw "Baseline timestamp mismatch in window $expectedWindowNumber."
        }
        if ([int]$capture.requestedDurationMilliseconds -ne [int]$window.requestedDurationMilliseconds -or
            [Math]::Abs([double]$capture.actualDurationMilliseconds - [double]$window.actualDurationMilliseconds) -gt 0.001) {
            throw "Baseline duration alignment mismatch in window $expectedWindowNumber."
        }
    }
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
} else { 'unknown' }

$integrityIssues = @()
for ($index = 0; $index -lt $captures.Count; $index++) {
    foreach ($issue in @(Get-CaptureIntegrityIssues $captures[$index])) {
        $integrityIssues += "capture $($index + 1): $issue"
    }
}

Write-Host 'Evidence envelope/provenance verification passed.' -ForegroundColor Green
Write-Host "Path:            $resolvedPath"
Write-Host "Type:            $evidenceType"
Write-Host "Purpose:         $purpose"
Write-Host "Schema:          $($document.schema)"
Write-Host "Protocol:        $($document.protocolVersion)"
Write-Host "Product version: $($document.productVersion)"
Write-Host "Scenario:        $scenario"
Write-Host "Source revision: $sourceRevision"
Write-Host "Capture IDs:     $($requestIds.Count) unique"
Write-Host "SHA-256:         $sha256"

if ($integrityIssues.Count -eq 0) {
    Write-Host 'Measurement integrity: CLEAN' -ForegroundColor Green
}
else {
    Write-Host "Measurement integrity: WARNING ($($integrityIssues.Count) issue(s))" -ForegroundColor Yellow
    $integrityIssues | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    if ($RequireCleanCapture -or $RequireValidBaseline) {
        throw 'Evidence envelope is valid, but capture integrity is not clean.'
    }
}

if ($evidenceType -eq 'observation') {
    Write-Host ''
    Write-Host 'Quick diagnostic snapshot summary'
    Write-CaptureSummary $captures[0]
    Write-Host 'Decision status:   diagnostic only; do not use a single snapshot as a benchmark verdict.' -ForegroundColor Yellow

    $runtimeProperty = $document.PSObject.Properties['runtimeContext']
    if ($null -ne $runtimeProperty -and $null -ne $runtimeProperty.Value) {
        $runtime = $runtimeProperty.Value
        Write-Host "Runtime context:   CPU busy=$(Format-Value $runtime.systemCpuBusyPercent '%'), power=$($runtime.startPower.lineState), configured-mode=$($runtime.startPower.userConfiguredPowerMode), changed=$($runtime.powerContextChanged)"
    }
}
else {
    $quality = Get-RequiredPropertyValue $document 'quality' 'Baseline evidence'
    Write-Host ''
    Write-Host 'Repeated decision-baseline summary'
    Write-Host "Windows:         $($captures.Count)"
    Write-Host "Method:          $($document.baselineMethodVersion)"
    Write-Host "Status:          $($quality.status)"
    Write-Host "Valid compare:   $($quality.isValidForComparison)"
    Write-Host "Valid captures:  $($quality.validCaptureWindowCount)/$($quality.totalWindowCount)"
    Write-Host "DPC p99 quality: median=$(Format-Value $quality.dpcP99.medianMicroseconds ' us'), noise=$(Format-Value $quality.dpcP99.relativeNoiseFloor), drift=$(Format-Value $quality.dpcP99.relativeDrift)"
    Write-Host "ISR p99 quality: median=$(Format-Value $quality.isrP99.medianMicroseconds ' us'), noise=$(Format-Value $quality.isrP99.relativeNoiseFloor), drift=$(Format-Value $quality.isrP99.relativeDrift)"

    for ($index = 0; $index -lt $captures.Count; $index++) {
        $window = $windows[$index]
        Write-Host ''
        Write-Host "Window $($index + 1): requested=$($window.requestedDurationMilliseconds) ms, actual=$(Format-Value $window.actualDurationMilliseconds ' ms'), DPC=$($window.dpcEventCount), ISR=$($window.isrEventCount)"
        Write-CaptureSummary $captures[$index] '  '
    }

    $reasons = @($quality.reasons)
    if ($reasons.Count -eq 0) {
        Write-Host 'Reasons:         none'
    }
    else {
        Write-Host "Reasons:         $($reasons.Count)"
        $reasons | ForEach-Object { Write-Host "  - $_" }
    }

    if ($RequireValidBaseline) {
        if ($captures.Count -ne $RequiredBaselineWindows -or $windows.Count -ne $RequiredBaselineWindows) {
            throw "Baseline is not closure-ready: expected exactly $RequiredBaselineWindows windows, found captures=$($captures.Count), windows=$($windows.Count)."
        }
        if ([string]$document.baselineMethodVersion -ne $ExpectedBaselineMethod) {
            throw "Baseline is not closure-ready: method '$($document.baselineMethodVersion)' is not '$ExpectedBaselineMethod'."
        }
        if (-not [bool]$quality.isValidForComparison -or [string]$quality.status -ne 'Valid') {
            throw "Baseline is not closure-ready: quality status is '$($quality.status)' and IsValidForComparison is '$($quality.isValidForComparison)'."
        }
        if ([int]$quality.validCaptureWindowCount -ne $RequiredBaselineWindows -or [int]$quality.totalWindowCount -ne $RequiredBaselineWindows) {
            throw "Baseline is not closure-ready: valid/total counts are $($quality.validCaptureWindowCount)/$($quality.totalWindowCount)."
        }

        for ($index = 0; $index -lt $windows.Count; $index++) {
            $window = $windows[$index]
            $requested = [int]$window.requestedDurationMilliseconds
            $actual = [double]$window.actualDurationMilliseconds
            if ($requested -lt $MinimumBaselineRequestedMilliseconds) {
                throw "Baseline window $($index + 1) requested only $requested ms; at least $MinimumBaselineRequestedMilliseconds ms is required."
            }
            if ($actual -lt ($requested * $MinimumBaselineDurationRatio)) {
                throw "Baseline window $($index + 1) completed only $actual ms of $requested ms; at least $([Math]::Round($MinimumBaselineDurationRatio * 100))% is required."
            }
            if ([int]$window.dpcEventCount -lt $MinimumBaselineEventsPerMetricWindow -or [int]$window.isrEventCount -lt $MinimumBaselineEventsPerMetricWindow) {
                throw "Baseline window $($index + 1) is undersampled: DPC=$($window.dpcEventCount), ISR=$($window.isrEventCount); each requires at least $MinimumBaselineEventsPerMetricWindow events."
            }
        }

        Write-Host 'Decision-baseline closure gate: PASS' -ForegroundColor Green
    }
}
