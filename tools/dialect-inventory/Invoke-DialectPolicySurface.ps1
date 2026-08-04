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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'dialect-policy-surface.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-policy-surface.json' }

try {
    Import-Module (Join-Path $scriptRoot 'DialectPolicySurface.psm1') -Force
    $boundary = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-policy-consumer-boundary.json') | ConvertFrom-Json
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json
    $result = New-DialectPolicySurfaceReport -PolicyConsumerBoundary $boundary -Catalog $catalog -OutputPath $OutputPath
    Write-Output "M0_DIA_POLICY_SURFACE_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_POLICY_SURFACE_SET_HASH=$($result.policySurfaceSetHash)"
    Write-Output "M0_DIA_POLICY_SURFACE_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
