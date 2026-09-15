[CmdletBinding()]
param(
    [string]$OutputPath,
    [ValidateRange(1, 10)]
    [int]$MaxFilesPerComponent = 3,
    [ValidateRange(50, 2000)]
    [int]$MaxEventsPerFile = 500
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')
    $OutputPath = Join-Path (Get-Location) "LatencyPilot-diagnostics-$stamp.zip"
}
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if ([System.IO.Path]::GetExtension($OutputPath) -ne '.zip') {
    throw 'OutputPath must use the .zip extension.'
}

$allowedFields = @(
    '@t',
    '@l',
    '@mt',
    'Component',
    'ProcessId',
    'EventId',
    'RequestId',
    'Command',
    'Status',
    'ErrorCode',
    'ElapsedMilliseconds',
    'RequestedDurationMilliseconds',
    'ActualDurationMilliseconds'
)

function Export-RedactedLogs {
    param(
        [Parameter(Mandatory)]
        [string]$SourceDirectory,
        [Parameter(Mandatory)]
        [string]$DestinationDirectory
    )

    if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
    $files = Get-ChildItem -LiteralPath $SourceDirectory -File -Filter '*.json' |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First $MaxFilesPerComponent

    foreach ($file in $files) {
        $destination = Join-Path $DestinationDirectory ($file.BaseName + '.redacted.jsonl')
        $output = New-Object System.Collections.Generic.List[string]
        $lines = @(Get-Content -LiteralPath $file.FullName -Tail $MaxEventsPerFile -ErrorAction Stop)

        foreach ($line in $lines) {
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            try {
                $event = $line | ConvertFrom-Json -ErrorAction Stop
            }
            catch {
                continue
            }

            $safe = [ordered]@{}
            foreach ($field in $allowedFields) {
                $property = $event.PSObject.Properties[$field]
                if ($null -ne $property -and $null -ne $property.Value) {
                    $safe[$field] = $property.Value
                }
            }

            if ($safe.Count -ne 0) {
                $output.Add(($safe | ConvertTo-Json -Compress -Depth 4))
            }
        }

        if ($output.Count -ne 0) {
            [System.IO.File]::WriteAllLines($destination, $output, [System.Text.UTF8Encoding]::new($false))
        }
    }
}

$stage = Join-Path ([System.IO.Path]::GetTempPath()) ("LatencyPilot-Diagnostics-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $stage | Out-Null

try {
    $appLogs = Join-Path $env:LOCALAPPDATA 'LatencyPilot\Logs\App'
    $serviceLogs = Join-Path $env:ProgramData 'LatencyPilot\Logs\Service'
    Export-RedactedLogs -SourceDirectory $appLogs -DestinationDirectory (Join-Path $stage 'AppLogs')
    Export-RedactedLogs -SourceDirectory $serviceLogs -DestinationDirectory (Join-Path $stage 'ServiceLogs')

    $installRoot = $null
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $installRoot = Join-Path $env:ProgramFiles 'LatencyPilot'
    }
    if ($null -ne $installRoot) {
        foreach ($name in 'VERSION.txt', 'BUILD_INFO.txt') {
            $source = Join-Path $installRoot $name
            if (Test-Path -LiteralPath $source -PathType Leaf) {
                Copy-Item -LiteralPath $source -Destination (Join-Path $stage $name)
            }
        }
    }

    @(
        'LatencyPilot redacted local diagnostics bundle'
        ('created_utc=' + [DateTimeOffset]::UtcNow.ToString('O'))
        'raw_messages=excluded'
        'exception_text=excluded'
        'device_inventory=excluded'
        'evidence_artifacts=excluded'
        'upload=none'
    ) | Set-Content -LiteralPath (Join-Path $stage 'BUNDLE_INFO.txt') -Encoding UTF8

    $parent = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Force -Path $parent | Out-Null
    }
    Remove-Item -LiteralPath $OutputPath -Force -ErrorAction SilentlyContinue
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $OutputPath -CompressionLevel Optimal
    Write-Host "Created redacted diagnostics bundle: $OutputPath"
}
finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
