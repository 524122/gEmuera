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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'dialect-module-composition.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-module-composition.json' }

try {
    Import-Module (Join-Path $scriptRoot 'DialectModuleComposition.psm1') -Force
    $compatibilityPack = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-compatibility-pack.json') | ConvertFrom-Json
    $policySurface = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-policy-surface.json') | ConvertFrom-Json
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json
    $result = New-DialectModuleCompositionReport -CompatibilityPack $compatibilityPack -PolicySurface $policySurface -Catalog $catalog -OutputPath $OutputPath
    Write-Output "M0_DIA_MODULE_COMPOSITION_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_MODULE_COMPOSITION_SET_HASH=$($result.moduleCompositionSetHash)"
    Write-Output "M0_DIA_MODULE_COMPOSITION_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
