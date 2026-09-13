[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath,

    [Parameter(Mandatory)]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceHead = ''
$sourceState = 'unavailable'
$evidenceCommit = ''

$git = Get-Command git -ErrorAction SilentlyContinue
$gitMetadataAvailable = $null -ne $git -and (Test-Path -LiteralPath (Join-Path $repoRoot '.git'))

if ($gitMetadataAvailable) {
    $headOutput = & git -C $repoRoot rev-parse HEAD 2>$null
    if ($LASTEXITCODE -eq 0) {
        $sourceHead = ($headOutput | Select-Object -First 1).Trim()

        $statusOutput = @(& git -C $repoRoot status --porcelain --untracked-files=normal 2>$null)
        if ($LASTEXITCODE -eq 0) {
            $sourceState = if ($statusOutput.Count -eq 0) { 'clean' } else { 'dirty' }
            if ($sourceState -eq 'clean') {
                $evidenceCommit = $sourceHead
            }
        }
    }
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}

@(
    "version=$Version"
    "commit=$evidenceCommit"
    "source_head=$sourceHead"
    "source_state=$sourceState"
    'build_mode=owner-local-source'
) | Set-Content -LiteralPath $OutputPath -Encoding UTF8
