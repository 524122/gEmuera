[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RootPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $root = (Resolve-Path -LiteralPath $RootPath).Path
    $output = [IO.Path]::GetFullPath($OutputPath)
    $rootPrefix = $root.TrimEnd('\') + '\'
    if ($output.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'M0-SAV-01 fixture audit refuses to write inside the source root.'
    }
    $parent = Split-Path -Parent $output
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) { [IO.Directory]::CreateDirectory($parent) | Out-Null }
    Import-Module (Join-Path $PSScriptRoot 'LegacySaveFixtureAudit.psm1') -Force
    $report = New-LegacySaveFixtureAuditReport -RootPath $root
    Write-LegacySaveFixtureAuditJson -Value $report -Path $output
    $report | ConvertTo-Json -Depth 30
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
