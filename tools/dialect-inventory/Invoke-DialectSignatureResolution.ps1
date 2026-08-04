[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$ClassificationPath = '',
    [string]$ResolutionCatalogPath = '',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ClassificationPath)) { $ClassificationPath = Join-Path $scriptRoot 'dialect-classification.json' }
if ([string]::IsNullOrWhiteSpace($ResolutionCatalogPath)) { $ResolutionCatalogPath = Join-Path $scriptRoot 'instruction-signature-resolution.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path ([IO.Path]::GetFullPath($ProjectRoot)) 'docs\NewFrameworkDesign\generated\dialect-signature-resolution.json'
}

try {
    Import-Module (Join-Path $scriptRoot 'DialectInventory.psm1') -Force
    Import-Module (Join-Path $scriptRoot 'DialectSignatureInventory.psm1') -Force
    Import-Module (Join-Path $scriptRoot 'DialectSignatureResolution.psm1') -Force
    $inventory = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $ClassificationPath
    $signatures = New-DialectSignatureInventory -ProjectRoot $ProjectRoot -Inventory $inventory
    $catalog = Get-Content -LiteralPath $ResolutionCatalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $result = New-DialectSignatureResolutionReport -SignatureInventory $signatures -Catalog $catalog -OutputPath $OutputPath
    Write-Output "M0_DIA_SIGNATURE_RESOLUTION_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_SIGNATURE_RESOLUTION_CATALOG_HASH=$($result.catalogHash)"
    Write-Output "M0_DIA_SIGNATURE_RESOLUTION_SET_HASH=$($result.resolutionSetHash)"
    Write-Output "M0_DIA_SIGNATURE_RESOLUTION_BY_RULE=$($result.coverage.resolvedStaticByRuleCount)"
    Write-Output "M0_DIA_SIGNATURE_RESOLUTION_UNRESOLVED=$($result.coverage.unresolvedCount)"
    Write-Output "M0_DIA_SIGNATURE_RESOLUTION_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
