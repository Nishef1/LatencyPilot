[CmdletBinding()]
param(
    [string]$Version,
    [switch]$ReplaceExisting
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Native {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,
        [Parameter(ValueFromRemainingArguments)]
        [string[]]$Arguments
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FilePath exited with code $LASTEXITCODE."
    }
}

function Resolve-InnoSetupCompiler {
    $defaultPath = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
    if (Test-Path -LiteralPath $defaultPath -PathType Leaf) {
        return $defaultPath
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw 'Inno Setup 6 compiler (ISCC.exe) was not found.'
    }

    return $command.Source
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw 'git is required.'
    }
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet is required.'
    }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI (gh) is required.'
    }

    Invoke-Native gh auth status

    $branch = (git branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') {
        throw "Releases must be published from main. Current branch: '$branch'."
    }

    $workingTree = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the Git working tree.'
    }
    if ($workingTree.Count -ne 0) {
        throw 'The working tree must be clean before publishing a release.'
    }

    Invoke-Native git fetch origin main --quiet

    $commit = (git rev-parse HEAD).Trim()
    $originMain = (git rev-parse origin/main).Trim()
    if ($commit -ne $originMain) {
        throw "HEAD ($commit) must exactly match origin/main ($originMain) before publishing."
    }

    if ([string]::IsNullOrWhiteSpace($Version)) {
        $Version = (Get-Content -LiteralPath 'RELEASE_VERSION' -Raw).Trim()
    }
    if ($Version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        throw 'Version must be exactly three numeric components: MAJOR.MINOR.PATCH.'
    }

    $projectVersion = (& dotnet msbuild src/LatencyPilot.App/LatencyPilot.App.csproj -nologo -getProperty:Version).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to read the project Version property.'
    }
    if ($projectVersion -ne $Version) {
        throw "Requested release version '$Version' does not match repository Version '$projectVersion'."
    }

    $revision = 'unspecified'
    if (Test-Path -LiteralPath 'RELEASE_REVISION' -PathType Leaf) {
        $revision = (Get-Content -LiteralPath 'RELEASE_REVISION' -Raw).Trim()
    }

    $testRunsJson = gh run list --workflow ci.yml --commit $commit --status completed --limit 10 --json databaseId,headSha,conclusion
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to query GitHub Actions test evidence.'
    }
    $testRuns = @($testRunsJson | ConvertFrom-Json)
    $greenTestRun = $testRuns |
        Where-Object { $_.headSha -eq $commit -and $_.conclusion -eq 'success' } |
        Select-Object -First 1
    if ($null -eq $greenTestRun) {
        throw "No successful Tests workflow run exists for commit $commit. Let CI pass before publishing."
    }

    $tag = "v$Version"
    $existingRelease = $null
    gh release view $tag --json tagName *> $null
    if ($LASTEXITCODE -eq 0) {
        $existingRelease = $tag
    }

    $remoteTagExists = $false
    git ls-remote --exit-code --tags origin "refs/tags/$tag" *> $null
    if ($LASTEXITCODE -eq 0) {
        $remoteTagExists = $true
    }

    if (($null -ne $existingRelease -or $remoteTagExists) -and -not $ReplaceExisting) {
        throw "Release/tag $tag already exists. Re-run with -ReplaceExisting only when replacement is intentional."
    }

    $artifactsRoot = Join-Path $repoRoot 'artifacts'
    $payloadRoot = Join-Path $artifactsRoot 'payload'
    $appOutput = Join-Path $payloadRoot 'App'
    $serviceOutput = Join-Path $payloadRoot 'Service'
    $installerOutput = Join-Path $artifactsRoot 'installer'
    $portableOutput = Join-Path $artifactsRoot 'portable'

    Remove-Item -LiteralPath $payloadRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $installerOutput -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $portableOutput -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $payloadRoot, $installerOutput, $portableOutput | Out-Null

    Write-Host "Publishing LatencyPilot $Version (revision $revision) from $commit"
    Write-Host "Validated by Tests workflow run $($greenTestRun.databaseId)."

    Invoke-Native dotnet restore LatencyPilot.slnx --runtime win-x64
    Invoke-Native dotnet build LatencyPilot.slnx --configuration Release --no-restore

    Invoke-Native dotnet publish src/LatencyPilot.App/LatencyPilot.App.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $appOutput `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishReadyToRun=false

    $pri = Join-Path $appOutput 'LatencyPilot.pri'
    if (-not (Test-Path -LiteralPath $pri -PathType Leaf) -or (Get-Item -LiteralPath $pri).Length -le 0) {
        throw 'LatencyPilot.pri is missing or empty in the WinUI publish output.'
    }

    Invoke-Native dotnet publish src/LatencyPilot.Service/LatencyPilot.Service.csproj `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --no-restore `
        --output $serviceOutput `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:PublishReadyToRun=false

    Copy-Item scripts/Install-Service.ps1 (Join-Path $payloadRoot 'Install-Service.ps1')
    Copy-Item scripts/Uninstall-Service.ps1 (Join-Path $payloadRoot 'Uninstall-Service.ps1')
    Copy-Item docs/PHYSICAL_VALIDATION.md (Join-Path $payloadRoot 'PHYSICAL_VALIDATION.md')
    Copy-Item docs/PORTABLE.md (Join-Path $payloadRoot 'PORTABLE.md')
    Copy-Item docs/DIAGNOSTICS.md (Join-Path $payloadRoot 'DIAGNOSTICS.md')
    Copy-Item README.md (Join-Path $payloadRoot 'README.md')
    Copy-Item LICENSE (Join-Path $payloadRoot 'LICENSE')
    Set-Content -LiteralPath (Join-Path $payloadRoot 'VERSION.txt') -Value $Version -NoNewline
    @(
        "version=$Version"
        "release_revision=$revision"
        "commit=$commit"
        "tests_workflow_run=$($greenTestRun.databaseId)"
        'build_mode=owner-local'
        'deployment=self-contained-offline'
    ) | Set-Content -LiteralPath (Join-Path $payloadRoot 'BUILD_INFO.txt')

    $env:LATENCYPILOT_VERSION = $Version
    $iscc = Resolve-InnoSetupCompiler
    Invoke-Native $iscc installer/LatencyPilot.iss

    $setup = Join-Path $installerOutput "LatencyPilot-$Version-win-x64-setup.exe"
    if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
        throw "Setup output was not created: $setup"
    }
    $setupHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $setup).Hash.ToLowerInvariant()
    $setupChecksum = "$setup.sha256"
    Set-Content -LiteralPath $setupChecksum -Value "$setupHash  $(Split-Path $setup -Leaf)" -NoNewline

    $portableDirectory = Join-Path $portableOutput "LatencyPilot-$Version-win-x64-portable"
    New-Item -ItemType Directory -Force -Path $portableDirectory | Out-Null
    Copy-Item -Path (Join-Path $appOutput '*') -Destination $portableDirectory -Recurse -Force
    Copy-Item -Path $serviceOutput -Destination (Join-Path $portableDirectory 'Service') -Recurse -Force
    Get-ChildItem -LiteralPath $payloadRoot -File | Copy-Item -Destination $portableDirectory -Force

    $portable = "$portableDirectory.zip"
    Compress-Archive -Path $portableDirectory -DestinationPath $portable -CompressionLevel Optimal -Force
    $portableHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $portable).Hash.ToLowerInvariant()
    $portableChecksum = "$portable.sha256"
    Set-Content -LiteralPath $portableChecksum -Value "$portableHash  $(Split-Path $portable -Leaf)" -NoNewline

    if ($ReplaceExisting) {
        gh release view $tag *> $null
        if ($LASTEXITCODE -eq 0) {
            Invoke-Native gh release delete $tag --cleanup-tag --yes
        }
        else {
            git ls-remote --exit-code --tags origin "refs/tags/$tag" *> $null
            if ($LASTEXITCODE -eq 0) {
                Invoke-Native git push origin ":refs/tags/$tag"
            }
        }
    }

    Invoke-Native gh release create $tag `
        $setup `
        $setupChecksum `
        $portable `
        $portableChecksum `
        --target $commit `
        --title "LatencyPilot $Version" `
        --prerelease `
        --generate-notes

    Write-Host ''
    Write-Host "Published $tag from $commit."
    Write-Host "Setup SHA-256:    $setupHash"
    Write-Host "Portable SHA-256: $portableHash"
}
finally {
    Pop-Location
}
