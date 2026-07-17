[CmdletBinding()]
param([string]$ProjectRoot=(Get-Location).Path,[string]$ClassificationPath='',[string]$ArgumentCatalogPath='',[string]$FlagCatalogPath='',[string]$OutputPath='')
Set-StrictMode -Version Latest;$ErrorActionPreference='Stop';$scriptRoot=Split-Path -Parent $MyInvocation.MyCommand.Path
if([string]::IsNullOrWhiteSpace($ClassificationPath)){$ClassificationPath=Join-Path $scriptRoot 'dialect-classification.json'}
if([string]::IsNullOrWhiteSpace($ArgumentCatalogPath)){$ArgumentCatalogPath=Join-Path $scriptRoot 'instruction-signature-resolution.json'}
if([string]::IsNullOrWhiteSpace($FlagCatalogPath)){$FlagCatalogPath=Join-Path $scriptRoot 'instruction-flag-resolution.json'}
if([string]::IsNullOrWhiteSpace($OutputPath)){$OutputPath=Join-Path ([IO.Path]::GetFullPath($ProjectRoot)) 'NewFrameworkDesign\generated\dialect-instruction-flag-resolution.json'}
try{
    Import-Module (Join-Path $scriptRoot 'DialectInventory.psm1') -Force;Import-Module (Join-Path $scriptRoot 'DialectSignatureInventory.psm1') -Force;Import-Module (Join-Path $scriptRoot 'DialectSignatureResolution.psm1') -Force;Import-Module (Join-Path $scriptRoot 'DialectInstructionFlagResolution.psm1') -Force
    $inventory=New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $ClassificationPath;$signatures=New-DialectSignatureInventory -ProjectRoot $ProjectRoot -Inventory $inventory
    $argCatalog=Get-Content $ArgumentCatalogPath -Raw -Encoding UTF8|ConvertFrom-Json;$arguments=New-DialectSignatureResolutionReport -SignatureInventory $signatures -Catalog $argCatalog
    $flagCatalog=Get-Content $FlagCatalogPath -Raw -Encoding UTF8|ConvertFrom-Json;$result=New-DialectInstructionFlagResolutionReport -SignatureInventory $signatures -InstructionSignatureResolution $arguments -Catalog $flagCatalog -OutputPath $OutputPath
    Write-Output "M0_DIA_FLAG_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))";Write-Output "M0_DIA_FLAG_CATALOG_HASH=$($result.catalogHash)";Write-Output "M0_DIA_FLAG_SET_HASH=$($result.resolutionSetHash)";Write-Output "M0_DIA_FLAG_RULES=$($result.coverage.catalogRuleCount)";Write-Output "M0_DIA_FLAG_RESULT=$($result.result)";exit 0
}catch{[Console]::Error.WriteLine($_.Exception.Message);exit 1}
