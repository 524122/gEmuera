[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$ClassificationPath = '',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ClassificationPath)) { $ClassificationPath = Join-Path $scriptRoot 'dialect-classification.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path ([IO.Path]::GetFullPath($ProjectRoot)) 'docs\NewFrameworkDesign\generated\dialect-signature-inventory.json'
}
try {
    Import-Module (Join-Path $scriptRoot 'DialectInventory.psm1') -Force
    Import-Module (Join-Path $scriptRoot 'DialectSignatureInventory.psm1') -Force
    $inventory = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $ClassificationPath
    $result = New-DialectSignatureInventory -ProjectRoot $ProjectRoot -Inventory $inventory -OutputPath $OutputPath
    Write-Output "M0_DIA_SIGNATURE_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_SIGNATURE_SET_HASH=$($result.descriptorSetHash)"
    Write-Output "M0_DIA_SIGNATURE_INSTRUCTIONS=$($result.instructionCount)"
    Write-Output "M0_DIA_SIGNATURE_FUNCTIONS=$($result.expressionFunctionCount)"
    Write-Output "M0_DIA_SIGNATURE_INSTRUCTION_ARGS_RESOLVED=$($result.coverage.instructionArgumentResolvedCount)"
    Write-Output "M0_DIA_SIGNATURE_FUNCTION_COMPLETE=$($result.coverage.expressionCompleteStaticCount)"
    Write-Output "M0_DIA_SIGNATURE_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
