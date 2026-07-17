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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'dialect-policy-consumer-boundary.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'NewFrameworkDesign\generated\dialect-policy-consumer-boundary.json' }

try {
    Import-Module (Join-Path $scriptRoot 'DialectPolicyConsumerBoundary.psm1') -Force
    $inventory = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'NewFrameworkDesign\generated\dialect-inventory.json') | ConvertFrom-Json
    $vocabulary = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'NewFrameworkDesign\generated\dialect-declaration-vocabulary.json') | ConvertFrom-Json
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json
    $result = New-DialectPolicyConsumerBoundaryReport -Inventory $inventory -DeclarationVocabulary $vocabulary -Catalog $catalog -OutputPath $OutputPath
    Write-Output "M0_DIA_POLICY_CONSUMER_BOUNDARY_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_POLICY_CONSUMER_BOUNDARY_SET_HASH=$($result.boundarySetHash)"
    Write-Output "M0_DIA_POLICY_CONSUMER_BOUNDARY_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
