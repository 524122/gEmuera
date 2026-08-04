[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'docs\NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectDeclarationVocabulary.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-declaration-vocabulary.json'
$reportSchemaPath = Join-Path $toolRoot 'dialect-declaration-vocabulary.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'dialect-declaration-vocabulary-catalog.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-declaration-vocabulary-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-DeclarationVocabularyContract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-DeclarationVocabularyThrows([scriptblock]$Action, [string]$Pattern, [string]$Failure) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) { throw "$Failure Actual: $($caught.Exception.Message)" }
}

function Read-DeclarationVocabularyJson([string]$Path) {
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-DeclarationVocabularyObject([object]$Value) {
    return ($Value | ConvertTo-Json -Depth 80 | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing DIA-13 module: $modulePath"
    }

    Import-Module $modulePath -Force
    $catalog = Read-DeclarationVocabularyJson $catalogPath
    $inventory = Read-DeclarationVocabularyJson (Join-Path $generatedRoot 'dialect-inventory.json')
    $compatibilityPack = Read-DeclarationVocabularyJson (Join-Path $generatedRoot 'dialect-compatibility-pack.json')
    $reportSchema = Read-DeclarationVocabularyJson $reportSchemaPath
    $catalogSchema = Read-DeclarationVocabularyJson $catalogSchemaPath

    $actual = New-DialectDeclarationVocabularyReport -Inventory $inventory -CompatibilityPack $compatibilityPack -Catalog $catalog
    Assert-DeclarationVocabularyContract ($actual.workPackage -ceq 'M0-DIA-13') 'Unexpected DIA-13 work package.'
    Assert-DeclarationVocabularyContract ($actual.executionStatus -ceq 'InProgress' -and $actual.gateStatus -ceq 'Blocked' -and $actual.blockerCode -ceq 'EvidenceMissing') 'DIA-13 status was incorrectly advanced.'
    Assert-DeclarationVocabularyContract ($actual.result -ceq 'Partial') 'DIA-13 must remain Partial.'
    Assert-DeclarationVocabularyContract ($actual.vocabularySetHash -match '^[0-9a-f]{64}$') 'DIA-13 vocabulary set hash is invalid.'
    Assert-DeclarationVocabularyContract ($actual.sourceInventoryHash -ceq $inventory.canonicalHash) 'DIA-13 did not pin DIA-01 evidence.'
    Assert-DeclarationVocabularyContract ($actual.sourceCompatibilityPackHash -ceq $compatibilityPack.compatibilityPackSetHash) 'DIA-13 did not pin DIA-12 evidence.'
    Assert-DeclarationVocabularyContract ($actual.currentRuntimeIsolation.status -ceq 'Failed') 'Runtime isolation was incorrectly advanced.'
    Assert-DeclarationVocabularyContract ($actual.parserVmConsumption.status -ceq 'NotConsumed') 'DIA-13 was incorrectly wired into Parser/VM.'
    Assert-DeclarationVocabularyContract ($actual.compatibilityPlanRuntime.status -ceq 'NotImplemented') 'DIA-13 was incorrectly presented as a runtime CompatibilityPlan.'
    Assert-DeclarationVocabularyContract ($actual.resolverRuntime.status -ceq 'NotImplemented') 'DIA-13 was incorrectly presented as a runtime resolver.'
    Assert-DeclarationVocabularyContract ($actual.m1Eligibility.status -ceq 'Blocked') 'DIA-13 incorrectly advanced M1 eligibility.'
    Assert-DeclarationVocabularyContract ($reportSchema.properties.workPackage.const -ceq 'M0-DIA-13') 'DIA-13 report schema work package drifted.'
    Assert-DeclarationVocabularyContract ($catalogSchema.properties.sourceWorkPackage.const -ceq 'M0-DIA-13') 'DIA-13 catalog schema work package drifted.'

    Assert-DeclarationVocabularyContract ($actual.declarationCounts.behaviorKeyCount -eq 10) 'Unexpected static BehaviorKey count.'
    Assert-DeclarationVocabularyContract ($actual.declarationCounts.capabilityIdCount -eq 4) 'Unexpected static CapabilityId count.'
    Assert-DeclarationVocabularyContract ($actual.declarationCounts.sourceClassificationCount -eq 30 -and $actual.declarationCounts.branchHitCount -eq 97) 'DIA-13 source classification coverage drifted.'
    Assert-DeclarationVocabularyContract ($actual.currentCompatibilityPackExposure.status -ceq 'NoRuntimeCapabilitiesDeclared') 'DIA-13 must keep candidate vocabulary out of current CompatibilityPacks.'
    Assert-DeclarationVocabularyContract ((@($actual.currentCompatibilityPackExposure.allowedCapabilityIds).Count) -eq 0) 'Current CompatibilityPack unexpectedly exposes a runtime capability.'

    $behaviorIds = @($actual.declarations | Where-Object kind -ceq 'BehaviorKey' | ForEach-Object id)
    $capabilityIds = @($actual.declarations | Where-Object kind -ceq 'CapabilityId' | ForEach-Object id)
    Assert-DeclarationVocabularyContract (($behaviorIds -join ',') -ceq 'call.extra-arguments.v1,call.private-argument-shape.v1,display.history-capacity.v1,display.refresh-timing.v1,function.snake-fallen-state.v1,instruction.scoped-variable-registration.v1,parser.startup-fault.v1,parser.user-variable-resolution.v1,parser.warning-routing.v1,resource.lazy-index.v1') 'BehaviorKey vocabulary drifted.'
    Assert-DeclarationVocabularyContract (($capabilityIds -join ',') -ceq 'compatibility.plan.selection.v1,diagnostics.profile-identity.v1,dialect.module-id.v1,instruction.registry.v1') 'CapabilityId vocabulary drifted.'
    $extraArgs = @($actual.declarations | Where-Object { $_.kind -ceq 'BehaviorKey' -and $_.id -ceq 'call.extra-arguments.v1' })
    $userVariables = @($actual.declarations | Where-Object { $_.kind -ceq 'BehaviorKey' -and $_.id -ceq 'parser.user-variable-resolution.v1' })
    $selectionCapability = @($actual.declarations | Where-Object { $_.kind -ceq 'CapabilityId' -and $_.id -ceq 'compatibility.plan.selection.v1' })
    Assert-DeclarationVocabularyContract ($extraArgs.Count -eq 1 -and $extraArgs[0].branchHitCount -eq 1 -and (@($extraArgs[0].sourceClassificationIds) -join ',') -ceq 'call-extra-arguments-policy') 'Extra-argument policy provenance drifted.'
    Assert-DeclarationVocabularyContract ($userVariables.Count -eq 1 -and $userVariables[0].branchHitCount -eq 4 -and (@($userVariables[0].sourceClassificationIds) -join ',') -ceq 'expression-user-variable-policy,header-user-variable-policy') 'User-variable policy provenance drifted.'
    Assert-DeclarationVocabularyContract ($selectionCapability.Count -eq 1 -and $selectionCapability[0].branchHitCount -eq 35 -and (@($selectionCapability[0].targetModuleIds) -join ',') -ceq 'godot.launcher,session.selection') 'Selection capability provenance drifted.'
    Assert-DeclarationVocabularyContract (@($actual.declarations | Where-Object { $_.currentPackEligibility -cne 'NotEligible' }).Count -eq 0) 'DIA-13 incorrectly made a declaration pack-eligible.'

    $snapshot = @($actual.declarations | ForEach-Object { $_.kind + ':' + $_.id + ':' + $_.branchHitCount })
    $inventory.branchHits[0].classificationId = 'MUTATED_AFTER_VOCABULARY_REPORT'
    $compatibilityPack.declarationPolicy.allowedCapabilityIds = @('MUTATED_AFTER_VOCABULARY_REPORT')
    Assert-DeclarationVocabularyContract ((@($actual.declarations | ForEach-Object { $_.kind + ':' + $_.id + ':' + $_.branchHitCount }) -join '|') -ceq ($snapshot -join '|')) 'DIA-13 retained a mutable reference to source evidence.'
    $inventory = Read-DeclarationVocabularyJson (Join-Path $generatedRoot 'dialect-inventory.json')
    $compatibilityPack = Read-DeclarationVocabularyJson (Join-Path $generatedRoot 'dialect-compatibility-pack.json')

    $reorderedCatalog = Copy-DeclarationVocabularyObject $catalog
    [array]::Reverse($reorderedCatalog.declarations)
    foreach ($declaration in @($reorderedCatalog.declarations)) {
        [array]::Reverse($declaration.sourceClassificationIds)
        [array]::Reverse($declaration.targetModuleIds)
        [array]::Reverse($declaration.fixtureIds)
    }
    $reordered = New-DialectDeclarationVocabularyReport $inventory $compatibilityPack $reorderedCatalog
    Assert-DeclarationVocabularyContract ($reordered.vocabularySetHash -ceq $actual.vocabularySetHash) 'Catalog enumeration order changed the DIA-13 vocabulary hash.'

    $advancedInventory = Copy-DeclarationVocabularyObject $inventory
    $advancedInventory | Add-Member -NotePropertyName 'compatibilityPlanRuntime' -NotePropertyValue ([pscustomobject]@{ status = 'Implemented' })
    Assert-DeclarationVocabularyThrows { New-DialectDeclarationVocabularyReport $advancedInventory $compatibilityPack $catalog } 'requires M0-DIA-01 inventory evidence|runtime compatibility plan' 'DIA-13 accepted invalid or advanced source inventory evidence.'

    $staleInventoryCatalog = Copy-DeclarationVocabularyObject $catalog
    $staleInventoryCatalog.sourceInventoryHash = ('0' * 64)
    Assert-DeclarationVocabularyThrows { New-DialectDeclarationVocabularyReport $inventory $compatibilityPack $staleInventoryCatalog } 'source inventory hash drifted' 'DIA-13 accepted stale DIA-01 evidence.'

    $unknownDeclarationCatalog = Copy-DeclarationVocabularyObject $catalog
    $unknownDeclarationCatalog.declarations[0].id = 'unknown.future-policy.v1'
    Assert-DeclarationVocabularyThrows { New-DialectDeclarationVocabularyReport $inventory $compatibilityPack $unknownDeclarationCatalog } 'references unknown static declaration' 'DIA-13 accepted a declaration absent from DIA-01.'

    $wrongCountCatalog = Copy-DeclarationVocabularyObject $catalog
    $wrongCountCatalog.declarations[0].expectedBranchHitCount = 999
    Assert-DeclarationVocabularyThrows { New-DialectDeclarationVocabularyReport $inventory $compatibilityPack $wrongCountCatalog } 'branch hit count drifted' 'DIA-13 accepted source branch-hit count drift.'

    $exposedCapabilityPack = Copy-DeclarationVocabularyObject $compatibilityPack
    $exposedCapabilityPack.declarationPolicy.allowedCapabilityIds = @('compatibility.plan.selection.v1')
    Assert-DeclarationVocabularyThrows { New-DialectDeclarationVocabularyReport $inventory $exposedCapabilityPack $catalog } 'must not expose runtime capability ids' 'DIA-13 accepted a runtime capability leak into CompatibilityPack.'

    $outputPath = Join-Path $testRoot 'dialect-declaration-vocabulary.json'
    $written = New-DialectDeclarationVocabularyReport -Inventory $inventory -CompatibilityPack $compatibilityPack -Catalog $catalog -OutputPath $outputPath
    Assert-DeclarationVocabularyContract (Test-Path -LiteralPath $outputPath -PathType Leaf) 'DIA-13 report was not written.'
    Assert-DeclarationVocabularyContract ((Read-DeclarationVocabularyJson $outputPath).vocabularySetHash -ceq $written.vocabularySetHash) 'Written DIA-13 report hash drifted.'

    Write-Output 'M0 dialect declaration vocabulary contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-declaration-vocabulary-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
