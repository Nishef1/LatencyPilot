[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Path,

    [string]$ExpectedCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

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

$requestIds = @()
if ($null -ne $document.capture) {
    $requestIds = @([string]$document.capture.requestId)
}
elseif ($null -ne $document.captures) {
    $requestIds = @($document.captures | ForEach-Object { [string]$_.requestId })
}

if ($requestIds.Count -eq 0 -or $requestIds | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
    throw 'Evidence is missing one or more capture RequestId values.'
}

$duplicateRequestIds = $requestIds | Group-Object | Where-Object Count -gt 1
if ($duplicateRequestIds) {
    throw 'Evidence contains duplicate capture RequestId values.'
}

$sha256 = (Get-FileHash -LiteralPath $resolvedPath -Algorithm SHA256).Hash.ToLowerInvariant()
$scenario = if ($null -ne $document.measurementContext) { [string]$document.measurementContext.displayName } else { 'unknown' }
$evidenceType = if ($null -ne $document.capture) { 'observation' } elseif ($null -ne $document.captures) { 'baseline' } else { 'unknown' }

Write-Host 'LatencyPilot evidence verification passed.' -ForegroundColor Green
Write-Host "Path:            $resolvedPath"
Write-Host "Type:            $evidenceType"
Write-Host "Schema:          $($document.schema)"
Write-Host "Product version: $($document.productVersion)"
Write-Host "Scenario:        $scenario"
Write-Host "Source revision: $sourceRevision"
Write-Host "Capture IDs:     $($requestIds.Count) unique"
Write-Host "SHA-256:         $sha256"
