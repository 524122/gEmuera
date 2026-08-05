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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'legacy-session-state-classification.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'docs\NewFrameworkDesign\generated\legacy-session-state-inventory.json' }

try {
    Import-Module (Join-Path $scriptRoot 'LegacySessionStateInventory.psm1') -Force
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json
    $result = New-LegacySessionRootInventory -ProjectRoot $project -Catalog $catalog -OutputPath $OutputPath
    Write-Output "M0_SES_INVENTORY_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_SES_INVENTORY_HASH=$($result.inventorySetHash)"
    Write-Output "M0_SES_GLOBAL_STATIC_COUNT=$($result.coverage.globalStaticStateCount)"
    Write-Output "M0_SES_PROGRAM_COUNT=$($result.coverage.programStateCount)"
    Write-Output "M0_SES_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
