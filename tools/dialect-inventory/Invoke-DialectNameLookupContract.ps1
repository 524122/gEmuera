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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'dialect-name-lookup-contract.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-name-lookup-contract.json' }

function Read-GeneratedLookupJson([string]$Name) {
    $path = Join-Path $project ('docs\NewFrameworkDesign\generated\' + $Name)
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json)
}

try {
    Import-Module (Join-Path $scriptRoot 'DialectNameLookupContract.psm1') -Force
    $result = New-DialectNameLookupContractReport `
        -Inventory (Read-GeneratedLookupJson 'dialect-inventory.json') `
        -OwnershipEvidence (Read-GeneratedLookupJson 'dialect-ownership-evidence.json') `
        -Catalog (Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json) `
        -ProjectRoot $project `
        -OutputPath $OutputPath
    Write-Output "M0_DIA_LOOKUP_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_LOOKUP_CONTRACT_SET_HASH=$($result.contractSetHash)"
    Write-Output "M0_DIA_LOOKUP_INSTRUCTION_REGISTRATIONS=$($result.instructionRegistrationCount)"
    Write-Output "M0_DIA_LOOKUP_EXPRESSION_REGISTRATIONS=$($result.expressionRegistrationCount)"
    Write-Output "M0_DIA_LOOKUP_COLLISIONS=$($result.crossRegistryCollisionCount)"
    Write-Output "M0_DIA_LOOKUP_INSTRUCTION_SURFACE=$($result.instructionLookupSurfaceCount)"
    Write-Output "M0_DIA_LOOKUP_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
