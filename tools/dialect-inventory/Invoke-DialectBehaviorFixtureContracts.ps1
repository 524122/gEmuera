[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$OutputPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    $resolvedRoot = [IO.Path]::GetFullPath($ProjectRoot)
    $modulePath = Join-Path $resolvedRoot 'tools\dialect-inventory\DialectBehaviorFixtureContracts.psm1'
    $catalogPath = Join-Path $resolvedRoot 'tools\dialect-inventory\dialect-behavior-fixture-contract-catalog.json'
    $vocabularyPath = Join-Path $resolvedRoot 'NewFrameworkDesign\generated\dialect-declaration-vocabulary.json'
    $surfacePath = Join-Path $resolvedRoot 'NewFrameworkDesign\generated\dialect-policy-surface.json'
    if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $resolvedRoot 'NewFrameworkDesign\generated\dialect-behavior-fixture-contracts.json' }
    foreach ($path in @($modulePath, $catalogPath, $vocabularyPath, $surfacePath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required M0-DIA-17 input is missing: $path" }
    }
    Import-Module $modulePath -Force
    $vocabulary = Get-Content -LiteralPath $vocabularyPath -Raw -Encoding utf8 | ConvertFrom-Json
    $surface = Get-Content -LiteralPath $surfacePath -Raw -Encoding utf8 | ConvertFrom-Json
    $catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding utf8 | ConvertFrom-Json
    $report = New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $catalog -OutputPath $OutputPath
    Write-Output ("M0-DIA-17 behavior fixture contracts generated: contracts={0}; hash={1}; output={2}" -f $report.contractCounts.contractCount, $report.behaviorFixtureContractSetHash, [IO.Path]::GetFullPath($OutputPath))
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
