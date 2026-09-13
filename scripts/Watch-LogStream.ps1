[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('APP', 'SERVICE')]
    [string]$Component,

    [Parameter(Mandatory = $true)]
    [string]$Directory,

    [Parameter(Mandatory = $true)]
    [string]$Pattern
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-EventValue {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Event,
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    $property = $Event.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Get-LevelLabel {
    param([object]$Value)

    if ($null -eq $Value -or [string]::IsNullOrWhiteSpace([string]$Value)) {
        return 'INF'
    }

    switch (([string]$Value).ToUpperInvariant()) {
        'VERBOSE' { return 'VRB' }
        'VRB' { return 'VRB' }
        'DEBUG' { return 'DBG' }
        'DBG' { return 'DBG' }
        'INFORMATION' { return 'INF' }
        'INFO' { return 'INF' }
        'INF' { return 'INF' }
        'WARNING' { return 'WRN' }
        'WARN' { return 'WRN' }
        'WRN' { return 'WRN' }
        'ERROR' { return 'ERR' }
        'ERR' { return 'ERR' }
        'FATAL' { return 'FTL' }
        'FTL' { return 'FTL' }
        default { return ([string]$Value).ToUpperInvariant() }
    }
}

function Write-LogLine {
    param([Parameter(Mandatory = $true)][string]$Line)

    try {
        $event = $Line | ConvertFrom-Json
        $timestampValue = Get-EventValue -Event $event -Name '@t'
        $levelValue = Get-EventValue -Event $event -Name '@l'
        $messageValue = Get-EventValue -Event $event -Name '@m'
        $exceptionValue = Get-EventValue -Event $event -Name '@x'

        $timestamp = if ($null -ne $timestampValue) {
            ([DateTimeOffset]::Parse([string]$timestampValue)).ToLocalTime().ToString('HH:mm:ss.fff')
        }
        else {
            '--:--:--.---'
        }

        $level = Get-LevelLabel -Value $levelValue
        $message = if ([string]::IsNullOrWhiteSpace([string]$messageValue)) {
            $Line
        }
        else {
            [string]$messageValue
        }

        $color = switch ($level) {
            'ERR' { 'Red' }
            'FTL' { 'Red' }
            'WRN' { 'Yellow' }
            'DBG' { 'DarkGray' }
            'VRB' { 'DarkGray' }
            default { if ($Component -eq 'SERVICE') { 'Cyan' } else { 'Green' } }
        }

        Write-Host "[$timestamp][$Component][$level] $message" -ForegroundColor $color
        if (-not [string]::IsNullOrWhiteSpace([string]$exceptionValue)) {
            Write-Host ([string]$exceptionValue) -ForegroundColor Red
        }
    }
    catch {
        Write-Host "[$Component] $Line"
    }
}

while (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
    Start-Sleep -Milliseconds 250
}

$file = $null
while ($null -eq $file) {
    $file = Get-ChildItem -LiteralPath $Directory -Filter $Pattern -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1

    if ($null -eq $file) {
        Start-Sleep -Milliseconds 250
    }
}

Write-Host "[$Component] following $($file.FullName)" -ForegroundColor DarkGray
Get-Content -LiteralPath $file.FullName -Tail 0 -Wait | ForEach-Object {
    if (-not [string]::IsNullOrWhiteSpace($_)) {
        Write-LogLine -Line $_
    }
}
