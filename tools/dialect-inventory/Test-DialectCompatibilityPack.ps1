[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectCompatibilityPack.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-compatibility-pack.json'
$reportSchemaPath = Join-Path $toolRoot 'dialect-compatibility-pack.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'dialect-compatibility-pack-catalog.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-compatibility-pack-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-CompatibilityPackContract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-CompatibilityPackThrows([scriptblock]$Action, [string]$Pattern, [string]$Failure) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) { throw "$Failure Actual: $($caught.Exception.Message)" }
}

function Read-CompatibilityPackJson([string]$Path) {
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-CompatibilityPackObject([object]$Value) {
    return ($Value | ConvertTo-Json -Depth 80 | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing DIA-12 module: $modulePath"
    }

    Import-Module $modulePath -Force
    $catalog = Read-CompatibilityPackJson $catalogPath
    $planPreflight = Read-CompatibilityPackJson (Join-Path $generatedRoot 'dialect-plan-preflight.json')
    $profileSelection = Read-CompatibilityPackJson (Join-Path $generatedRoot 'dialect-profile-selection.json')
    $reportSchema = Read-CompatibilityPackJson $reportSchemaPath
    $catalogSchema = Read-CompatibilityPackJson $catalogSchemaPath

    $actual = New-DialectCompatibilityPackReport -PlanPreflight $planPreflight -ProfileSelection $profileSelection -Catalog $catalog
    Assert-CompatibilityPackContract ($actual.workPackage -ceq 'M0-DIA-12') 'Unexpected DIA-12 work package.'
    Assert-CompatibilityPackContract ($actual.executionStatus -ceq 'InProgress' -and $actual.gateStatus -ceq 'Blocked' -and $actual.blockerCode -ceq 'EvidenceMissing') 'DIA-12 status was incorrectly advanced.'
    Assert-CompatibilityPackContract ($actual.result -ceq 'Partial') 'DIA-12 must remain Partial.'
    Assert-CompatibilityPackContract ($actual.compatibilityPackSetHash -match '^[0-9a-f]{64}$') 'DIA-12 compatibility pack set hash is invalid.'
    Assert-CompatibilityPackContract ($actual.sourcePlanPreflightHash -ceq $planPreflight.preflightSetHash) 'DIA-12 did not pin DIA-10 evidence.'
    Assert-CompatibilityPackContract ($actual.sourceProfileSelectionHash -ceq $profileSelection.selectionSetHash) 'DIA-12 did not pin DIA-11 evidence.'
    Assert-CompatibilityPackContract ($actual.currentRuntimeIsolation.status -ceq 'Failed') 'Runtime isolation was incorrectly advanced.'
    Assert-CompatibilityPackContract ($actual.parserVmConsumption.status -ceq 'NotConsumed') 'DIA-12 was incorrectly wired into Parser/VM.'
    Assert-CompatibilityPackContract ($actual.compatibilityPlanRuntime.status -ceq 'NotImplemented') 'DIA-12 was incorrectly presented as a runtime CompatibilityPlan.'
    Assert-CompatibilityPackContract ($actual.resolverRuntime.status -ceq 'NotImplemented') 'DIA-12 was incorrectly presented as a runtime resolver.'
    Assert-CompatibilityPackContract ($actual.m1Eligibility.status -ceq 'Blocked') 'DIA-12 incorrectly advanced M1 eligibility.'
    Assert-CompatibilityPackContract ($reportSchema.properties.workPackage.const -ceq 'M0-DIA-12') 'DIA-12 report schema work package drifted.'
    Assert-CompatibilityPackContract ($catalogSchema.properties.sourceWorkPackage.const -ceq 'M0-DIA-12') 'DIA-12 catalog schema work package drifted.'

    Assert-CompatibilityPackContract ($actual.declarationPolicy.compatibilityPackSchema -ceq 'emuera.compatibility-pack/v1') 'CompatibilityPack schema id drifted.'
    Assert-CompatibilityPackContract ($actual.declarationPolicy.externalCodePolicy -ceq 'BuiltInCompiledOnly') 'DIA-12 external-code policy drifted.'
    Assert-CompatibilityPackContract ($actual.declarationPolicy.contentBindingStatus -ceq 'NotBound') 'DIA-12 must not claim a content fingerprint binding.'
    Assert-CompatibilityPackContract ((@($actual.declarationPolicy.allowedCapabilityIds).Count) -eq 0) 'DIA-12 unexpectedly declared runtime capabilities.'

    Assert-CompatibilityPackContract ((@($actual.packs.profileId) -join ',') -ceq 'snake,v24pure') 'DIA-12 pack profile set drifted.'
    $v24 = @($actual.packs | Where-Object profileId -ceq 'v24pure')
    $snake = @($actual.packs | Where-Object profileId -ceq 'snake')
    Assert-CompatibilityPackContract ($v24.Count -eq 1 -and $v24[0].legacyCoreProfileEnum -ceq 'V24Pure') 'v24 CompatibilityPack mapping drifted.'
    Assert-CompatibilityPackContract ($snake.Count -eq 1 -and $snake[0].legacyCoreProfileEnum -ceq 'Snake') 'Snake CompatibilityPack mapping drifted.'
    Assert-CompatibilityPackContract ((@($v24[0].builtInModuleIds) -join ',') -ceq 'gemuera.v24') 'v24 built-in module declaration drifted.'
    Assert-CompatibilityPackContract ((@($snake[0].builtInModuleIds) -join ',') -ceq 'game.snake,gemuera.v24') 'Snake built-in module declaration drifted.'
    Assert-CompatibilityPackContract ((@($v24[0].staticProjectionSupportModuleIds) -join ',') -ceq 'legacy.current.common,legacy.current.expression') 'v24 static projection support declaration drifted.'
    Assert-CompatibilityPackContract ((@($snake[0].staticProjectionSupportModuleIds) -join ',') -ceq 'legacy.current.common,legacy.current.expression') 'Snake static projection support declaration drifted.'
    Assert-CompatibilityPackContract ((@($v24[0].requiredCapabilityIds).Count) -eq 0 -and (@($snake[0].requiredCapabilityIds).Count) -eq 0) 'DIA-12 must keep required capability declarations explicit and empty.'
    Assert-CompatibilityPackContract ($v24[0].distributionEligibility -ceq 'Blocked' -and $snake[0].distributionEligibility -ceq 'Blocked') 'DIA-12 incorrectly marked a static declaration distributable.'
    Assert-CompatibilityPackContract ($v24[0].saveProfileEvidenceStatus -ceq 'Uncovered' -and $snake[0].fixtureEvidenceStatus -ceq 'Uncovered') 'DIA-12 evidence gaps drifted.'

    $packSnapshot = @($actual.packs | ForEach-Object { $_.profileId + ':' + ($_.builtInModuleIds -join '+') + ':' + $_.distributionEligibility })
    $planPreflight.profiles[0].legacyCoreProfileEnum = 'MUTATED_AFTER_PACK_REPORT'
    $profileSelection.legacyCoreProfiles[0].legacyCoreProfileEnum = 'MUTATED_AFTER_PACK_REPORT'
    Assert-CompatibilityPackContract ((@($actual.packs | ForEach-Object { $_.profileId + ':' + ($_.builtInModuleIds -join '+') + ':' + $_.distributionEligibility }) -join '|') -ceq ($packSnapshot -join '|')) 'DIA-12 retained a mutable reference to source evidence.'
    $planPreflight = Read-CompatibilityPackJson (Join-Path $generatedRoot 'dialect-plan-preflight.json')
    $profileSelection = Read-CompatibilityPackJson (Join-Path $generatedRoot 'dialect-profile-selection.json')

    $reorderedCatalog = Copy-CompatibilityPackObject $catalog
    [array]::Reverse($reorderedCatalog.builtInModules)
    [array]::Reverse($reorderedCatalog.profileContracts)
    [array]::Reverse($reorderedCatalog.packs)
    foreach ($pack in @($reorderedCatalog.packs)) {
        [array]::Reverse($pack.builtInModuleIds)
        [array]::Reverse($pack.projectionEvidenceModuleIds)
    }
    $reordered = New-DialectCompatibilityPackReport $planPreflight $profileSelection $reorderedCatalog
    Assert-CompatibilityPackContract ($reordered.compatibilityPackSetHash -ceq $actual.compatibilityPackSetHash) 'Catalog enumeration order changed the DIA-12 set hash.'

    $advancedPlan = Copy-CompatibilityPackObject $planPreflight
    $advancedPlan.compatibilityPlanRuntime.status = 'Implemented'
    Assert-CompatibilityPackThrows { New-DialectCompatibilityPackReport $advancedPlan $profileSelection $catalog } 'runtime compatibility plan status must remain NotImplemented' 'DIA-12 accepted an advanced DIA-10 runtime status.'

    $staleSelectionHashCatalog = Copy-CompatibilityPackObject $catalog
    $staleSelectionHashCatalog.sourceProfileSelectionHash = ('0' * 64)
    Assert-CompatibilityPackThrows { New-DialectCompatibilityPackReport $planPreflight $profileSelection $staleSelectionHashCatalog } 'source profile selection hash drifted' 'DIA-12 accepted stale DIA-11 evidence.'

    $unverifiedModuleCatalog = Copy-CompatibilityPackObject $catalog
    $unverifiedModuleCatalog.builtInModules[0].id = 'external.unverified.module'
    Assert-CompatibilityPackThrows { New-DialectCompatibilityPackReport $planPreflight $profileSelection $unverifiedModuleCatalog } 'unverified built-in module' 'DIA-12 accepted an unverified module declaration.'

    $executablePayloadCatalog = Copy-CompatibilityPackObject $catalog
    $executablePayloadCatalog.packs[0] | Add-Member -NotePropertyName 'dllPath' -NotePropertyValue 'mods/unsafe.dll'
    Assert-CompatibilityPackThrows { New-DialectCompatibilityPackReport $planPreflight $profileSelection $executablePayloadCatalog } 'forbids executable payload field' 'DIA-12 accepted an executable DLL payload.'

    $missingCapabilityCatalog = Copy-CompatibilityPackObject $catalog
    $missingCapabilityCatalog.packs[0].PSObject.Properties.Remove('requiredCapabilities')
    Assert-CompatibilityPackThrows { New-DialectCompatibilityPackReport $planPreflight $profileSelection $missingCapabilityCatalog } 'missing required field: requiredCapabilities' 'DIA-12 accepted a pack with no required-capability declaration.'

    $modernProfileCatalog = Copy-CompatibilityPackObject $catalog
    $modernProfileCatalog.profileContracts[0].profileId = 'snake-modern-mobile'
    $modernProfileCatalog.packs[0].profileId = 'snake-modern-mobile'
    Assert-CompatibilityPackThrows { New-DialectCompatibilityPackReport $planPreflight $profileSelection $modernProfileCatalog } 'Unsupported DIA-12 profile: snake-modern-mobile' 'DIA-12 accepted SnakeModernMobile without independent evidence.'

    $outputPath = Join-Path $testRoot 'dialect-compatibility-pack.json'
    $written = New-DialectCompatibilityPackReport -PlanPreflight $planPreflight -ProfileSelection $profileSelection -Catalog $catalog -OutputPath $outputPath
    Assert-CompatibilityPackContract (Test-Path -LiteralPath $outputPath -PathType Leaf) 'DIA-12 report was not written.'
    Assert-CompatibilityPackContract ((Read-CompatibilityPackJson $outputPath).compatibilityPackSetHash -ceq $written.compatibilityPackSetHash) 'Written DIA-12 report hash drifted.'

    Write-Output 'M0 dialect CompatibilityPack contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-compatibility-pack-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
