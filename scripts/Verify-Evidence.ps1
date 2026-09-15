[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string]$Path,

    [string]$ExpectedCommit,

    [string]$ExpectedSha256,

    [switch]$RequireCleanCapture,

    [switch]$RequireValidBaseline
)

# Windows PowerShell 5.1 is the built-in owner-local shell on supported Windows.
# Some constrained installations do not expose Get-FileHash, so provide only the
# SHA-256 surface used by the canonical verifier rather than requiring PowerShell 7.
if ($null -eq (Get-Command Get-FileHash -ErrorAction SilentlyContinue)) {
    function Get-FileHash {
        [CmdletBinding()]
        param(
            [Parameter(Mandatory)]
            [string]$LiteralPath,

            [ValidateSet('SHA256')]
            [string]$Algorithm = 'SHA256'
        )

        $resolved = (Resolve-Path -LiteralPath $LiteralPath).Path
        $stream = [System.IO.File]::OpenRead($resolved)
        try {
            $hasher = [System.Security.Cryptography.SHA256]::Create()
            try {
                $bytes = $hasher.ComputeHash($stream)
            }
            finally {
                $hasher.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }

        [pscustomobject]@{
            Algorithm = $Algorithm
            Hash = ([System.BitConverter]::ToString($bytes)).Replace('-', '')
            Path = $resolved
        }
    }
}

$coreVerifier = Join-Path $PSScriptRoot 'Verify-Evidence.Core.ps1'
if (-not (Test-Path -LiteralPath $coreVerifier -PathType Leaf)) {
    throw "Canonical evidence verifier core was not found: $coreVerifier"
}

& $coreVerifier @PSBoundParameters
