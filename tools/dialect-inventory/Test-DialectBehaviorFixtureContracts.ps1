[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-17-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-DialectBehaviorFixtureContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Copy-DialectBehaviorFixtureContractObject {
    param([Parameter(Mandatory = $true)][object]$Value)
    return ($Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
}

function Assert-DialectBehaviorFixtureContractThrows {
    param([Parameter(Mandatory = $true)][scriptblock]$Action, [Parameter(Mandatory = $true)][string]$Message)
    $thrown = $false
    try { & $Action } catch { $thrown = $true }
    Assert-DialectBehaviorFixtureContract $thrown $Message
}

try {
    $modulePath = Join-Path $ProjectRoot 'tools\dialect-inventory\DialectBehaviorFixtureContracts.psm1'
    $catalogPath = Join-Path $ProjectRoot 'tools\dialect-inventory\dialect-behavior-fixture-contract-catalog.json'
    $catalogSchemaPath = Join-Path $ProjectRoot 'tools\dialect-inventory\dialect-behavior-fixture-contract-catalog.schema.json'
    $reportSchemaPath = Join-Path $ProjectRoot 'tools\dialect-inventory\dialect-behavior-fixture-contract.schema.json'
    $vocabularyPath = Join-Path $ProjectRoot 'docs\NewFrameworkDesign\generated\dialect-declaration-vocabulary.json'
    $surfacePath = Join-Path $ProjectRoot 'docs\NewFrameworkDesign\generated\dialect-policy-surface.json'
    foreach ($path in @($modulePath, $catalogPath, $catalogSchemaPath, $reportSchemaPath, $vocabularyPath, $surfacePath)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "M0-DIA-17 required input is missing: $path" }
    }

    Import-Module $modulePath -Force
    $vocabulary = Get-Content -LiteralPath $vocabularyPath -Raw -Encoding utf8 | ConvertFrom-Json
    $surface = Get-Content -LiteralPath $surfacePath -Raw -Encoding utf8 | ConvertFrom-Json
    $catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding utf8 | ConvertFrom-Json
    $outputPath = Join-Path $testRoot 'actual.json'
    $actual = New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $catalog -OutputPath $outputPath

    Assert-DialectBehaviorFixtureContract ($actual.workPackage -ceq 'M0-DIA-17') 'Unexpected M0-DIA-17 work package.'
    Assert-DialectBehaviorFixtureContract ($actual.executionStatus -ceq 'InProgress' -and $actual.gateStatus -ceq 'Blocked' -and $actual.blockerCode -ceq 'EvidenceMissing' -and $actual.result -ceq 'Partial') 'M0-DIA-17 status was incorrectly advanced.'
    Assert-DialectBehaviorFixtureContract ($actual.contractCounts.contractCount -eq 10 -and $actual.contractCounts.plannedFixtureContractCount -eq 10 -and $actual.contractCounts.behaviorEvidenceCoveredCount -eq 0) 'Unexpected M0-DIA-17 coverage counts.'
    Assert-DialectBehaviorFixtureContract (@($actual.contracts | Where-Object fixtureContractStatus -cne 'Planned').Count -eq 0) 'Fixture plan status was incorrectly upgraded.'
    Assert-DialectBehaviorFixtureContract (@($actual.contracts | Where-Object behaviorEvidenceStatus -cne 'Uncovered').Count -eq 0) 'Behavior evidence was incorrectly upgraded.'
    Assert-DialectBehaviorFixtureContract (@($actual.contracts | Where-Object interfaceImplementationStatus -cne 'BlockedByFixture').Count -eq 0) 'Runtime interface implementation was incorrectly authorized.'
    Assert-DialectBehaviorFixtureContract ((Test-Path -LiteralPath $outputPath -PathType Leaf)) 'M0-DIA-17 report was not written.'
    Assert-DialectBehaviorFixtureContract ($actual.behaviorFixtureContractSetHash -match '^[0-9a-f]{64}$') 'M0-DIA-17 canonical hash is invalid.'

    $repeat = New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $catalog
    Assert-DialectBehaviorFixtureContract ($repeat.behaviorFixtureContractSetHash -ceq $actual.behaviorFixtureContractSetHash) 'M0-DIA-17 hash changed between identical builds.'

    $reorderedCatalog = Copy-DialectBehaviorFixtureContractObject $catalog
    $reorderedContracts = @($reorderedCatalog.contracts)
    [array]::Reverse($reorderedContracts)
    $reorderedCatalog.contracts = $reorderedContracts
    $reordered = New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $reorderedCatalog
    Assert-DialectBehaviorFixtureContract ($reordered.behaviorFixtureContractSetHash -ceq $actual.behaviorFixtureContractSetHash) 'M0-DIA-17 hash depends on catalog enumeration order.'

    $duplicateCatalog = Copy-DialectBehaviorFixtureContractObject $catalog
    $duplicateCatalog.contracts = @($duplicateCatalog.contracts) + @(Copy-DialectBehaviorFixtureContractObject $duplicateCatalog.contracts[0])
    Assert-DialectBehaviorFixtureContractThrows { New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $duplicateCatalog } 'Duplicate BehaviorKey fixture contract was accepted.'

    $wrongFixtureCatalog = Copy-DialectBehaviorFixtureContractObject $catalog
    ($wrongFixtureCatalog.contracts | Where-Object behaviorKeyId -ceq 'call.extra-arguments.v1')[0].fixtureId = 'DIA-OTHER-001'
    Assert-DialectBehaviorFixtureContractThrows { New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $wrongFixtureCatalog } 'Fixture ID drift from DIA-13/DIA-15 was accepted.'

    $staleCatalog = Copy-DialectBehaviorFixtureContractObject $catalog
    $staleCatalog.sourcePolicySurfaceHash = ('0' * 64)
    Assert-DialectBehaviorFixtureContractThrows { New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $staleCatalog } 'Stale policy surface hash was accepted.'

    $advancedCatalog = Copy-DialectBehaviorFixtureContractObject $catalog
    $advancedCatalog.contracts[0].fixtureContractStatus = 'Captured'
    Assert-DialectBehaviorFixtureContractThrows { New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $advancedCatalog } 'Catalog prematurely upgraded behavior fixture status.'

    $payloadCatalog = Copy-DialectBehaviorFixtureContractObject $catalog
    $payloadCatalog.contracts[0] | Add-Member -NotePropertyName policyValue -NotePropertyValue 'forbidden'
    Assert-DialectBehaviorFixtureContractThrows { New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $payloadCatalog } 'Catalog accepted an undeclared policy value payload.'

    $advancedSurface = Copy-DialectBehaviorFixtureContractObject $surface
    ($advancedSurface.contracts | Where-Object behaviorKeyId -ceq 'call.extra-arguments.v1')[0].policyValueStatus = 'Resolved'
    Assert-DialectBehaviorFixtureContractThrows { New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $advancedSurface -Catalog $catalog } 'Advanced M0-DIA-15 policy evidence was accepted.'

    $mutatedReport = New-DialectBehaviorFixtureContractReport -DeclarationVocabulary $vocabulary -PolicySurface $surface -Catalog $catalog
    $mutatedReportContract = @($mutatedReport.contracts | Where-Object behaviorKeyId -ceq 'call.extra-arguments.v1')[0]
    $sourceSurfaceContract = @($surface.contracts | Where-Object behaviorKeyId -ceq 'call.extra-arguments.v1')[0]
    $mutatedReportContract.targetModuleIds[0] = 'tampered.module'
    Assert-DialectBehaviorFixtureContract ($sourceSurfaceContract.targetModuleIds[0] -cne 'tampered.module') 'M0-DIA-17 report retained a mutable M0-DIA-15 source array.'

    $catalogSchema = Get-Content -LiteralPath $catalogSchemaPath -Raw -Encoding utf8 | ConvertFrom-Json
    $reportSchema = Get-Content -LiteralPath $reportSchemaPath -Raw -Encoding utf8 | ConvertFrom-Json
    Assert-DialectBehaviorFixtureContract ($catalogSchema.properties.sourceWorkPackage.const -ceq 'M0-DIA-17' -and $catalogSchema.properties.expectedContractCount.const -eq 10) 'M0-DIA-17 catalog schema drifted.'
    Assert-DialectBehaviorFixtureContract ($reportSchema.properties.workPackage.const -ceq 'M0-DIA-17' -and $reportSchema.properties.result.const -ceq 'Partial') 'M0-DIA-17 report schema drifted.'

    Write-Output 'M0 dialect behavior fixture contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
    if ($resolvedTest.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and $resolvedTest.Contains('gemuera-m0-dia-17-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
