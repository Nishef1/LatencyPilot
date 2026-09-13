[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Path,

    [string]$ExpectedCommit,

    [string]$ExpectedSha256,

    [switch]$RequireCleanCapture,

    [switch]$RequireValidBaseline
)

# Keep a deterministic strictness level that is supported by Windows PowerShell 5.1.
Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

$ExpectedSchema = 'latencypilot-evidence-v8'
$ExpectedProtocol = 6
$ExpectedBaselineMethod = 'baseline-quality-v2'
$QuickSnapshotPurpose = 'quick-diagnostic-snapshot'
$DecisionBaselinePurpose = 'repeated-decision-baseline'
$RequiredBaselineWindows = 5
$MinimumBaselineRequestedMilliseconds = 20000
$MinimumBaselineDurationRatio = 0.95
$MinimumBaselineEventsPerMetricWindow = 1000
$MinimumSamplesForP999 = 10000
$MaximumRelativeNoiseFloor = 0.30
$MaximumRelativeDrift = 0.20
$ExtremeWindowRelativeDeviation = 0.50
$NumericTolerance = 0.001

function Get-RequiredPropertyValue {
    param(
        [Parameter(Mandatory)]$Object,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Context
    )

    if ($null -eq $Object) {
        throw "$Context is null while reading required property '$Name'."
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Context is missing required property '$Name'."
    }

    return $property.Value
}

function Get-OptionalPropertyValue {
    param(
        $Object,
        [Parameter(Mandatory)][string]$Name
    )

    if ($null -eq $Object) {
        return $null
    }

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Test-FiniteNumber {
    param($Value)

    if ($null -eq $Value) {
        return $false
    }

    try {
        $number = [double]$Value
    }
    catch {
        return $false
    }

    return (-not [double]::IsNaN($number)) -and (-not [double]::IsInfinity($number))
}

function Test-FinitePositive {
    param($Value)

    return (Test-FiniteNumber $Value) -and ([double]$Value -gt 0)
}

function Test-FiniteNonNegative {
    param($Value)

    return (Test-FiniteNumber $Value) -and ([double]$Value -ge 0)
}

function Assert-NearlyEqual {
    param(
        $Expected,
        $Actual,
        [Parameter(Mandatory)][string]$Context,
        [double]$Tolerance = $NumericTolerance
    )

    if ($null -eq $Expected -and $null -eq $Actual) {
        return
    }

    if ($null -eq $Expected -or $null -eq $Actual) {
        throw "$Context mismatch: expected '$Expected', actual '$Actual'."
    }

    if (-not (Test-FiniteNumber $Expected) -or -not (Test-FiniteNumber $Actual)) {
        throw "$Context contains a non-finite value."
    }

    if ([Math]::Abs([double]$Expected - [double]$Actual) -gt $Tolerance) {
        throw "$Context mismatch: expected '$Expected', actual '$Actual'."
    }
}

function Format-Value {
    param($Value, [string]$Suffix = '')

    if ($null -eq $Value) {
        return '-'
    }

    if ($Value -is [bool]) {
        return "$Value$Suffix"
    }

    if ($Value -is [byte] -or $Value -is [sbyte] -or
        $Value -is [System.Int16] -or $Value -is [System.UInt16] -or
        $Value -is [int] -or $Value -is [uint] -or
        $Value -is [long] -or $Value -is [ulong] -or
        $Value -is [float] -or $Value -is [double] -or $Value -is [decimal]) {
        return ('{0:N3}{1}' -f [double]$Value, $Suffix)
    }

    return "$Value$Suffix"
}

function Get-Percent {
    param([int]$Numerator, [int]$Denominator)

    if ($Denominator -le 0) {
        return $null
    }

    return [Math]::Round(($Numerator * 100.0) / $Denominator, 3)
}

function Get-Percentile {
    param(
        [Parameter(Mandatory)][double[]]$SortedValues,
        [Parameter(Mandatory)][double]$Percentile
    )

    if ($SortedValues.Count -eq 0) {
        throw 'Percentile calculation requires at least one value.'
    }

    if ($Percentile -lt 0 -or $Percentile -gt 1) {
        throw "Percentile '$Percentile' is outside [0,1]."
    }

    $position = ($SortedValues.Count - 1) * $Percentile
    $lower = [int][Math]::Floor($position)
    $upper = [int][Math]::Ceiling($position)
    if ($lower -eq $upper) {
        return [double]$SortedValues[$lower]
    }

    $fraction = $position - $lower
    return [double]$SortedValues[$lower] +
        (([double]$SortedValues[$upper] - [double]$SortedValues[$lower]) * $fraction)
}

function Assert-Distribution {
    param(
        [Parameter(Mandatory)]$Distribution,
        [Parameter(Mandatory)][string]$Context
    )

    $count = [int](Get-RequiredPropertyValue $Distribution 'count' $Context)
    if ($count -lt 0) {
        throw "$Context has a negative sample count."
    }

    $p50 = Get-OptionalPropertyValue $Distribution 'p50Microseconds'
    $p95 = Get-OptionalPropertyValue $Distribution 'p95Microseconds'
    $p99 = Get-OptionalPropertyValue $Distribution 'p99Microseconds'
    $p999 = Get-OptionalPropertyValue $Distribution 'p999Microseconds'
    $maximum = Get-OptionalPropertyValue $Distribution 'maximumMicroseconds'

    if ($count -eq 0) {
        foreach ($entry in @(
            @{ Name = 'p50'; Value = $p50 },
            @{ Name = 'p95'; Value = $p95 },
            @{ Name = 'p99'; Value = $p99 },
            @{ Name = 'p99.9'; Value = $p999 },
            @{ Name = 'maximum'; Value = $maximum })) {
            if ($null -ne $entry.Value) {
                throw "$Context exposes $($entry.Name) without samples."
            }
        }
        return
    }

    foreach ($entry in @(
        @{ Name = 'p50'; Value = $p50 },
        @{ Name = 'p95'; Value = $p95 },
        @{ Name = 'p99'; Value = $p99 },
        @{ Name = 'maximum'; Value = $maximum })) {
        if (-not (Test-FiniteNonNegative $entry.Value)) {
            throw "$Context has invalid $($entry.Name) evidence."
        }
    }

    if ([double]$p50 -gt [double]$p95 -or
        [double]$p95 -gt [double]$p99 -or
        [double]$p99 -gt [double]$maximum) {
        throw "$Context has non-monotonic percentile evidence."
    }

    if ($count -lt $MinimumSamplesForP999) {
        if ($null -ne $p999) {
            throw "$Context exposes p99.9 from only $count samples; protocol v6 requires at least $MinimumSamplesForP999."
        }
    }
    else {
        if (-not (Test-FiniteNonNegative $p999)) {
            throw "$Context omits or invalidates p99.9 despite $count samples."
        }

        if ([double]$p999 -lt [double]$p99 -or [double]$p999 -gt [double]$maximum) {
            throw "$Context has non-monotonic p99.9 evidence."
        }
    }
}

function Assert-ThresholdSummary {
    param(
        [Parameter(Mandatory)]$Thresholds,
        [Parameter(Mandatory)]$Distribution,
        [Parameter(Mandatory)][string]$Context,
        [double]$ExpectedGuidanceThreshold = 0
    )

    $count = [int](Get-RequiredPropertyValue $Distribution 'count' "$Context distribution")
    $guidanceThreshold = Get-RequiredPropertyValue $Thresholds 'guidanceThresholdMicroseconds' $Context
    $guidanceCount = [int](Get-RequiredPropertyValue $Thresholds 'guidanceExceedanceCount' $Context)
    $overOneMillisecond = [int](Get-RequiredPropertyValue $Thresholds 'overOneMillisecondCount' $Context)
    $overThreeMilliseconds = [int](Get-RequiredPropertyValue $Thresholds 'overThreeMillisecondsCount' $Context)

    if (-not (Test-FinitePositive $guidanceThreshold)) {
        throw "$Context has an invalid guidance threshold."
    }

    if ($ExpectedGuidanceThreshold -gt 0 -and
        [Math]::Abs([double]$guidanceThreshold - $ExpectedGuidanceThreshold) -gt $NumericTolerance) {
        throw "$Context guidance threshold '$guidanceThreshold' does not match expected '$ExpectedGuidanceThreshold'."
    }

    if ($guidanceCount -lt 0 -or $overOneMillisecond -lt 0 -or $overThreeMilliseconds -lt 0 -or
        $guidanceCount -gt $count -or $overOneMillisecond -gt $count -or $overThreeMilliseconds -gt $overOneMillisecond) {
        throw "$Context has inconsistent threshold counts."
    }

    if ([double]$guidanceThreshold -lt 1000 -and $guidanceCount -lt $overOneMillisecond) {
        throw "$Context reports fewer guidance exceedances than >1 ms events."
    }
}

function Assert-CaptureShape {
    param(
        [Parameter(Mandatory)]$Capture,
        [Parameter(Mandatory)][string]$Context
    )

    $requestIdText = [string](Get-RequiredPropertyValue $Capture 'requestId' $Context)
    $parsedRequestId = [Guid]::Empty
    if (-not [Guid]::TryParse($requestIdText, [ref]$parsedRequestId) -or $parsedRequestId -eq [Guid]::Empty) {
        throw "$Context has an invalid or empty RequestId."
    }

    $requestedDuration = [int](Get-RequiredPropertyValue $Capture 'requestedDurationMilliseconds' $Context)
    $actualDuration = Get-RequiredPropertyValue $Capture 'actualDurationMilliseconds' $Context
    if ($requestedDuration -lt 100 -or -not (Test-FinitePositive $actualDuration)) {
        throw "$Context has invalid requested/actual duration evidence."
    }

    $eventsLost = [int](Get-RequiredPropertyValue $Capture 'eventsLost' $Context)
    $invalidEventCount = [int](Get-RequiredPropertyValue $Capture 'invalidEventCount' $Context)
    $invalidImageEventCount = [int](Get-RequiredPropertyValue $Capture 'invalidImageEventCount' $Context)
    $resolvedModuleEventCount = [int](Get-RequiredPropertyValue $Capture 'resolvedModuleEventCount' $Context)
    $unresolvedModuleEventCount = [int](Get-RequiredPropertyValue $Capture 'unresolvedModuleEventCount' $Context)
    if ($eventsLost -lt -1 -or $invalidEventCount -lt 0 -or $invalidImageEventCount -lt 0 -or
        $resolvedModuleEventCount -lt 0 -or $unresolvedModuleEventCount -lt 0) {
        throw "$Context has impossible integrity/attribution counters."
    }

    $dpc = Get-RequiredPropertyValue $Capture 'dpc' $Context
    $isr = Get-RequiredPropertyValue $Capture 'isr' $Context
    $dpcThresholds = Get-RequiredPropertyValue $Capture 'dpcThresholds' $Context
    $isrThresholds = Get-RequiredPropertyValue $Capture 'isrThresholds' $Context
    Assert-Distribution $dpc "$Context DPC"
    Assert-Distribution $isr "$Context ISR"
    Assert-ThresholdSummary $dpcThresholds $dpc "$Context DPC thresholds" 100
    Assert-ThresholdSummary $isrThresholds $isr "$Context ISR thresholds" 25

    $processors = @((Get-RequiredPropertyValue $Capture 'processors' $Context))
    $processorDpcCount = 0
    $processorIsrCount = 0
    foreach ($processor in $processors) {
        $processorDpc = Get-RequiredPropertyValue $processor 'dpc' "$Context processor"
        $processorIsr = Get-RequiredPropertyValue $processor 'isr' "$Context processor"
        Assert-Distribution $processorDpc "$Context processor DPC"
        Assert-Distribution $processorIsr "$Context processor ISR"
        Assert-ThresholdSummary (Get-RequiredPropertyValue $processor 'dpcThresholds' "$Context processor") $processorDpc "$Context processor DPC thresholds" 100
        Assert-ThresholdSummary (Get-RequiredPropertyValue $processor 'isrThresholds' "$Context processor") $processorIsr "$Context processor ISR thresholds" 25
        $processorDpcCount += [int](Get-RequiredPropertyValue $processorDpc 'count' "$Context processor DPC")
        $processorIsrCount += [int](Get-RequiredPropertyValue $processorIsr 'count' "$Context processor ISR")
    }

    if ($processorDpcCount -ne [int](Get-RequiredPropertyValue $dpc 'count' "$Context DPC") -or
        $processorIsrCount -ne [int](Get-RequiredPropertyValue $isr 'count' "$Context ISR")) {
        throw "$Context processor aggregates do not reconcile with top-level DPC/ISR counts."
    }

    foreach ($module in @((Get-RequiredPropertyValue $Capture 'modules' $Context))) {
        $moduleDpc = Get-RequiredPropertyValue $module 'dpc' "$Context module"
        $moduleIsr = Get-RequiredPropertyValue $module 'isr' "$Context module"
        Assert-Distribution $moduleDpc "$Context module DPC"
        Assert-Distribution $moduleIsr "$Context module ISR"
        Assert-ThresholdSummary (Get-RequiredPropertyValue $module 'dpcThresholds' "$Context module") $moduleDpc "$Context module DPC thresholds" 100
        Assert-ThresholdSummary (Get-RequiredPropertyValue $module 'isrThresholds' "$Context module") $moduleIsr "$Context module ISR thresholds" 25
    }

    foreach ($routine in @((Get-RequiredPropertyValue $Capture 'unresolvedRoutines' $Context))) {
        $routineDpc = Get-RequiredPropertyValue $routine 'dpc' "$Context unresolved routine"
        $routineIsr = Get-RequiredPropertyValue $routine 'isr' "$Context unresolved routine"
        Assert-Distribution $routineDpc "$Context unresolved DPC"
        Assert-Distribution $routineIsr "$Context unresolved ISR"
        Assert-ThresholdSummary (Get-RequiredPropertyValue $routine 'dpcThresholds' "$Context unresolved routine") $routineDpc "$Context unresolved DPC thresholds" 100
        Assert-ThresholdSummary (Get-RequiredPropertyValue $routine 'isrThresholds' "$Context unresolved routine") $routineIsr "$Context unresolved ISR thresholds" 25
    }

    return $requestIdText
}

function Get-CaptureIntegrityIssues {
    param([Parameter(Mandatory)]$Capture)

    $issues = [System.Collections.Generic.List[string]]::new()
    $eventsLost = [int](Get-RequiredPropertyValue $Capture 'eventsLost' 'Capture')
    $invalidEventCount = [int](Get-RequiredPropertyValue $Capture 'invalidEventCount' 'Capture')
    $invalidImageEventCount = [int](Get-RequiredPropertyValue $Capture 'invalidImageEventCount' 'Capture')
    $eventLimitReached = [bool](Get-RequiredPropertyValue $Capture 'eventLimitReached' 'Capture')

    if ($eventsLost -lt 0) { $issues.Add('ETW loss count is unavailable') }
    elseif ($eventsLost -gt 0) { $issues.Add("ETW events lost: $eventsLost") }
    if ($invalidEventCount -ne 0) { $issues.Add("invalid latency events: $invalidEventCount") }
    if ($invalidImageEventCount -ne 0) { $issues.Add("invalid image events: $invalidImageEventCount") }
    if ($eventLimitReached) { $issues.Add('capture event limit reached') }

    return @($issues)
}

function Assert-BaselineMetricQuality {
    param(
        [Parameter(Mandatory)][double[]]$Values,
        [Parameter(Mandatory)]$SerializedQuality,
        [Parameter(Mandatory)][string]$Context
    )

    if ($Values.Count -ne $RequiredBaselineWindows) {
        throw "$Context needs exactly $RequiredBaselineWindows values for closure."
    }

    [double[]]$sorted = @($Values | Sort-Object)
    $median = Get-Percentile $sorted 0.50
    $p10 = Get-Percentile $sorted 0.10
    $p90 = Get-Percentile $sorted 0.90
    if ($median -le 0) {
        throw "$Context median is not positive."
    }

    $relativeNoise = ($p90 - $p10) / [Math]::Abs($median)
    [double[]]$early = @([double]$Values[0], [double]$Values[1])
    [double[]]$late = @([double]$Values[3], [double]$Values[4])
    $early = @($early | Sort-Object)
    $late = @($late | Sort-Object)
    $earlyMedian = Get-Percentile $early 0.50
    $lateMedian = Get-Percentile $late 0.50
    $relativeDrift = [Math]::Abs($lateMedian - $earlyMedian) / [Math]::Abs($median)
    $extremeWindows = @()
    for ($index = 0; $index -lt $Values.Count; $index++) {
        if ([Math]::Abs([double]$Values[$index] - $median) / [Math]::Abs($median) -gt $ExtremeWindowRelativeDeviation) {
            $extremeWindows += ($index + 1)
        }
    }

    if ($relativeNoise -gt $MaximumRelativeNoiseFloor) {
        throw "$Context P10-P90 relative spread is $relativeNoise, above $MaximumRelativeNoiseFloor."
    }
    if ($relativeDrift -gt $MaximumRelativeDrift) {
        throw "$Context early/late relative drift is $relativeDrift, above $MaximumRelativeDrift."
    }
    if ($extremeWindows.Count -ne 0) {
        throw "$Context has >50% extreme deviation in window(s) $($extremeWindows -join ', ')."
    }

    if ([int](Get-RequiredPropertyValue $SerializedQuality 'eligibleWindowCount' $Context) -ne $RequiredBaselineWindows) {
        throw "$Context serialized eligible-window count is not $RequiredBaselineWindows."
    }

    Assert-NearlyEqual $median (Get-OptionalPropertyValue $SerializedQuality 'medianMicroseconds') "$Context median"
    Assert-NearlyEqual $p10 (Get-OptionalPropertyValue $SerializedQuality 'p10Microseconds') "$Context p10"
    Assert-NearlyEqual $p90 (Get-OptionalPropertyValue $SerializedQuality 'p90Microseconds') "$Context p90"
    Assert-NearlyEqual $relativeNoise (Get-OptionalPropertyValue $SerializedQuality 'relativeNoiseFloor') "$Context relative noise"
    Assert-NearlyEqual $relativeDrift (Get-OptionalPropertyValue $SerializedQuality 'relativeDrift') "$Context relative drift"

    $serializedExtremeWindows = @((Get-RequiredPropertyValue $SerializedQuality 'extremeWindowNumbers' $Context))
    if ($serializedExtremeWindows.Count -ne 0) {
        throw "$Context serialized quality unexpectedly reports extreme windows."
    }

    $serializedReasons = @((Get-RequiredPropertyValue $SerializedQuality 'reasons' $Context))
    if ($serializedReasons.Count -ne 0) {
        throw "$Context serialized quality unexpectedly reports failure reasons."
    }
}

function Write-CaptureSummary {
    param($Capture, [string]$Prefix = '')

    $dpc = Get-RequiredPropertyValue $Capture 'dpc' 'Capture'
    $isr = Get-RequiredPropertyValue $Capture 'isr' 'Capture'
    $dpcThresholds = Get-RequiredPropertyValue $Capture 'dpcThresholds' 'Capture'
    $isrThresholds = Get-RequiredPropertyValue $Capture 'isrThresholds' 'Capture'
    $dpcCount = [int](Get-RequiredPropertyValue $dpc 'count' 'DPC')
    $isrCount = [int](Get-RequiredPropertyValue $isr 'count' 'ISR')
    $dpcGuidanceCount = [int](Get-RequiredPropertyValue $dpcThresholds 'guidanceExceedanceCount' 'DPC thresholds')
    $isrGuidanceCount = [int](Get-RequiredPropertyValue $isrThresholds 'guidanceExceedanceCount' 'ISR thresholds')

    Write-Host "${Prefix}RequestId:   $(Get-RequiredPropertyValue $Capture 'requestId' 'Capture')"
    Write-Host "${Prefix}Duration:    requested=$(Get-RequiredPropertyValue $Capture 'requestedDurationMilliseconds' 'Capture') ms, actual=$(Format-Value (Get-RequiredPropertyValue $Capture 'actualDurationMilliseconds' 'Capture') ' ms')"
    Write-Host "${Prefix}Integrity:   lost=$(Get-RequiredPropertyValue $Capture 'eventsLost' 'Capture'), invalid=$(Get-RequiredPropertyValue $Capture 'invalidEventCount' 'Capture'), invalid-images=$(Get-RequiredPropertyValue $Capture 'invalidImageEventCount' 'Capture'), event-limit=$(Get-RequiredPropertyValue $Capture 'eventLimitReached' 'Capture')"
    Write-Host "${Prefix}DPC:         count=$dpcCount, p99=$(Format-Value (Get-OptionalPropertyValue $dpc 'p99Microseconds') ' us'), p99.9=$(Format-Value (Get-OptionalPropertyValue $dpc 'p999Microseconds') ' us'), max=$(Format-Value (Get-OptionalPropertyValue $dpc 'maximumMicroseconds') ' us'), >100 us=$dpcGuidanceCount ($(Format-Value (Get-Percent $dpcGuidanceCount $dpcCount) '%'))"
    Write-Host "${Prefix}ISR:         count=$isrCount, p99=$(Format-Value (Get-OptionalPropertyValue $isr 'p99Microseconds') ' us'), p99.9=$(Format-Value (Get-OptionalPropertyValue $isr 'p999Microseconds') ' us'), max=$(Format-Value (Get-OptionalPropertyValue $isr 'maximumMicroseconds') ' us'), >25 us=$isrGuidanceCount ($(Format-Value (Get-Percent $isrGuidanceCount $isrCount) '%'))"
}

if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
    throw "Evidence file was not found: $Path"
}

$resolvedPath = (Resolve-Path -LiteralPath $Path).Path
$document = Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json

$schema = [string](Get-RequiredPropertyValue $document 'schema' 'Evidence')
$protocol = [int](Get-RequiredPropertyValue $document 'protocolVersion' 'Evidence')
$purpose = [string](Get-RequiredPropertyValue $document 'purpose' 'Evidence')
$productVersion = [string](Get-RequiredPropertyValue $document 'productVersion' 'Evidence')
$sourceRevision = [string](Get-RequiredPropertyValue $document 'sourceRevisionId' 'Evidence')
if ($schema -ne $ExpectedSchema) {
    throw "Unexpected evidence schema '$schema'. Expected '$ExpectedSchema'."
}
if ($protocol -ne $ExpectedProtocol) {
    throw "Unexpected protocol version '$protocol'. Expected '$ExpectedProtocol'."
}
if ($productVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "Evidence productVersion '$productVersion' does not use MAJOR.MINOR.PATCH form."
}
if ($sourceRevision -notmatch '^[0-9a-fA-F]{40}$') {
    throw 'Evidence does not carry the full 40-hex clean sourceRevisionId required for closure-grade provenance.'
}

$exportedAtUtcText = [string](Get-RequiredPropertyValue $document 'exportedAtUtc' 'Evidence')
$parsedExportedAtUtc = [DateTimeOffset]::MinValue
if (-not [DateTimeOffset]::TryParse($exportedAtUtcText, [ref]$parsedExportedAtUtc)) {
    throw "Evidence exportedAtUtc '$exportedAtUtcText' is invalid."
}
$null = Get-RequiredPropertyValue $document 'environment' 'Evidence'
$measurementContext = Get-RequiredPropertyValue $document 'measurementContext' 'Evidence'
$scenarioValue = [string](Get-RequiredPropertyValue $measurementContext 'scenario' 'Measurement context')
$scenarioDisplayName = [string](Get-RequiredPropertyValue $measurementContext 'displayName' 'Measurement context')
if ([string]::IsNullOrWhiteSpace($scenarioValue) -or [string]::IsNullOrWhiteSpace($scenarioDisplayName)) {
    throw 'Evidence measurement context is incomplete.'
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$strictClosure = $RequireCleanCapture -or $RequireValidBaseline
if ([string]::IsNullOrWhiteSpace($ExpectedCommit) -and (Get-Command git -ErrorAction SilentlyContinue) -and (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))) {
    $workingTree = @(& git -C $repoRoot status --porcelain --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the Git working tree.'
    }

    if ($workingTree.Count -eq 0) {
        $ExpectedCommit = (& git -C $repoRoot rev-parse HEAD | Select-Object -First 1).Trim()
        if ($LASTEXITCODE -ne 0) {
            throw 'Unable to resolve the current Git HEAD.'
        }
    }
    elseif ($strictClosure) {
        throw 'The repository working tree is dirty. Supply the exact full -ExpectedCommit used for this evidence or return to the clean source revision.'
    }
}

if ($strictClosure -and [string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    throw 'Strict closure verification requires -ExpectedCommit or a clean Git checkout from which the exact HEAD can be resolved.'
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedCommit)) {
    $ExpectedCommit = $ExpectedCommit.Trim()
    if ($ExpectedCommit -notmatch '^[0-9a-fA-F]{7,40}$') {
        throw "Expected commit '$ExpectedCommit' is not a valid Git revision id."
    }
    if ($strictClosure -and $ExpectedCommit.Length -ne 40) {
        throw 'Strict closure verification requires the full 40-hex -ExpectedCommit.'
    }
    if (-not $sourceRevision.StartsWith($ExpectedCommit, [StringComparison]::OrdinalIgnoreCase)) {
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
$integrityIssues = @()
for ($index = 0; $index -lt $captures.Count; $index++) {
    $requestId = Assert-CaptureShape $captures[$index] "capture $($index + 1)"
    $requestIds += $requestId
    foreach ($issue in @(Get-CaptureIntegrityIssues $captures[$index])) {
        $integrityIssues += "capture $($index + 1): $issue"
    }
}
if (@($requestIds | Group-Object | Where-Object Count -gt 1).Count -ne 0) {
    throw 'Evidence contains duplicate capture RequestId values.'
}

$windows = @()
$runtimeWindows = @()
$quality = $null
if ($evidenceType -eq 'baseline') {
    $windows = @((Get-RequiredPropertyValue $document 'windows' 'Baseline evidence'))
    $runtimeWindows = @((Get-RequiredPropertyValue $document 'runtimeWindows' 'Baseline evidence'))
    $quality = Get-RequiredPropertyValue $document 'quality' 'Baseline evidence'
    $baselineMethod = [string](Get-RequiredPropertyValue $document 'baselineMethodVersion' 'Baseline evidence')
    if ($baselineMethod -ne $ExpectedBaselineMethod) {
        throw "Baseline method '$baselineMethod' is not '$ExpectedBaselineMethod'."
    }
    if ($captures.Count -ne $windows.Count -or $captures.Count -ne $runtimeWindows.Count) {
        throw "Baseline evidence is misaligned: captures=$($captures.Count), windows=$($windows.Count), runtimeWindows=$($runtimeWindows.Count)."
    }

    for ($index = 0; $index -lt $captures.Count; $index++) {
        $expectedWindowNumber = $index + 1
        $capture = $captures[$index]
        $window = $windows[$index]
        $runtimeWindow = $runtimeWindows[$index]
        if ([int](Get-RequiredPropertyValue $window 'windowNumber' "window $expectedWindowNumber") -ne $expectedWindowNumber -or
            [int](Get-RequiredPropertyValue $runtimeWindow 'windowNumber' "runtime window $expectedWindowNumber") -ne $expectedWindowNumber) {
            throw "Baseline sequence mismatch at position $expectedWindowNumber."
        }
        if ([string](Get-RequiredPropertyValue $capture 'startedAtUtc' "capture $expectedWindowNumber") -ne
            [string](Get-RequiredPropertyValue $window 'startedAtUtc' "window $expectedWindowNumber")) {
            throw "Baseline timestamp mismatch in window $expectedWindowNumber."
        }
        if ([int](Get-RequiredPropertyValue $capture 'requestedDurationMilliseconds' "capture $expectedWindowNumber") -ne
            [int](Get-RequiredPropertyValue $window 'requestedDurationMilliseconds' "window $expectedWindowNumber")) {
            throw "Baseline requested-duration mismatch in window $expectedWindowNumber."
        }
        Assert-NearlyEqual (Get-RequiredPropertyValue $capture 'actualDurationMilliseconds' "capture $expectedWindowNumber") (Get-RequiredPropertyValue $window 'actualDurationMilliseconds' "window $expectedWindowNumber") "Baseline actual-duration window $expectedWindowNumber"

        $captureDpc = Get-RequiredPropertyValue $capture 'dpc' "capture $expectedWindowNumber"
        $captureIsr = Get-RequiredPropertyValue $capture 'isr' "capture $expectedWindowNumber"
        if ([int](Get-RequiredPropertyValue $window 'dpcEventCount' "window $expectedWindowNumber") -ne [int](Get-RequiredPropertyValue $captureDpc 'count' "capture $expectedWindowNumber DPC") -or
            [int](Get-RequiredPropertyValue $window 'isrEventCount' "window $expectedWindowNumber") -ne [int](Get-RequiredPropertyValue $captureIsr 'count' "capture $expectedWindowNumber ISR")) {
            throw "Baseline event-count mismatch in window $expectedWindowNumber."
        }
        Assert-NearlyEqual (Get-OptionalPropertyValue $captureDpc 'p99Microseconds') (Get-OptionalPropertyValue $window 'dpcP99Microseconds') "Baseline DPC p99 window $expectedWindowNumber"
        Assert-NearlyEqual (Get-OptionalPropertyValue $captureIsr 'p99Microseconds') (Get-OptionalPropertyValue $window 'isrP99Microseconds') "Baseline ISR p99 window $expectedWindowNumber"
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

if ($integrityIssues.Count -ne 0 -and ($RequireCleanCapture -or $RequireValidBaseline)) {
    throw "Evidence capture integrity is not clean: $($integrityIssues -join '; ')."
}

if ($RequireValidBaseline) {
    if ($captures.Count -ne $RequiredBaselineWindows -or $windows.Count -ne $RequiredBaselineWindows -or $runtimeWindows.Count -ne $RequiredBaselineWindows) {
        throw "Baseline is not closure-ready: exactly $RequiredBaselineWindows aligned windows are required."
    }

    $dpcP99Values = @()
    $isrP99Values = @()
    for ($index = 0; $index -lt $windows.Count; $index++) {
        $window = $windows[$index]
        $requested = [int](Get-RequiredPropertyValue $window 'requestedDurationMilliseconds' "window $($index + 1)")
        $actual = [double](Get-RequiredPropertyValue $window 'actualDurationMilliseconds' "window $($index + 1)")
        $dpcCount = [int](Get-RequiredPropertyValue $window 'dpcEventCount' "window $($index + 1)")
        $isrCount = [int](Get-RequiredPropertyValue $window 'isrEventCount' "window $($index + 1)")
        $dpcP99 = Get-OptionalPropertyValue $window 'dpcP99Microseconds'
        $isrP99 = Get-OptionalPropertyValue $window 'isrP99Microseconds'
        $captureIntegrityValid = [bool](Get-RequiredPropertyValue $window 'captureIntegrityValid' "window $($index + 1)")

        if (-not $captureIntegrityValid) {
            throw "Baseline window $($index + 1) is marked capture-integrity invalid."
        }
        if ($requested -lt $MinimumBaselineRequestedMilliseconds -or $actual -lt ($requested * $MinimumBaselineDurationRatio)) {
            throw "Baseline window $($index + 1) does not satisfy duration requirements."
        }
        if ($dpcCount -lt $MinimumBaselineEventsPerMetricWindow -or $isrCount -lt $MinimumBaselineEventsPerMetricWindow) {
            throw "Baseline window $($index + 1) is undersampled: DPC=$dpcCount, ISR=$isrCount."
        }
        if (-not (Test-FinitePositive $dpcP99) -or -not (Test-FinitePositive $isrP99)) {
            throw "Baseline window $($index + 1) lacks finite positive DPC/ISR p99 evidence."
        }
        $dpcP99Values += [double]$dpcP99
        $isrP99Values += [double]$isrP99
    }

    $serializedStatus = [string](Get-RequiredPropertyValue $quality 'status' 'Baseline quality')
    $serializedValid = [bool](Get-RequiredPropertyValue $quality 'isValidForComparison' 'Baseline quality')
    $serializedValidCaptureCount = [int](Get-RequiredPropertyValue $quality 'validCaptureWindowCount' 'Baseline quality')
    $serializedTotalCount = [int](Get-RequiredPropertyValue $quality 'totalWindowCount' 'Baseline quality')
    if ($serializedStatus -ne 'Valid' -or -not $serializedValid -or
        $serializedValidCaptureCount -ne $RequiredBaselineWindows -or
        $serializedTotalCount -ne $RequiredBaselineWindows) {
        throw 'Serialized baseline quality is not closure-ready Valid/5-of-5 evidence.'
    }

    Assert-BaselineMetricQuality $dpcP99Values (Get-RequiredPropertyValue $quality 'dpcP99' 'Baseline quality') 'DPC p99 quality'
    Assert-BaselineMetricQuality $isrP99Values (Get-RequiredPropertyValue $quality 'isrP99' 'Baseline quality') 'ISR p99 quality'
    $qualityReasons = @((Get-RequiredPropertyValue $quality 'reasons' 'Baseline quality'))
    if ($qualityReasons.Count -ne 0) {
        throw 'Serialized Valid baseline unexpectedly contains quality failure reasons.'
    }
}

Write-Host 'Evidence verification passed.' -ForegroundColor Green
Write-Host "Path:            $resolvedPath"
Write-Host "Type:            $evidenceType"
Write-Host "Purpose:         $purpose"
Write-Host "Schema:          $schema"
Write-Host "Protocol:        $protocol"
Write-Host "Product version: $productVersion"
Write-Host "Scenario:        $scenarioDisplayName ($scenarioValue)"
Write-Host "Source revision: $sourceRevision"
Write-Host "Capture IDs:     $($requestIds.Count) unique"
Write-Host "SHA-256:         $sha256"

if ($integrityIssues.Count -eq 0) {
    Write-Host 'Measurement integrity: CLEAN' -ForegroundColor Green
}
else {
    Write-Host "Measurement integrity: WARNING ($($integrityIssues.Count) issue(s))" -ForegroundColor Yellow
    $integrityIssues | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
}

if ($evidenceType -eq 'observation') {
    Write-Host ''
    Write-Host 'Quick diagnostic snapshot summary'
    Write-CaptureSummary $captures[0]
    Write-Host 'Decision status: diagnostic only; a single snapshot is not a benchmark verdict.' -ForegroundColor Yellow

    $runtime = Get-OptionalPropertyValue $document 'runtimeContext'
    if ($null -ne $runtime) {
        $cpuBusy = Get-OptionalPropertyValue $runtime 'systemCpuBusyPercent'
        $powerChanged = Get-OptionalPropertyValue $runtime 'powerContextChanged'
        Write-Host "Runtime context: CPU busy=$(Format-Value $cpuBusy '%'), power-context-changed=$(Format-Value $powerChanged)"
    }
}
else {
    Write-Host ''
    Write-Host 'Repeated decision-baseline summary'
    Write-Host "Windows:         $($captures.Count)"
    Write-Host "Method:          $(Get-RequiredPropertyValue $document 'baselineMethodVersion' 'Baseline evidence')"
    Write-Host "Status:          $(Get-RequiredPropertyValue $quality 'status' 'Baseline quality')"
    Write-Host "Valid compare:   $(Get-RequiredPropertyValue $quality 'isValidForComparison' 'Baseline quality')"

    $dpcQuality = Get-RequiredPropertyValue $quality 'dpcP99' 'Baseline quality'
    $isrQuality = Get-RequiredPropertyValue $quality 'isrP99' 'Baseline quality'
    Write-Host "DPC p99 quality: median=$(Format-Value (Get-OptionalPropertyValue $dpcQuality 'medianMicroseconds') ' us'), noise=$(Format-Value (Get-OptionalPropertyValue $dpcQuality 'relativeNoiseFloor')), drift=$(Format-Value (Get-OptionalPropertyValue $dpcQuality 'relativeDrift'))"
    Write-Host "ISR p99 quality: median=$(Format-Value (Get-OptionalPropertyValue $isrQuality 'medianMicroseconds') ' us'), noise=$(Format-Value (Get-OptionalPropertyValue $isrQuality 'relativeNoiseFloor')), drift=$(Format-Value (Get-OptionalPropertyValue $isrQuality 'relativeDrift'))"

    if ($RequireValidBaseline) {
        Write-Host 'Decision-baseline closure gate: PASS' -ForegroundColor Green
    }
}
