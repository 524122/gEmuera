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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'dialect-compatibility-pack.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-compatibility-pack.json' }

try {
    Import-Module (Join-Path $scriptRoot 'DialectCompatibilityPack.psm1') -Force
    $planPreflight = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-plan-preflight.json') | ConvertFrom-Json
    $profileSelection = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-profile-selection.json') | ConvertFrom-Json
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json
    $result = New-DialectCompatibilityPackReport -PlanPreflight $planPreflight -ProfileSelection $profileSelection -Catalog $catalog -OutputPath $OutputPath
    Write-Output "M0_DIA_COMPATIBILITY_PACK_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_COMPATIBILITY_PACK_SET_HASH=$($result.compatibilityPackSetHash)"
    Write-Output "M0_DIA_COMPATIBILITY_PACK_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
