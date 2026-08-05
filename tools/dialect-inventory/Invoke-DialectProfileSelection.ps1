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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'dialect-profile-selection.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-profile-selection.json' }

try {
    Import-Module (Join-Path $scriptRoot 'DialectProfileSelection.psm1') -Force
    $plan = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-plan-preflight.json') | ConvertFrom-Json
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json
    $result = New-DialectProfileSelectionReport -PlanPreflight $plan -Catalog $catalog -ProjectRoot $project -OutputPath $OutputPath
    Write-Output "M0_DIA_PROFILE_SELECTION_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_PROFILE_SELECTION_SET_HASH=$($result.selectionSetHash)"
    Write-Output "M0_DIA_PROFILE_SELECTION_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
