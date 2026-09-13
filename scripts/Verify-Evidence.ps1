[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Path,

    [string]$ExpectedCommit,

    [string]$ExpectedSha256,

    [switch]$RequireCleanCapture
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Percent {
    param([int]$Numerator, [int]$Denominator)

    if ($Denominator -le 0) {
        return $null
    }

    return [Math]::Round(($Numerator * 100.0) / $Denominator, 3)
}

function Format-Value {
    param($Value, [string]$Suffix = '')

    if ($null -eq $Value) {
        return '—'
    }

    if ($Value -is [double] -or $Value -is [float] -or $Value -is [decimal]) {
        return ('{0:N3}{1}' -f [double]$Value, $Suffix)
    }

    return "$Value$Suffix"
}

function Get-CaptureIntegrityIssues {
    param($Capture)

    $issues = [System.Collections.Generic.List[string]]::new()

    if ([int]$Capture.eventsLost -ne 0) {
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

function Write-CaptureSummary {
    param($Capture, [string]$Prefix = '')

    $dpcRate = Get-Percent ([int]$Capture.dpcThresholds.guidanceExceedanceCount) ([int]$Capture.dpc.count)
    $isrRate = Get-Percent ([int]$Capture.isrThresholds.guidanceExceedanceCount) ([int]$Capture.isr.count)
    $attributedTotal = [int]$Capture.resolvedModuleEventCount + [int]$Capture.unresolvedModuleEventCount
    $coverage = Get-Percent ([int]$Capture.resolvedModuleEventCount) $attributedTotal

    $topDpcProcessor = @($Capture.processors | Sort-Object { [int]$_.dpc.count } -Descending | Select-Object -First 1)
    $topIsrProcessor = @($Capture.processors | Sort-Object { [int]$_.isr.count } -Descending | Select-Object -First 1)
    $topDpcModule = @(
        $Capture.modules |
            Where-Object { [int]$_.dpcThresholds.guidanceExceedanceCount -gt 0 } |
            Sort-Object { [int]$_.dpcThresholds.guidanceExceedanceCount } -Descending |
            Select-Object -First 1
    )
    $topIsrModule = @(
        $Capture.modules |
            Where-Object { [int]$_.isrThresholds.guidanceExceedanceCount -gt 0 } |
            Sort-Object { [int]$_.isrThresholds.guidanceExceedanceCount } -Descending |
            Select-Object -First 1
    )

    Write-Host "${Prefix}RequestId:       $($Capture.requestId)"
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

    if ([bool]$Capture.moduleContributorListTruncated -or [bool]$Capture.unresolvedRoutineListTruncated) {
        Write-Host "${Prefix}Contributor lists: truncated (module=$($Capture.moduleContributorListTruncated), unresolved=$($Capture.unresolvedRoutineListTruncated))" -ForegroundColor Yellow
    }
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
$captures = @()
$evidenceType = 'unknown'

if ($null -ne $captureProperty -and $null -ne $captureProperty.Value) {
    $captures = @($captureProperty.Value)
    $requestIds = @([string]$captureProperty.Value.requestId)
    $evidenceType = 'observation'
}
elseif ($null -ne $capturesProperty -and $null -ne $capturesProperty.Value) {
    $captures = @($capturesProperty.Value)
    $requestIds = @($captures | ForEach-Object { [string]$_.requestId })
    $evidenceType = 'baseline'
}

if ($captures.Count -eq 0) {
    throw 'Evidence contains neither an observation capture nor a baseline capture sequence.'
}

$missingRequestIds = @($requestIds | Where-Object { [string]::IsNullOrWhiteSpace($_) })
if ($missingRequestIds.Count -ne 0) {
    throw 'Evidence is missing one or more capture RequestId values.'
}

$duplicateRequestIds = @($requestIds | Group-Object | Where-Object Count -gt 1)
if ($duplicateRequestIds.Count -ne 0) {
    throw 'Evidence contains duplicate capture RequestId values.'
}

if ($evidenceType -eq 'baseline') {
    $windowsProperty = $document.PSObject.Properties['windows']
    $runtimeWindowsProperty = $document.PSObject.Properties['runtimeWindows']
    if ($null -eq $windowsProperty -or $null -eq $runtimeWindowsProperty) {
        throw 'Baseline evidence is missing window or runtime-window records.'
    }

    $windowCount = @($windowsProperty.Value).Count
    $runtimeWindowCount = @($runtimeWindowsProperty.Value).Count
    if ($captures.Count -ne $windowCount -or $captures.Count -ne $runtimeWindowCount) {
        throw "Baseline evidence is misaligned: captures=$($captures.Count), windows=$windowCount, runtimeWindows=$runtimeWindowCount."
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
}
else {
    'unknown'
}

$integrityIssues = @()
for ($index = 0; $index -lt $captures.Count; $index++) {
    $captureIssues = @(Get-CaptureIntegrityIssues $captures[$index])
    foreach ($issue in $captureIssues) {
        $integrityIssues += "capture $($index + 1): $issue"
    }
}

Write-Host 'Evidence envelope/provenance verification passed.' -ForegroundColor Green
Write-Host "Path:            $resolvedPath"
Write-Host "Type:            $evidenceType"
Write-Host "Schema:          $($document.schema)"
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
    if ($RequireCleanCapture) {
        throw 'Evidence envelope is valid, but capture integrity is not clean.'
    }
}

if ($evidenceType -eq 'observation') {
    Write-Host ''
    Write-Host 'Observation summary'
    Write-CaptureSummary $captures[0]

    $runtimeProperty = $document.PSObject.Properties['runtimeContext']
    if ($null -ne $runtimeProperty -and $null -ne $runtimeProperty.Value) {
        $runtime = $runtimeProperty.Value
        Write-Host "Runtime context: CPU busy=$(Format-Value $runtime.systemCpuBusyPercent '%'), power=$($runtime.startPower.lineState), configured-mode=$($runtime.startPower.userConfiguredPowerMode), changed=$($runtime.powerContextChanged)"
    }
}
elseif ($evidenceType -eq 'baseline') {
    $qualityProperty = $document.PSObject.Properties['quality']
    Write-Host ''
    Write-Host 'Baseline summary'
    Write-Host "Windows:         $($captures.Count)"
    Write-Host "Method:          $($document.baselineMethodVersion)"

    if ($null -ne $qualityProperty -and $null -ne $qualityProperty.Value) {
        $quality = $qualityProperty.Value
        Write-Host "Status:          $($quality.status)"
        Write-Host "Valid compare:   $($quality.isValidForComparison)"
        Write-Host "Valid captures:  $($quality.validCaptureWindowCount)/$($quality.totalWindowCount)"
        Write-Host "DPC p99 quality: median=$(Format-Value $quality.dpcP99.medianMicroseconds ' us'), noise=$(Format-Value $quality.dpcP99.relativeNoiseFloor), drift=$(Format-Value $quality.dpcP99.relativeDrift)"
        Write-Host "ISR p99 quality: median=$(Format-Value $quality.isrP99.medianMicroseconds ' us'), noise=$(Format-Value $quality.isrP99.relativeNoiseFloor), drift=$(Format-Value $quality.isrP99.relativeDrift)"
        $reasons = @($quality.reasons)
        if ($reasons.Count -eq 0) {
            Write-Host 'Reasons:         none'
        }
        else {
            Write-Host "Reasons:         $($reasons.Count)"
            $reasons | ForEach-Object { Write-Host "  - $_" }
        }
    }

    for ($index = 0; $index -lt $captures.Count; $index++) {
        Write-Host ''
        Write-Host "Window $($index + 1)"
        Write-CaptureSummary $captures[$index] '  '
    }
}
