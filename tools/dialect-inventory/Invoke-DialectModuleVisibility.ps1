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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'dialect-module-visibility.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'NewFrameworkDesign\generated\dialect-module-visibility.json' }

function Read-ModuleVisibilityJson {
    param([string]$Name)
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project ('NewFrameworkDesign\generated\' + $Name)) | ConvertFrom-Json)
}

try {
    Import-Module (Join-Path $scriptRoot 'DialectModuleVisibility.psm1') -Force
    $result = New-DialectModuleVisibilityReport -RegistrySnapshot (Read-ModuleVisibilityJson 'dialect-registry-snapshots.json') -OwnershipEvidence (Read-ModuleVisibilityJson 'dialect-ownership-evidence.json') -NameLookupContract (Read-ModuleVisibilityJson 'dialect-name-lookup-contract.json') -Catalog (Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json) -OutputPath $OutputPath
    Write-Output "M0_DIA_MODULE_VISIBILITY_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_MODULE_VISIBILITY_SET_HASH=$($result.visibilitySetHash)"
    Write-Output "M0_DIA_MODULE_VISIBILITY_V24_VISIBLE=$($result.coverage.v24VisibleCandidateCount)"
    Write-Output "M0_DIA_MODULE_VISIBILITY_SNAKE_ONLY=$($result.coverage.snakeOnlyCandidateCount)"
    Write-Output "M0_DIA_MODULE_VISIBILITY_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
