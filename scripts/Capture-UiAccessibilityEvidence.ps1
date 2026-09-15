[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$AppTarget,

    [Parameter(Mandatory)]
    [ValidateSet('Light', 'Dark', 'HighContrast', 'TextScale', 'Narrow', 'Keyboard')]
    [string]$Scenario,

    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

Set-StrictMode -Version 3.0
$ErrorActionPreference = 'Stop'

function Invoke-WinApp {
    param([Parameter(Mandatory)][string[]]$Arguments)

    $output = & winapp @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "winapp $($Arguments -join ' ') failed with exit code $LASTEXITCODE.`n$($output -join [Environment]::NewLine)"
    }

    return @($output)
}

if (-not (Get-Command winapp -ErrorAction SilentlyContinue)) {
    throw 'Microsoft WinApp CLI was not found. Install Microsoft.winappcli with WinGet before capturing UI Automation evidence.'
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

$stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$prefix = "LatencyPilot-ui-$($Scenario.ToLowerInvariant())-$stamp"
$statusPath = Join-Path $resolvedOutput "$prefix-status.json"
$treePath = Join-Path $resolvedOutput "$prefix-uia.txt"
$interactivePath = Join-Path $resolvedOutput "$prefix-interactive.txt"
$screenshotPath = Join-Path $resolvedOutput "$prefix.png"
$manifestPath = Join-Path $resolvedOutput "$prefix-manifest.json"

$status = Invoke-WinApp @('ui', 'status', '-a', $AppTarget, '--json')
$status | Set-Content -LiteralPath $statusPath -Encoding UTF8

$tree = Invoke-WinApp @('ui', 'inspect', '-a', $AppTarget, '--depth', '8')
$tree | Set-Content -LiteralPath $treePath -Encoding UTF8

$interactive = Invoke-WinApp @('ui', 'inspect', '-a', $AppTarget, '--interactive', '--depth', '8')
$interactive | Set-Content -LiteralPath $interactivePath -Encoding UTF8

$null = Invoke-WinApp @('ui', 'screenshot', '-a', $AppTarget, '--output', $screenshotPath)
if (-not (Test-Path -LiteralPath $screenshotPath -PathType Leaf)) {
    throw "WinApp CLI did not produce the expected screenshot: $screenshotPath"
}

$artifacts = @($statusPath, $treePath, $interactivePath, $screenshotPath) | ForEach-Object {
    $file = Get-Item -LiteralPath $_
    [pscustomobject]@{
        path = $file.FullName
        length = $file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$manifest = [ordered]@{
    schema = 'latencypilot-ui-accessibility-evidence-v1'
    capturedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    appTarget = $AppTarget
    scenario = $Scenario
    artifacts = $artifacts
    interpretation = [ordered]@{
        automatedCaptureComplete = $true
        manualReviewStillRequired = $true
        review = @(
            'Confirm every actionable control has a meaningful accessible name and role in the UIA tree.',
            'Confirm logical keyboard focus order and keyboard invocation without pointer input.',
            'Run Accessibility Insights FastPass and record any failures/exemptions.',
            'For HighContrast/TextScale/Narrow scenarios, visually compare the screenshot with the live app for clipping, overlap, truncation, hidden state, and contrast failures.',
            'Run a focused Narrator pass for the primary observation/baseline/export flow.'
        )
    }
}

$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Write-Host 'WinUI accessibility evidence captured.' -ForegroundColor Green
Write-Host "Scenario: $Scenario"
Write-Host "UIA tree: $treePath"
Write-Host "Interactive tree: $interactivePath"
Write-Host "Screenshot: $screenshotPath"
Write-Host "Manifest: $manifestPath"
Write-Host 'Manual accessibility interpretation remains required; this command captures reproducible evidence and does not declare accessibility compliance.' -ForegroundColor Yellow
