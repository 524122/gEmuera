[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$ClassificationPath = '',
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($ClassificationPath)) {
    $ClassificationPath = Join-Path $scriptRoot 'dialect-classification.json'
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path ([IO.Path]::GetFullPath($ProjectRoot)) 'docs\NewFrameworkDesign\generated\dialect-inventory.json'
}

try {
    Import-Module (Join-Path $scriptRoot 'DialectInventory.psm1') -Force
    $result = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $ClassificationPath -OutputPath $OutputPath
    Write-Output "M0_DIA_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_CANONICAL_HASH=$($result.canonicalHash)"
    Write-Output "M0_DIA_BRANCH_HITS=$($result.branchHitCount)"
    Write-Output "M0_DIA_INSTRUCTIONS=$($result.instructionRegistrationCount)"
    Write-Output "M0_DIA_EXPRESSIONS=$($result.expressionRegistrationCount)"
    Write-Output "M0_DIA_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
