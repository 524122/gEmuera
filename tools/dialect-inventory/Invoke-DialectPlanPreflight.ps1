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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'dialect-plan-preflight.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'NewFrameworkDesign\generated\dialect-plan-preflight.json' }

function Read-DialectPlanPreflightGeneratedJson {
    param([Parameter(Mandatory = $true)][string]$Name)

    $path = Join-Path $project ('NewFrameworkDesign\generated\' + $Name)
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json)
}

try {
    Import-Module (Join-Path $scriptRoot 'DialectPlanPreflight.psm1') -Force
    $result = New-DialectPlanPreflightReport `
        -RegistrySnapshot (Read-DialectPlanPreflightGeneratedJson 'dialect-registry-snapshots.json') `
        -SessionInventory (Read-DialectPlanPreflightGeneratedJson 'legacy-session-state-inventory.json') `
        -Catalog (Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json) `
        -OutputPath $OutputPath
    Write-Output "M0_DIA_PLAN_PREFLIGHT_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_PLAN_PREFLIGHT_SET_HASH=$($result.preflightSetHash)"
    Write-Output "M0_DIA_PLAN_PREFLIGHT_PROFILES=$(@($result.profiles).Count)"
    Write-Output "M0_DIA_PLAN_PREFLIGHT_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
