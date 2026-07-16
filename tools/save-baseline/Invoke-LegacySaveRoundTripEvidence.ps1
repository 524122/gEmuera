[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RootPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][ValidateSet('Upstream1808', 'GEmueraSnake')][string]$ProfileId,
    [string]$IsolatedCopyRoot = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $root = (Resolve-Path -LiteralPath $RootPath).Path
    $output = [IO.Path]::GetFullPath($OutputPath)
    $rootPrefix = $root.TrimEnd('\') + '\'
    if ($output.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'M0-SAV-01 evidence report must be outside the source root.' }
    if ([string]::IsNullOrWhiteSpace($IsolatedCopyRoot)) { $IsolatedCopyRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-save-roundtrip-' + [Guid]::NewGuid().ToString('N')) }
    $isolated = [IO.Path]::GetFullPath($IsolatedCopyRoot)
    if ($isolated.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'M0-SAV-01 isolated copy must be outside the source root.' }
    $parent = Split-Path -Parent $output
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    Import-Module (Join-Path $PSScriptRoot 'LegacySaveRoundTripEvidence.psm1') -Force
    $report = New-LegacySaveRoundTripEvidence -RootPath $root -IsolatedCopyRoot $isolated -ProfileId $ProfileId
    Write-LegacySaveRoundTripEvidenceJson -Value $report -Path $output
    $report | ConvertTo-Json -Depth 30
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
