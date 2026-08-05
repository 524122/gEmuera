[CmdletBinding()]
param(
    [string]$ProjectRoot=(Get-Location).Path,
    [Parameter(Mandatory=$true)][string]$UpstreamProjectRoot,
    [string]$CatalogPath='',
    [string]$OutputPath=''
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$scriptRoot=Split-Path -Parent $MyInvocation.MyCommand.Path
$project=[IO.Path]::GetFullPath($ProjectRoot)
if([string]::IsNullOrWhiteSpace($CatalogPath)){$CatalogPath=Join-Path $scriptRoot 'dialect-ownership-evidence.json'}
if([string]::IsNullOrWhiteSpace($OutputPath)){$OutputPath=Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-ownership-evidence.json'}
function Read-Json([string]$Name){Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project ('docs\NewFrameworkDesign\generated\'+$Name))|ConvertFrom-Json}
try{
    Import-Module (Join-Path $scriptRoot 'DialectOwnershipEvidence.psm1') -Force
    $result=New-DialectOwnershipEvidenceReport -Inventory (Read-Json 'dialect-inventory.json') -RegistrySnapshot (Read-Json 'dialect-registry-snapshots.json') -SignatureInventory (Read-Json 'dialect-signature-inventory.json') -InstructionSignatureResolution (Read-Json 'dialect-signature-resolution.json') -FunctionSignatureResolution (Read-Json 'dialect-function-signature-resolution.json') -InstructionFlagResolution (Read-Json 'dialect-instruction-flag-resolution.json') -Catalog (Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath|ConvertFrom-Json) -UpstreamProjectRoot $UpstreamProjectRoot -OutputPath $OutputPath
    Write-Output "M0_DIA_OWNERSHIP_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_OWNERSHIP_SET_HASH=$($result.evidenceSetHash)"
    Write-Output "M0_DIA_OWNERSHIP_UPSTREAM_INSTRUCTIONS=$($result.coverage.upstreamInstructionNameMatchCount)"
    Write-Output "M0_DIA_OWNERSHIP_UPSTREAM_FUNCTIONS=$($result.coverage.upstreamExpressionNameMatchCount)"
    Write-Output "M0_DIA_OWNERSHIP_UNRESOLVED=$($result.coverage.unresolvedOwnershipCount)"
    Write-Output "M0_DIA_OWNERSHIP_RESULT=$($result.result)"
    exit 0
}catch{[Console]::Error.WriteLine($_.Exception.Message);[Console]::Error.WriteLine($_.ScriptStackTrace);exit 1}
