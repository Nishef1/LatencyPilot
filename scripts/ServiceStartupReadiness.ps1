Set-StrictMode -Version Latest

function Get-LatencyPilotLogProperty {
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

function Wait-LatencyPilotServiceStartupReadiness {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$LogDirectory,

        [Parameter(Mandatory = $true)]
        [DateTimeOffset]$NotBeforeUtc,

        [TimeSpan]$Timeout = [TimeSpan]::FromSeconds(8)
    )

    if ($Timeout -lt [TimeSpan]::Zero) {
        throw [ArgumentOutOfRangeException]::new('Timeout', 'Timeout cannot be negative.')
    }

    $notBefore = $NotBeforeUtc.ToUniversalTime()
    $deadline = [DateTimeOffset]::UtcNow.Add($Timeout)

    do {
        $latestStartupEvent = $null

        if (Test-Path -LiteralPath $LogDirectory -PathType Container) {
            $logFiles = Get-ChildItem -LiteralPath $LogDirectory -Filter 'latencypilot-service-*.json' -File -ErrorAction SilentlyContinue |
                Sort-Object LastWriteTimeUtc -Descending |
                Select-Object -First 3

            foreach ($file in $logFiles) {
                try {
                    $lines = @(Get-Content -LiteralPath $file.FullName -Tail 512 -ErrorAction Stop)
                }
                catch {
                    continue
                }

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

                    $timestampValue = Get-LatencyPilotLogProperty -Event $event -Name '@t'
                    $messageValue = Get-LatencyPilotLogProperty -Event $event -Name '@m'
                    if ($null -eq $timestampValue -or [string]::IsNullOrWhiteSpace([string]$messageValue)) {
                        continue
                    }

                    try {
                        $timestamp = [DateTimeOffset]::Parse([string]$timestampValue).ToUniversalTime()
                    }
                    catch {
                        continue
                    }

                    if ($timestamp -lt $notBefore) {
                        continue
                    }

                    $message = [string]$messageValue
                    $kind = $null
                    $unresolvedCount = $null

                    if ($message -match '^Mutation journal initialized\. Unresolved experiment count: (?<count>\d+)\. Mutation remains disabled by protocol\.$') {
                        $kind = 'Ready'
                        $unresolvedCount = [int]$Matches['count']
                    }
                    elseif ($message -like 'Mutation journal startup inspection failed:*') {
                        $kind = 'Failure'
                    }
                    else {
                        continue
                    }

                    if ($null -eq $latestStartupEvent -or $timestamp -gt $latestStartupEvent.TimestampUtc) {
                        $latestStartupEvent = [pscustomobject]@{
                            Kind = $kind
                            TimestampUtc = $timestamp
                            UnresolvedCount = $unresolvedCount
                            Message = $message
                            LogPath = $file.FullName
                        }
                    }
                }
            }
        }

        if ($null -ne $latestStartupEvent) {
            if ($latestStartupEvent.Kind -eq 'Failure') {
                throw [InvalidOperationException]::new($latestStartupEvent.Message)
            }

            return [pscustomobject]@{
                TimestampUtc = $latestStartupEvent.TimestampUtc
                UnresolvedCount = [int]$latestStartupEvent.UnresolvedCount
                LogPath = $latestStartupEvent.LogPath
            }
        }

        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            break
        }

        Start-Sleep -Milliseconds 100
    }
    while ($true)

    throw [TimeoutException]::new(
        "No current LatencyPilot mutation-journal startup readiness event appeared within $([Math]::Round($Timeout.TotalSeconds, 1)) seconds in '$LogDirectory'.")
}
