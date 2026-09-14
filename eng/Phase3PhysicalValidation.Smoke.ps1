Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot '..\tools\LatencyPilot.PhysicalValidation\LatencyPilot.PhysicalValidation.csproj'

& dotnet run --project $project --configuration Release -- inspect
if ($LASTEXITCODE -ne 0) {
    throw "Physical validation harness inspect command failed with exit code $LASTEXITCODE."
}

& dotnet run --project $project --configuration Release -- apply --experiment 00000000-0000-0000-0000-000000000001
if ($LASTEXITCODE -eq 0) {
    throw 'A mutation-capable command succeeded without --confirm-physical-mutation.'
}

Write-Host 'Phase 3 physical validation harness smoke passed.'
