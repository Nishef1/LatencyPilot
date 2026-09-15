[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CandidateManifest,

    [Parameter(Mandatory)]
    [string]$ClosureRecord
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RequiredClosureChecks = @(
    'phase2PhysicalClosed',
    'gateAPassed',
    'gateBImplemented',
    'gateCPassed',
    'gateDPassed',
    'usbPhysicalPassed',
    'nicPhysicalPassed',
    'accessibilityPassed',
    'cleanInstallPassed',
    'upgradePassed',
    'uninstallPassed',
    'rebootRecoveryPassed',
    'crashRecoveryPassed',
    'representativeHardwarePassed'
)

$RequiredEvidenceCategories = @(
    'phase2',
    'gate-a',
    'gate-b',
    'gate-c',
    'gate-d',
    'usb',
    'nic',
    'accessibility',
    'package-lifecycle',
    'recovery',
    'hardware'
)

function Invoke-Native {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE."
    }
}

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $raw = Get-Content -LiteralPath $resolved -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        throw "JSON file is empty: $resolved"
    }

    try {
        $document = $raw | ConvertFrom-Json
    }
    catch {
        throw "JSON file is invalid: $resolved`n$($_.Exception.Message)"
    }

    return [pscustomobject]@{
        Path = $resolved
        Sha256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
        Document = $document
    }
}

function Assert-ExactFileHash {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$ExpectedSha256,
        [Parameter(Mandatory)][string]$Description
    )

    if ($ExpectedSha256 -notmatch '^[0-9a-fA-F]{64}$') {
        throw "$Description has an invalid SHA-256 value '$ExpectedSha256'."
    }

    $resolved = (Resolve-Path -LiteralPath $Path -ErrorAction Stop).Path
    $actual = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $ExpectedSha256.ToLowerInvariant()) {
        throw "$Description hash mismatch. Expected $ExpectedSha256, actual $actual: $resolved"
    }

    return $resolved
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try {
    foreach ($requiredCommand in 'git', 'gh') {
        if (-not (Get-Command $requiredCommand -ErrorAction SilentlyContinue)) {
            throw "$requiredCommand is required."
        }
    }
    Invoke-Native -FilePath 'gh' -ArgumentList @('auth', 'status')

    $candidateFile = Read-JsonFile -Path $CandidateManifest
    $candidate = $candidateFile.Document
    if ($candidate.schema -ne 'latencypilot-final-candidate-v1') {
        throw "Candidate manifest schema '$($candidate.schema)' is not latencypilot-final-candidate-v1."
    }
    if ($candidate.version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Candidate version '$($candidate.version)' is invalid."
    }
    if ($candidate.commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw "Candidate commit '$($candidate.commit)' is not a full Git revision."
    }
    if ($candidate.signing -ne 'authenticode-sha256-rfc3161') {
        throw "Final candidate signing mode '$($candidate.signing)' is not acceptable."
    }

    $closureFile = Read-JsonFile -Path $ClosureRecord
    $closure = $closureFile.Document
    if ($closure.schema -ne 'latencypilot-final-closure-v1') {
        throw "Closure record schema '$($closure.schema)' is not latencypilot-final-closure-v1."
    }
    if ($closure.version -ne $candidate.version) {
        throw "Closure version '$($closure.version)' does not match candidate '$($candidate.version)'."
    }
    if ($closure.commit -ne $candidate.commit) {
        throw "Closure commit '$($closure.commit)' does not match candidate '$($candidate.commit)'."
    }
    if ($closure.candidateManifestSha256 -ne $candidateFile.Sha256) {
        throw "Closure record does not bind to the exact candidate manifest SHA-256 $($candidateFile.Sha256)."
    }

    foreach ($checkName in $RequiredClosureChecks) {
        $property = $closure.checks.PSObject.Properties[$checkName]
        if ($null -eq $property -or $property.Value -ne $true) {
            throw "Final closure check '$checkName' is missing or not true."
        }
    }

    $evidence = @($closure.evidence)
    if ($evidence.Count -eq 0) {
        throw 'Final closure record contains no evidence files.'
    }

    foreach ($category in $RequiredEvidenceCategories) {
        $matches = @($evidence | Where-Object { $_.category -eq $category })
        if ($matches.Count -eq 0) {
            throw "Final closure is missing evidence category '$category'."
        }

        foreach ($entry in $matches) {
            if ([string]::IsNullOrWhiteSpace([string]$entry.path)) {
                throw "Evidence category '$category' contains an empty path."
            }
            Assert-ExactFileHash -Path ([string]$entry.path) -ExpectedSha256 ([string]$entry.sha256) -Description "Evidence '$category'" | Out-Null
        }
    }

    $branch = (git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') {
        throw "Final publication must run from main. Current branch: '$branch'."
    }
    if (@(git status --porcelain).Count -ne 0) {
        throw 'The working tree must be clean before final publication.'
    }

    Invoke-Native -FilePath 'git' -ArgumentList @('fetch', 'origin', 'main', '--quiet')
    $head = (git rev-parse HEAD).Trim()
    $originMain = (git rev-parse origin/main).Trim()
    if ($head -ne $candidate.commit -or $originMain -ne $candidate.commit) {
        throw "Candidate commit $($candidate.commit) must still be exact local/remote main. HEAD=$head origin/main=$originMain."
    }

    $testRunsJson = gh run list --workflow ci.yml --commit $candidate.commit --status completed --limit 10 --json databaseId,headSha,conclusion
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to query exact-commit Tests evidence.'
    }
    $greenTestRun = @($testRunsJson | ConvertFrom-Json) |
        Where-Object { $_.headSha -eq $candidate.commit -and $_.conclusion -eq 'success' } |
        Select-Object -First 1
    if ($null -eq $greenTestRun) {
        throw "No successful Tests workflow exists for final candidate $($candidate.commit)."
    }
    if ([long]$candidate.testsWorkflowRun -ne [long]$greenTestRun.databaseId) {
        throw "Candidate Tests run $($candidate.testsWorkflowRun) is not the current exact-commit successful run $($greenTestRun.databaseId)."
    }

    $candidateRoot = Split-Path -Parent $candidateFile.Path
    $assets = @()
    foreach ($assetName in 'setup', 'setupChecksum', 'portable', 'portableChecksum') {
        $asset = $candidate.assets.PSObject.Properties[$assetName]
        if ($null -eq $asset) {
            throw "Candidate manifest is missing asset '$assetName'."
        }
        $assetPath = Join-Path $candidateRoot ([string]$asset.Value.path)
        $assets += Assert-ExactFileHash -Path $assetPath -ExpectedSha256 ([string]$asset.Value.sha256) -Description "Candidate asset '$assetName'"
    }

    $tag = "v$($candidate.version)"
    gh release view $tag --json tagName *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "GitHub release $tag already exists. Final versions are immutable."
    }
    git ls-remote --exit-code --tags origin "refs/tags/$tag" *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "Remote tag $tag already exists. Final versions are immutable."
    }

    $releaseArguments = @(
        'release', 'create', $tag
    ) + $assets + @(
        '--target', $candidate.commit,
        '--title', "LatencyPilot $($candidate.version)",
        '--generate-notes'
    )
    Invoke-Native -FilePath 'gh' -ArgumentList $releaseArguments

    Write-Host ''
    Write-Host "Published physically closed final release $tag from exact staged candidate $($candidate.commit)." -ForegroundColor Green
    Write-Host "Candidate manifest SHA-256: $($candidateFile.Sha256)"
    Write-Host "Closure record SHA-256:    $($closureFile.Sha256)"
}
finally {
    Pop-Location
}
