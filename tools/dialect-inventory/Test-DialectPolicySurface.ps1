[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectPolicySurface.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-policy-surface.json'
$reportSchemaPath = Join-Path $toolRoot 'dialect-policy-surface.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'dialect-policy-surface-catalog.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-policy-surface-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-PolicySurfaceContract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-PolicySurfaceThrows([scriptblock]$Action, [string]$Pattern, [string]$Failure) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) { throw "$Failure Actual: $($caught.Exception.Message)" }
}

function Read-PolicySurfaceJson([string]$Path) {
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-PolicySurfaceObject([object]$Value) {
    return ($Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing DIA-15 module: $modulePath"
    }

    Import-Module $modulePath -Force
    $boundary = Read-PolicySurfaceJson (Join-Path $generatedRoot 'dialect-policy-consumer-boundary.json')
    $catalog = Read-PolicySurfaceJson $catalogPath
    $reportSchema = Read-PolicySurfaceJson $reportSchemaPath
    $catalogSchema = Read-PolicySurfaceJson $catalogSchemaPath

    $actual = New-DialectPolicySurfaceReport -PolicyConsumerBoundary $boundary -Catalog $catalog
    Assert-PolicySurfaceContract ($actual.workPackage -ceq 'M0-DIA-15') 'Unexpected DIA-15 work package.'
    Assert-PolicySurfaceContract ($actual.executionStatus -ceq 'InProgress' -and $actual.gateStatus -ceq 'Blocked' -and $actual.blockerCode -ceq 'EvidenceMissing') 'DIA-15 status was incorrectly advanced.'
    Assert-PolicySurfaceContract ($actual.result -ceq 'Partial') 'DIA-15 must remain Partial.'
    Assert-PolicySurfaceContract ($actual.policySurfaceSetHash -match '^[0-9a-f]{64}$') 'DIA-15 policy surface set hash is invalid.'
    Assert-PolicySurfaceContract ($actual.sourcePolicyConsumerBoundaryHash -ceq $boundary.boundarySetHash) 'DIA-15 did not pin DIA-14 evidence.'
    Assert-PolicySurfaceContract ($actual.sourceDeclarationVocabularyHash -ceq $boundary.sourceDeclarationVocabularyHash) 'DIA-15 did not retain DIA-13 provenance.'
    Assert-PolicySurfaceContract ($actual.currentRuntimeIsolation.status -ceq 'Failed') 'Runtime isolation was incorrectly advanced.'
    Assert-PolicySurfaceContract ($actual.parserVmConsumption.status -ceq 'NotConsumed') 'DIA-15 was incorrectly wired into Parser/VM.'
    Assert-PolicySurfaceContract ($actual.compatibilityPlanRuntime.status -ceq 'NotImplemented') 'DIA-15 was incorrectly presented as a runtime CompatibilityPlan.'
    Assert-PolicySurfaceContract ($actual.policyManagerRuntime.status -ceq 'NotImplemented') 'DIA-15 was incorrectly presented as a policy manager.'
    Assert-PolicySurfaceContract ($actual.runtimeTypeDeclarations.status -ceq 'NotImplemented') 'DIA-15 was incorrectly presented as implemented C# interfaces.'
    Assert-PolicySurfaceContract ($actual.m1Eligibility.status -ceq 'Blocked') 'DIA-15 incorrectly advanced M1 eligibility.'
    Assert-PolicySurfaceContract ($reportSchema.properties.workPackage.const -ceq 'M0-DIA-15') 'DIA-15 report schema work package drifted.'
    Assert-PolicySurfaceContract ($catalogSchema.properties.sourceWorkPackage.const -ceq 'M0-DIA-15') 'DIA-15 catalog schema work package drifted.'

    Assert-PolicySurfaceContract ($actual.surfaceCounts.behaviorKeyCount -eq 10 -and $actual.surfaceCounts.contractCount -eq 10) 'DIA-15 contract count drifted.'
    Assert-PolicySurfaceContract ($actual.surfaceCounts.policyDecisionCount -eq 6 -and $actual.surfaceCounts.bridgeProjectionCount -eq 2 -and $actual.surfaceCounts.frozenCatalogContributionCount -eq 2) 'DIA-15 contract kind count drifted.'
    Assert-PolicySurfaceContract ($actual.currentDeclarationVocabularyExposure.status -ceq 'NoRuntimeCapabilitiesDeclared') 'DIA-15 accepted runtime capability exposure.'
    Assert-PolicySurfaceContract ((@($actual.currentDeclarationVocabularyExposure.allowedCapabilityIds).Count) -eq 0) 'DIA-15 vocabulary exposure must be empty.'

    $behaviorKeys = @($actual.contracts | ForEach-Object behaviorKeyId)
    Assert-PolicySurfaceContract (($behaviorKeys -join ',') -ceq 'call.extra-arguments.v1,call.private-argument-shape.v1,display.history-capacity.v1,display.refresh-timing.v1,function.snake-fallen-state.v1,instruction.scoped-variable-registration.v1,parser.startup-fault.v1,parser.user-variable-resolution.v1,parser.warning-routing.v1,resource.lazy-index.v1') 'DIA-15 behavior key order drifted.'
    $extraArgs = @($actual.contracts | Where-Object behaviorKeyId -ceq 'call.extra-arguments.v1')
    $privateArgs = @($actual.contracts | Where-Object behaviorKeyId -ceq 'call.private-argument-shape.v1')
    $scoped = @($actual.contracts | Where-Object behaviorKeyId -ceq 'instruction.scoped-variable-registration.v1')
    $displayHistory = @($actual.contracts | Where-Object behaviorKeyId -ceq 'display.history-capacity.v1')
    Assert-PolicySurfaceContract ($extraArgs.Count -eq 1 -and $extraArgs[0].portTypeId -ceq 'IExtraArgumentPolicy' -and $extraArgs[0].contractKind -ceq 'PolicyDecision') 'Extra-argument policy surface drifted.'
    Assert-PolicySurfaceContract ($privateArgs.Count -eq 1 -and $privateArgs[0].portTypeId -ceq 'IPrivateArgumentShapePolicy') 'Private-argument policy surface drifted.'
    Assert-PolicySurfaceContract ($scoped.Count -eq 1 -and $scoped[0].portTypeId -ceq 'IInstructionCatalogBuilder' -and (@($scoped[0].sourceBindings | Where-Object role -ceq 'ConfigurationInput').Count -eq 1)) 'Scoped-variable policy surface drifted.'
    Assert-PolicySurfaceContract ($displayHistory.Count -eq 1 -and $displayHistory[0].contractKind -ceq 'BridgeProjection') 'Display projection surface drifted.'
    Assert-PolicySurfaceContract (@($actual.contracts | Where-Object { $_.contractDraftStatus -cne 'InterfaceDraftOnly' -or $_.policyValueStatus -cne 'Unspecified' -or $_.runtimeStatus -cne 'NotImplemented' }).Count -eq 0) 'DIA-15 incorrectly advanced an interface or policy value.'

    $designPath = Join-Path $ProjectRoot 'NewFrameworkDesign\DialectExtensionSystem.md'
    Assert-PolicySurfaceContract (Test-Path -LiteralPath $designPath -PathType Leaf) 'DIA-15 design authority document is missing.'
    $designText = Get-Content -Raw -Encoding UTF8 -LiteralPath $designPath
    foreach ($contract in @($actual.contracts)) {
        Assert-PolicySurfaceContract ($designText.Contains($contract.behaviorKeyId) -and $designText.Contains($contract.portTypeId) -and $designText.Contains($contract.consumerContractId)) "DIA-15 design document drifted from contract: $($contract.behaviorKeyId)"
    }
    foreach ($deprecatedIdentifier in @('call.extra_arguments', 'parser.startup_fault', 'parser.user_variable_resolution', 'call.private_argument_shape', 'resource.optional_record', 'display.refresh_timing', 'IStartupParseFaultPolicy', 'IPrivateArgumentPolicy', 'IResourceLookupPolicy')) {
        Assert-PolicySurfaceContract (-not $designText.Contains($deprecatedIdentifier)) "DIA-15 design document retained deprecated policy identifier: $deprecatedIdentifier"
    }

    $snapshot = @($actual.contracts | ForEach-Object { $_.behaviorKeyId + ':' + $_.consumerContractId + ':' + $_.portTypeId + ':' + $_.contractKind })
    $boundary.boundaries[0].decisionOwner = 'MUTATED_AFTER_POLICY_SURFACE_REPORT'
    Assert-PolicySurfaceContract ((@($actual.contracts | ForEach-Object { $_.behaviorKeyId + ':' + $_.consumerContractId + ':' + $_.portTypeId + ':' + $_.contractKind }) -join '|') -ceq ($snapshot -join '|')) 'DIA-15 retained a mutable reference to DIA-14 evidence.'
    $boundary = Read-PolicySurfaceJson (Join-Path $generatedRoot 'dialect-policy-consumer-boundary.json')

    $reorderedCatalog = Copy-PolicySurfaceObject $catalog
    [array]::Reverse($reorderedCatalog.contracts)
    $reordered = New-DialectPolicySurfaceReport $boundary $reorderedCatalog
    Assert-PolicySurfaceContract ($reordered.policySurfaceSetHash -ceq $actual.policySurfaceSetHash) 'Catalog enumeration order changed the DIA-15 policy surface hash.'

    $staleBoundaryCatalog = Copy-PolicySurfaceObject $catalog
    $staleBoundaryCatalog.sourcePolicyConsumerBoundaryHash = ('0' * 64)
    Assert-PolicySurfaceThrows { New-DialectPolicySurfaceReport $boundary $staleBoundaryCatalog } 'source policy consumer boundary hash drifted' 'DIA-15 accepted stale DIA-14 evidence.'

    $unknownBehaviorCatalog = Copy-PolicySurfaceObject $catalog
    $unknownBehaviorCatalog.contracts[0].behaviorKeyId = 'unknown.future-policy.v1'
    Assert-PolicySurfaceThrows { New-DialectPolicySurfaceReport $boundary $unknownBehaviorCatalog } 'references unknown BehaviorKey' 'DIA-15 accepted a behavior key absent from DIA-14.'

    $duplicatePortCatalog = Copy-PolicySurfaceObject $catalog
    $duplicatePortCatalog.contracts[1].portTypeId = $duplicatePortCatalog.contracts[0].portTypeId
    Assert-PolicySurfaceThrows { New-DialectPolicySurfaceReport $boundary $duplicatePortCatalog } 'Duplicate DIA-15 port type id' 'DIA-15 accepted duplicate interface draft ports.'

    $wrongOwnerCatalog = Copy-PolicySurfaceObject $catalog
    $wrongOwnerCatalog.contracts[0].decisionOwner = 'WrongDecisionOwner'
    Assert-PolicySurfaceThrows { New-DialectPolicySurfaceReport $boundary $wrongOwnerCatalog } 'decision owner drifted' 'DIA-15 accepted a decision owner that diverges from DIA-14.'

    $specifiedValueCatalog = Copy-PolicySurfaceObject $catalog
    $specifiedValueCatalog.contracts[0].policyValueStatus = 'Specified'
    Assert-PolicySurfaceThrows { New-DialectPolicySurfaceReport $boundary $specifiedValueCatalog } 'must remain Unspecified' 'DIA-15 accepted an unverified policy value.'

    $exposedBoundary = Copy-PolicySurfaceObject $boundary
    $exposedBoundary.currentDeclarationVocabularyExposure.allowedCapabilityIds = @('compatibility.plan.selection.v1')
    Assert-PolicySurfaceThrows { New-DialectPolicySurfaceReport $exposedBoundary $catalog } 'must retain no runtime capability exposure' 'DIA-15 accepted DIA-13 runtime capability exposure.'

    $outputPath = Join-Path $testRoot 'dialect-policy-surface.json'
    $written = New-DialectPolicySurfaceReport -PolicyConsumerBoundary $boundary -Catalog $catalog -OutputPath $outputPath
    Assert-PolicySurfaceContract (Test-Path -LiteralPath $outputPath -PathType Leaf) 'DIA-15 report was not written.'
    Assert-PolicySurfaceContract ((Read-PolicySurfaceJson $outputPath).policySurfaceSetHash -ceq $written.policySurfaceSetHash) 'Written DIA-15 report hash drifted.'

    Write-Output 'M0 dialect policy surface contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-policy-surface-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
