[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectPolicyConsumerBoundary.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-policy-consumer-boundary.json'
$reportSchemaPath = Join-Path $toolRoot 'dialect-policy-consumer-boundary.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'dialect-policy-consumer-boundary-catalog.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-policy-consumer-boundary-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-PolicyConsumerBoundaryContract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-PolicyConsumerBoundaryThrows([scriptblock]$Action, [string]$Pattern, [string]$Failure) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) { throw "$Failure Actual: $($caught.Exception.Message)" }
}

function Read-PolicyConsumerBoundaryJson([string]$Path) {
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-PolicyConsumerBoundaryObject([object]$Value) {
    return ($Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing DIA-14 module: $modulePath"
    }

    Import-Module $modulePath -Force
    $catalog = Read-PolicyConsumerBoundaryJson $catalogPath
    $inventory = Read-PolicyConsumerBoundaryJson (Join-Path $generatedRoot 'dialect-inventory.json')
    $vocabulary = Read-PolicyConsumerBoundaryJson (Join-Path $generatedRoot 'dialect-declaration-vocabulary.json')
    $reportSchema = Read-PolicyConsumerBoundaryJson $reportSchemaPath
    $catalogSchema = Read-PolicyConsumerBoundaryJson $catalogSchemaPath

    $actual = New-DialectPolicyConsumerBoundaryReport -Inventory $inventory -DeclarationVocabulary $vocabulary -Catalog $catalog
    Assert-PolicyConsumerBoundaryContract ($actual.workPackage -ceq 'M0-DIA-14') 'Unexpected DIA-14 work package.'
    Assert-PolicyConsumerBoundaryContract ($actual.executionStatus -ceq 'InProgress' -and $actual.gateStatus -ceq 'Blocked' -and $actual.blockerCode -ceq 'EvidenceMissing') 'DIA-14 status was incorrectly advanced.'
    Assert-PolicyConsumerBoundaryContract ($actual.result -ceq 'Partial') 'DIA-14 must remain Partial.'
    Assert-PolicyConsumerBoundaryContract ($actual.boundarySetHash -match '^[0-9a-f]{64}$') 'DIA-14 boundary set hash is invalid.'
    Assert-PolicyConsumerBoundaryContract ($actual.sourceInventoryHash -ceq $inventory.canonicalHash) 'DIA-14 did not pin DIA-01 evidence.'
    Assert-PolicyConsumerBoundaryContract ($actual.sourceDeclarationVocabularyHash -ceq $vocabulary.vocabularySetHash) 'DIA-14 did not pin DIA-13 evidence.'
    Assert-PolicyConsumerBoundaryContract ($actual.currentRuntimeIsolation.status -ceq 'Failed') 'Runtime isolation was incorrectly advanced.'
    Assert-PolicyConsumerBoundaryContract ($actual.parserVmConsumption.status -ceq 'NotConsumed') 'DIA-14 was incorrectly wired into Parser/VM.'
    Assert-PolicyConsumerBoundaryContract ($actual.compatibilityPlanRuntime.status -ceq 'NotImplemented') 'DIA-14 was incorrectly presented as a runtime CompatibilityPlan.'
    Assert-PolicyConsumerBoundaryContract ($actual.policyManagerRuntime.status -ceq 'NotImplemented') 'DIA-14 was incorrectly presented as a policy manager.'
    Assert-PolicyConsumerBoundaryContract ($actual.m1Eligibility.status -ceq 'Blocked') 'DIA-14 incorrectly advanced M1 eligibility.'
    Assert-PolicyConsumerBoundaryContract ($reportSchema.properties.workPackage.const -ceq 'M0-DIA-14') 'DIA-14 report schema work package drifted.'
    Assert-PolicyConsumerBoundaryContract ($catalogSchema.properties.sourceWorkPackage.const -ceq 'M0-DIA-14') 'DIA-14 catalog schema work package drifted.'

    Assert-PolicyConsumerBoundaryContract ($actual.boundaryCounts.behaviorKeyCount -eq 10 -and $actual.boundaryCounts.decisionBoundaryCount -eq 10) 'DIA-14 behavior boundary count drifted.'
    Assert-PolicyConsumerBoundaryContract ($actual.boundaryCounts.sourceClassificationBindingCount -eq 14 -and $actual.boundaryCounts.sourceFileBindingCount -eq 16) 'DIA-14 source binding count drifted.'
    Assert-PolicyConsumerBoundaryContract ($actual.currentDeclarationVocabularyExposure.status -ceq 'NoRuntimeCapabilitiesDeclared') 'DIA-14 accepted runtime capability exposure.'
    Assert-PolicyConsumerBoundaryContract ((@($actual.currentDeclarationVocabularyExposure.allowedCapabilityIds).Count) -eq 0) 'DIA-14 vocabulary exposure must be empty.'

    $behaviorKeys = @($actual.boundaries | ForEach-Object behaviorKeyId)
    Assert-PolicyConsumerBoundaryContract (($behaviorKeys -join ',') -ceq 'call.extra-arguments.v1,call.private-argument-shape.v1,display.history-capacity.v1,display.refresh-timing.v1,function.snake-fallen-state.v1,instruction.scoped-variable-registration.v1,parser.startup-fault.v1,parser.user-variable-resolution.v1,parser.warning-routing.v1,resource.lazy-index.v1') 'DIA-14 behavior key order drifted.'
    $extraArgs = @($actual.boundaries | Where-Object behaviorKeyId -ceq 'call.extra-arguments.v1')
    $scoped = @($actual.boundaries | Where-Object behaviorKeyId -ceq 'instruction.scoped-variable-registration.v1')
    $userVariable = @($actual.boundaries | Where-Object behaviorKeyId -ceq 'parser.user-variable-resolution.v1')
    Assert-PolicyConsumerBoundaryContract ($extraArgs.Count -eq 1 -and $extraArgs[0].consumerContractId -ceq 'policy.call.extra-arguments.v1' -and $extraArgs[0].decisionOwner -ceq 'IExtraArgumentPolicy') 'Extra-argument consumer boundary drifted.'
    Assert-PolicyConsumerBoundaryContract ($scoped.Count -eq 1 -and $scoped[0].consumerContractId -ceq 'catalog.instruction.scoped-variable-registration.v1' -and (@($scoped[0].sourceBindings | Where-Object role -ceq 'ConfigurationInput').Count -eq 1)) 'Scoped-variable input/consumer split drifted.'
    Assert-PolicyConsumerBoundaryContract ($userVariable.Count -eq 1 -and $userVariable[0].decisionOwner -ceq 'IUserVariableResolutionPolicy' -and (@($userVariable[0].sourceFilePaths).Count -eq 2)) 'User-variable single decision boundary drifted.'
    Assert-PolicyConsumerBoundaryContract (@($actual.boundaries | Where-Object { $_.boundaryDraftStatus -cne 'BoundaryDraftOnly' -or $_.runtimeStatus -cne 'NotImplemented' }).Count -eq 0) 'DIA-14 incorrectly advanced a boundary implementation.'

    $snapshot = @($actual.boundaries | ForEach-Object { $_.behaviorKeyId + ':' + $_.consumerContractId + ':' + $_.decisionOwner })
    $inventory.branchHits[0].intendedOwner = 'MUTATED_AFTER_BOUNDARY_REPORT'
    $vocabulary.declarations[0].id = 'MUTATED_AFTER_BOUNDARY_REPORT'
    Assert-PolicyConsumerBoundaryContract ((@($actual.boundaries | ForEach-Object { $_.behaviorKeyId + ':' + $_.consumerContractId + ':' + $_.decisionOwner }) -join '|') -ceq ($snapshot -join '|')) 'DIA-14 retained a mutable reference to source evidence.'
    $inventory = Read-PolicyConsumerBoundaryJson (Join-Path $generatedRoot 'dialect-inventory.json')
    $vocabulary = Read-PolicyConsumerBoundaryJson (Join-Path $generatedRoot 'dialect-declaration-vocabulary.json')

    $reorderedCatalog = Copy-PolicyConsumerBoundaryObject $catalog
    [array]::Reverse($reorderedCatalog.boundaries)
    foreach ($boundary in @($reorderedCatalog.boundaries)) { [array]::Reverse($boundary.sourceBindings) }
    $reordered = New-DialectPolicyConsumerBoundaryReport $inventory $vocabulary $reorderedCatalog
    Assert-PolicyConsumerBoundaryContract ($reordered.boundarySetHash -ceq $actual.boundarySetHash) 'Catalog enumeration order changed the DIA-14 boundary hash.'

    $staleInventoryCatalog = Copy-PolicyConsumerBoundaryObject $catalog
    $staleInventoryCatalog.sourceInventoryHash = ('0' * 64)
    Assert-PolicyConsumerBoundaryThrows { New-DialectPolicyConsumerBoundaryReport $inventory $vocabulary $staleInventoryCatalog } 'source inventory hash drifted' 'DIA-14 accepted stale DIA-01 evidence.'

    $unknownBehaviorCatalog = Copy-PolicyConsumerBoundaryObject $catalog
    $unknownBehaviorCatalog.boundaries[0].behaviorKeyId = 'unknown.future-policy.v1'
    Assert-PolicyConsumerBoundaryThrows { New-DialectPolicyConsumerBoundaryReport $inventory $vocabulary $unknownBehaviorCatalog } 'references unknown BehaviorKey' 'DIA-14 accepted a behavior key absent from DIA-13.'

    $duplicateConsumerCatalog = Copy-PolicyConsumerBoundaryObject $catalog
    $duplicateConsumerCatalog.boundaries[1].consumerContractId = $duplicateConsumerCatalog.boundaries[0].consumerContractId
    Assert-PolicyConsumerBoundaryThrows { New-DialectPolicyConsumerBoundaryReport $inventory $vocabulary $duplicateConsumerCatalog } 'Duplicate DIA-14 consumer contract id' 'DIA-14 accepted duplicate decision boundaries.'

    $wrongRoleCatalog = Copy-PolicyConsumerBoundaryObject $catalog
    $scopedCatalog = @($wrongRoleCatalog.boundaries | Where-Object behaviorKeyId -ceq 'instruction.scoped-variable-registration.v1')[0]
    @($scopedCatalog.sourceBindings | Where-Object classificationId -ceq 'scoped-variable-config-schema')[0].role = 'DecisionConsumer'
    Assert-PolicyConsumerBoundaryThrows { New-DialectPolicyConsumerBoundaryReport $inventory $vocabulary $wrongRoleCatalog } 'DecisionConsumer owner drifted' 'DIA-14 accepted a configuration input as a decision consumer.'

    $exposedVocabulary = Copy-PolicyConsumerBoundaryObject $vocabulary
    $exposedVocabulary.currentCompatibilityPackExposure.allowedCapabilityIds = @('compatibility.plan.selection.v1')
    Assert-PolicyConsumerBoundaryThrows { New-DialectPolicyConsumerBoundaryReport $inventory $exposedVocabulary $catalog } 'must retain no runtime capability exposure' 'DIA-14 accepted DIA-13 runtime capability exposure.'

    $outputPath = Join-Path $testRoot 'dialect-policy-consumer-boundary.json'
    $written = New-DialectPolicyConsumerBoundaryReport -Inventory $inventory -DeclarationVocabulary $vocabulary -Catalog $catalog -OutputPath $outputPath
    Assert-PolicyConsumerBoundaryContract (Test-Path -LiteralPath $outputPath -PathType Leaf) 'DIA-14 report was not written.'
    Assert-PolicyConsumerBoundaryContract ((Read-PolicyConsumerBoundaryJson $outputPath).boundarySetHash -ceq $written.boundarySetHash) 'Written DIA-14 report hash drifted.'

    Write-Output 'M0 dialect policy consumer boundary contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\\') + '\\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot).TrimEnd('\\') + '\\'
    if ($resolvedTest.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        $resolvedTest.Contains('gemuera-m0-dia-policy-consumer-boundary-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
