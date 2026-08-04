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
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $scriptRoot 'dialect-declaration-vocabulary.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-declaration-vocabulary.json' }

try {
    Import-Module (Join-Path $scriptRoot 'DialectDeclarationVocabulary.psm1') -Force
    $inventory = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-inventory.json') | ConvertFrom-Json
    $compatibilityPack = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $project 'docs\NewFrameworkDesign\generated\dialect-compatibility-pack.json') | ConvertFrom-Json
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json
    $result = New-DialectDeclarationVocabularyReport -Inventory $inventory -CompatibilityPack $compatibilityPack -Catalog $catalog -OutputPath $OutputPath
    Write-Output "M0_DIA_DECLARATION_VOCABULARY_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "M0_DIA_DECLARATION_VOCABULARY_SET_HASH=$($result.vocabularySetHash)"
    Write-Output "M0_DIA_DECLARATION_VOCABULARY_RESULT=$($result.result)"
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
