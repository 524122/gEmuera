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
    $OutputPath = Join-Path ([IO.Path]::GetFullPath($ProjectRoot)) 'NewFrameworkDesign\generated\dialect-registry-snapshots.json'
}

try {
    Import-Module (Join-Path $scriptRoot 'DialectInventory.psm1') -Force
    $inventory = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $ClassificationPath
    $result = New-DialectRegistrySnapshotReport -Inventory $inventory -OutputPath $OutputPath
    Write-Output "M0_DIA_SNAPSHOT_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_SNAPSHOT_SET_HASH=$($result.snapshotSetHash)"
    Write-Output "M0_DIA_V24_HASH=$($result.profiles.v24.canonicalHash)"
    Write-Output "M0_DIA_SNAKE_HASH=$($result.profiles.snake.canonicalHash)"
    Write-Output "M0_DIA_PROJECTION_INVARIANT=$($result.testProjectionInvariant.status)"
    Write-Output "M0_DIA_CURRENT_RUNTIME_ISOLATION=$($result.currentRuntimeIsolation.status)"
    Write-Output "M0_DIA_SNAKE_ONLY_INSTRUCTIONS=$($result.diff.snakeOnlyInstructionCount)"
    Write-Output "M0_DIA_SNAKE_ONLY_FUNCTIONS=$($result.diff.snakeOnlyExpressionFunctionCount)"
    Write-Output "M0_DIA_SNAPSHOT_RESULT=$($result.result)"
    if ($result.testProjectionInvariant.status -ne 'Passed') { exit 2 }
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
