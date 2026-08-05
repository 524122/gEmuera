[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$CatalogPath = '',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = [IO.Path]::GetFullPath($ProjectRoot)
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'legacy-save-baseline.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'docs\NewFrameworkDesign\generated\legacy-save-baseline.json' }

try {
    Import-Module (Join-Path $scriptRoot 'LegacySaveBaseline.psm1') -Force
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json
    $result = New-LegacySaveBaselineReport -ProjectRoot $project -Catalog $catalog -OutputPath $OutputPath
    Write-Output "M0_SAV_BASELINE_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_SAV_BASELINE_SET_HASH=$($result.saveBaselineSetHash)"
    Write-Output "M0_SAV_BASELINE_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
