Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# M0-DIA-15 assigns stable, future-facing port names to the DIA-14 static
# consumer boundaries.  It intentionally has no C# type declarations,
# policy values, resolver, CompatibilityPlan, or Parser/VM integration.
$script:DialectPolicySurfaceUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-DialectPolicySurfaceSha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-DialectPolicySurfaceCanonicalHash {
    param([Parameter(Mandatory = $true)][object]$Value, [int]$Depth = 100)

    return Get-DialectPolicySurfaceSha256Hex -Bytes $script:DialectPolicySurfaceUtf8NoBom.GetBytes(($Value | ConvertTo-Json -Depth $Depth -Compress))
}

function Assert-DialectPolicySurfaceHash {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid DIA-15 hash: $Name" }
}

function Assert-DialectPolicySurfaceSemanticVersion {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Invalid DIA-15 semantic version: $Name" }
}

function Get-DialectPolicySurfaceStringSet {
    param(
        [object[]]$Values,
        [Parameter(Mandatory = $true)][string]$Name,
        [bool]$AllowEmpty = $false
    )

    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($value in @($Values)) {
        if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { throw "$Name contains an empty value." }
        $text = [string]$value
        if (-not $set.Add($text)) { throw "Duplicate $Name value: $text" }
    }
    if (-not $AllowEmpty -and $set.Count -eq 0) { throw "$Name must not be empty." }
    $result = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in $set) { $result.Add($item) }
    return $result.ToArray()
}

function Assert-DialectPolicySurfaceStringSet {
    param(
        [object[]]$Actual,
        [object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Context,
        [bool]$AllowEmpty = $false
    )

    $actualValues = @(Get-DialectPolicySurfaceStringSet -Values $Actual -Name "$Context actual values" -AllowEmpty $AllowEmpty)
    $expectedValues = @(Get-DialectPolicySurfaceStringSet -Values $Expected -Name "$Context expected values" -AllowEmpty $AllowEmpty)
    if ($actualValues.Count -ne $expectedValues.Count) { throw "$Context count drifted: expected $($expectedValues.Count), actual $($actualValues.Count)." }
    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -cne $expectedValues[$index]) { throw "$Context value drifted: expected $($expectedValues[$index]), actual $($actualValues[$index])." }
    }
}

function Assert-DialectPolicySurfaceObjectShape {
    param(
        [Parameter(Mandatory = $true)][object]$Value,
        [Parameter(Mandatory = $true)][string[]]$Required,
        [Parameter(Mandatory = $true)][string[]]$Allowed,
        [Parameter(Mandatory = $true)][string]$Context
    )

    foreach ($requiredName in $Required) {
        if ($null -eq $Value.PSObject.Properties[$requiredName]) { throw "$Context is missing required field: $requiredName" }
    }
    foreach ($property in @($Value.PSObject.Properties)) {
        $isAllowed = $false
        foreach ($allowedName in $Allowed) {
            if ($property.Name -ceq $allowedName) { $isAllowed = $true; break }
        }
        if (-not $isAllowed) { throw "$Context contains unsupported field: $($property.Name)" }
    }
}

function Copy-DialectPolicySurfaceSourceBinding {
    param([Parameter(Mandatory = $true)][object]$SourceBinding, [Parameter(Mandatory = $true)][string]$Context)

    $fields = @('classificationId', 'role', 'branchHitCount', 'sourceFilePaths', 'currentOwners', 'intendedOwners', 'targetModuleIds', 'fixtureIds')
    Assert-DialectPolicySurfaceObjectShape -Value $SourceBinding -Required $fields -Allowed $fields -Context $Context
    $role = [string]$SourceBinding.role
    if ($role -cne 'DecisionConsumer' -and $role -cne 'ConfigurationInput') { throw "Invalid DIA-15 source role: $role" }
    return [pscustomobject][ordered]@{
        classificationId = [string]$SourceBinding.classificationId
        role = $role
        branchHitCount = [int]$SourceBinding.branchHitCount
        sourceFilePaths = @(Get-DialectPolicySurfaceStringSet -Values @($SourceBinding.sourceFilePaths) -Name "$Context source file")
        currentOwners = @(Get-DialectPolicySurfaceStringSet -Values @($SourceBinding.currentOwners) -Name "$Context current owner")
        intendedOwners = @(Get-DialectPolicySurfaceStringSet -Values @($SourceBinding.intendedOwners) -Name "$Context intended owner")
        targetModuleIds = @(Get-DialectPolicySurfaceStringSet -Values @($SourceBinding.targetModuleIds) -Name "$Context target module")
        fixtureIds = @(Get-DialectPolicySurfaceStringSet -Values @($SourceBinding.fixtureIds) -Name "$Context fixture")
    }
}

function Get-DialectPolicySurfaceBoundaryContext {
    param([Parameter(Mandatory = $true)][object]$PolicyConsumerBoundary)

    if ([string]$PolicyConsumerBoundary.schemaVersion -cne '1.0.0' -or [string]$PolicyConsumerBoundary.workPackage -cne 'M0-DIA-14') {
        throw 'DIA-15 requires M0-DIA-14 policy consumer boundary evidence.'
    }
    Assert-DialectPolicySurfaceHash $PolicyConsumerBoundary.boundarySetHash 'M0-DIA-14 boundarySetHash'
    Assert-DialectPolicySurfaceHash $PolicyConsumerBoundary.sourceDeclarationVocabularyHash 'M0-DIA-14 sourceDeclarationVocabularyHash'
    if ([string]$PolicyConsumerBoundary.executionStatus -cne 'InProgress' -or [string]$PolicyConsumerBoundary.gateStatus -cne 'Blocked' -or [string]$PolicyConsumerBoundary.blockerCode -cne 'EvidenceMissing' -or [string]$PolicyConsumerBoundary.result -cne 'Partial') {
        throw 'DIA-14 evidence status was incorrectly advanced.'
    }
    if ([string]$PolicyConsumerBoundary.currentRuntimeIsolation.status -cne 'Failed' -or
        [string]$PolicyConsumerBoundary.parserVmConsumption.status -cne 'NotConsumed' -or
        [string]$PolicyConsumerBoundary.compatibilityPlanRuntime.status -cne 'NotImplemented' -or
        [string]$PolicyConsumerBoundary.policyManagerRuntime.status -cne 'NotImplemented' -or
        [string]$PolicyConsumerBoundary.m1Eligibility.status -cne 'Blocked') {
        throw 'DIA-14 runtime boundary status was incorrectly advanced.'
    }
    if ($null -eq $PolicyConsumerBoundary.PSObject.Properties['currentDeclarationVocabularyExposure']) { throw 'DIA-14 is missing current declaration vocabulary exposure.' }
    $exposure = $PolicyConsumerBoundary.currentDeclarationVocabularyExposure
    if ([string]$exposure.status -cne 'NoRuntimeCapabilitiesDeclared' -or $null -eq $exposure.PSObject.Properties['allowedCapabilityIds']) {
        throw 'DIA-14 must retain no runtime capability exposure.'
    }
    $allowedCapabilityIds = @(Get-DialectPolicySurfaceStringSet -Values @($exposure.allowedCapabilityIds) -Name 'DIA-14 allowed capability id' -AllowEmpty $true)
    if ($allowedCapabilityIds.Count -ne 0) { throw 'DIA-14 must retain no runtime capability exposure.' }

    $boundaryByBehaviorKey = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($boundary in @($PolicyConsumerBoundary.boundaries)) {
        $fields = @('behaviorKeyId', 'consumerContractId', 'decisionOwner', 'branchHitCount', 'currentOwners', 'sourceIntendedOwners', 'sourceClassificationIds', 'sourceFilePaths', 'targetModuleIds', 'fixtureIds', 'sourceBindings', 'boundaryDraftStatus', 'runtimeStatus')
        Assert-DialectPolicySurfaceObjectShape -Value $boundary -Required $fields -Allowed $fields -Context 'DIA-14 boundary evidence entry'
        $behaviorKeyId = [string]$boundary.behaviorKeyId
        $consumerContractId = [string]$boundary.consumerContractId
        $decisionOwner = [string]$boundary.decisionOwner
        if ($behaviorKeyId -notmatch '^[a-z][a-z0-9.\-]*\.v[0-9]+$' -or $consumerContractId -notmatch '^[a-z][a-z0-9.\-]*\.v[0-9]+$' -or [string]::IsNullOrWhiteSpace($decisionOwner)) {
            throw "DIA-14 boundary evidence is invalid: $behaviorKeyId"
        }
        if ([string]$boundary.boundaryDraftStatus -cne 'BoundaryDraftOnly' -or [string]$boundary.runtimeStatus -cne 'NotImplemented') {
            throw "DIA-14 boundary implementation status drifted: $behaviorKeyId"
        }
        if ($boundaryByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "Duplicate DIA-14 boundary evidence: $behaviorKeyId" }
        $sourceBindings = New-Object 'System.Collections.Generic.List[object]'
        foreach ($sourceBinding in @($boundary.sourceBindings)) {
            $sourceBindings.Add((Copy-DialectPolicySurfaceSourceBinding -SourceBinding $sourceBinding -Context "DIA-14 $behaviorKeyId source binding"))
        }
        if ($sourceBindings.Count -eq 0) { throw "DIA-14 boundary has no source bindings: $behaviorKeyId" }
        $boundaryByBehaviorKey.Add($behaviorKeyId, [pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                consumerContractId = $consumerContractId
                decisionOwner = $decisionOwner
                branchHitCount = [int]$boundary.branchHitCount
                currentOwners = @(Get-DialectPolicySurfaceStringSet -Values @($boundary.currentOwners) -Name "DIA-14 $behaviorKeyId current owner")
                sourceIntendedOwners = @(Get-DialectPolicySurfaceStringSet -Values @($boundary.sourceIntendedOwners) -Name "DIA-14 $behaviorKeyId intended owner")
                sourceClassificationIds = @(Get-DialectPolicySurfaceStringSet -Values @($boundary.sourceClassificationIds) -Name "DIA-14 $behaviorKeyId source classification")
                sourceFilePaths = @(Get-DialectPolicySurfaceStringSet -Values @($boundary.sourceFilePaths) -Name "DIA-14 $behaviorKeyId source file")
                targetModuleIds = @(Get-DialectPolicySurfaceStringSet -Values @($boundary.targetModuleIds) -Name "DIA-14 $behaviorKeyId target module")
                fixtureIds = @(Get-DialectPolicySurfaceStringSet -Values @($boundary.fixtureIds) -Name "DIA-14 $behaviorKeyId fixture")
                sourceBindings = $sourceBindings.ToArray()
            })
    }
    if ($boundaryByBehaviorKey.Count -ne 10 -or [int]$PolicyConsumerBoundary.boundaryCounts.behaviorKeyCount -ne 10 -or [int]$PolicyConsumerBoundary.boundaryCounts.decisionBoundaryCount -ne 10) {
        throw 'DIA-14 must retain exactly ten BehaviorKey decision boundaries.'
    }
    return [pscustomobject][ordered]@{
        sourcePolicyConsumerBoundaryHash = [string]$PolicyConsumerBoundary.boundarySetHash
        sourceDeclarationVocabularyHash = [string]$PolicyConsumerBoundary.sourceDeclarationVocabularyHash
        boundaryByBehaviorKey = $boundaryByBehaviorKey
        currentDeclarationVocabularyExposure = [ordered]@{
            status = 'NoRuntimeCapabilitiesDeclared'
            allowedCapabilityIds = @()
            reason = 'DIA-14 retains the DIA-13 static declaration exposure without runtime capability ids.'
        }
    }
}

function Get-DialectPolicySurfaceCatalogContext {
    param([Parameter(Mandatory = $true)][object]$Catalog)

    $catalogFields = @('schemaVersion', 'catalogId', 'catalogVersion', 'sourceWorkPackage', 'sourcePolicyConsumerBoundaryWorkPackage', 'sourceDeclarationVocabularyWorkPackage', 'sourcePolicyConsumerBoundaryHash', 'sourceDeclarationVocabularyHash', 'expectedBehaviorKeyCount', 'expectedContractCount', 'expectedPolicyDecisionCount', 'expectedBridgeProjectionCount', 'expectedFrozenCatalogContributionCount', 'contracts')
    Assert-DialectPolicySurfaceObjectShape -Value $Catalog -Required $catalogFields -Allowed $catalogFields -Context 'DIA-15 catalog'
    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-15' -or
        [string]$Catalog.sourcePolicyConsumerBoundaryWorkPackage -cne 'M0-DIA-14' -or [string]$Catalog.sourceDeclarationVocabularyWorkPackage -cne 'M0-DIA-13' -or
        [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported DIA-15 policy surface catalog.'
    }
    Assert-DialectPolicySurfaceSemanticVersion $Catalog.catalogVersion 'catalogVersion'
    Assert-DialectPolicySurfaceHash $Catalog.sourcePolicyConsumerBoundaryHash 'catalog sourcePolicyConsumerBoundaryHash'
    Assert-DialectPolicySurfaceHash $Catalog.sourceDeclarationVocabularyHash 'catalog sourceDeclarationVocabularyHash'
    foreach ($countName in @('expectedBehaviorKeyCount', 'expectedContractCount', 'expectedPolicyDecisionCount', 'expectedBridgeProjectionCount', 'expectedFrozenCatalogContributionCount')) {
        if ([int]$Catalog.$countName -le 0) { throw "DIA-15 catalog $countName is invalid." }
    }

    $contractByBehaviorKey = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $portTypeIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($contract in @($Catalog.contracts)) {
        $contractFields = @('behaviorKeyId', 'consumerContractId', 'decisionOwner', 'portTypeId', 'contractKind', 'contractDraftStatus', 'policyValueStatus')
        Assert-DialectPolicySurfaceObjectShape -Value $contract -Required $contractFields -Allowed $contractFields -Context 'DIA-15 contract catalog entry'
        $behaviorKeyId = [string]$contract.behaviorKeyId
        $consumerContractId = [string]$contract.consumerContractId
        $decisionOwner = [string]$contract.decisionOwner
        $portTypeId = [string]$contract.portTypeId
        $contractKind = [string]$contract.contractKind
        if ($behaviorKeyId -notmatch '^[a-z][a-z0-9.\-]*\.v[0-9]+$' -or $consumerContractId -notmatch '^[a-z][a-z0-9.\-]*\.v[0-9]+$') { throw "Invalid DIA-15 contract key: $behaviorKeyId" }
        if ([string]::IsNullOrWhiteSpace($decisionOwner)) { throw "DIA-15 decision owner is empty: $behaviorKeyId" }
        if ($portTypeId -notmatch '^I[A-Za-z][A-Za-z0-9]*$') { throw "Invalid DIA-15 port type id: $portTypeId" }
        if ($contractKind -cne 'PolicyDecision' -and $contractKind -cne 'BridgeProjection' -and $contractKind -cne 'FrozenCatalogContribution') { throw "Invalid DIA-15 contract kind: $contractKind" }
        if ([string]$contract.contractDraftStatus -cne 'InterfaceDraftOnly') { throw "DIA-15 contract draft status must remain InterfaceDraftOnly: $behaviorKeyId" }
        if ([string]$contract.policyValueStatus -cne 'Unspecified') { throw "DIA-15 policy value status must remain Unspecified: $behaviorKeyId" }
        if ($contractByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "Duplicate DIA-15 BehaviorKey contract: $behaviorKeyId" }
        if (-not $portTypeIds.Add($portTypeId)) { throw "Duplicate DIA-15 port type id: $portTypeId" }
        $contractByBehaviorKey.Add($behaviorKeyId, [pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                consumerContractId = $consumerContractId
                decisionOwner = $decisionOwner
                portTypeId = $portTypeId
                contractKind = $contractKind
                contractDraftStatus = 'InterfaceDraftOnly'
                policyValueStatus = 'Unspecified'
            })
    }
    if ($contractByBehaviorKey.Count -eq 0) { throw 'DIA-15 catalog must contain policy surface contracts.' }

    $canonicalContracts = New-Object 'System.Collections.Generic.List[object]'
    foreach ($contract in $contractByBehaviorKey.Values) {
        $canonicalContracts.Add([ordered]@{
                behaviorKeyId = $contract.behaviorKeyId
                consumerContractId = $contract.consumerContractId
                decisionOwner = $contract.decisionOwner
                portTypeId = $contract.portTypeId
                contractKind = $contract.contractKind
                contractDraftStatus = $contract.contractDraftStatus
                policyValueStatus = $contract.policyValueStatus
            })
    }
    $canonicalCatalog = [ordered]@{
        schemaVersion = '1.0.0'; catalogId = [string]$Catalog.catalogId; catalogVersion = [string]$Catalog.catalogVersion
        sourceWorkPackage = 'M0-DIA-15'; sourcePolicyConsumerBoundaryWorkPackage = 'M0-DIA-14'; sourceDeclarationVocabularyWorkPackage = 'M0-DIA-13'
        sourcePolicyConsumerBoundaryHash = [string]$Catalog.sourcePolicyConsumerBoundaryHash; sourceDeclarationVocabularyHash = [string]$Catalog.sourceDeclarationVocabularyHash
        expectedBehaviorKeyCount = [int]$Catalog.expectedBehaviorKeyCount; expectedContractCount = [int]$Catalog.expectedContractCount
        expectedPolicyDecisionCount = [int]$Catalog.expectedPolicyDecisionCount; expectedBridgeProjectionCount = [int]$Catalog.expectedBridgeProjectionCount
        expectedFrozenCatalogContributionCount = [int]$Catalog.expectedFrozenCatalogContributionCount
        contracts = $canonicalContracts.ToArray()
    }
    return [pscustomobject][ordered]@{
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = Get-DialectPolicySurfaceCanonicalHash -Value $canonicalCatalog
        sourcePolicyConsumerBoundaryHash = [string]$Catalog.sourcePolicyConsumerBoundaryHash
        sourceDeclarationVocabularyHash = [string]$Catalog.sourceDeclarationVocabularyHash
        expectedBehaviorKeyCount = [int]$Catalog.expectedBehaviorKeyCount
        expectedContractCount = [int]$Catalog.expectedContractCount
        expectedPolicyDecisionCount = [int]$Catalog.expectedPolicyDecisionCount
        expectedBridgeProjectionCount = [int]$Catalog.expectedBridgeProjectionCount
        expectedFrozenCatalogContributionCount = [int]$Catalog.expectedFrozenCatalogContributionCount
        contractByBehaviorKey = $contractByBehaviorKey
    }
}

function New-DialectPolicySurfaceReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][object]$PolicyConsumerBoundary,
        [Parameter(Mandatory = $true, Position = 1)][object]$Catalog,
        [string]$OutputPath = ''
    )

    $boundaryContext = Get-DialectPolicySurfaceBoundaryContext -PolicyConsumerBoundary $PolicyConsumerBoundary
    $catalogContext = Get-DialectPolicySurfaceCatalogContext -Catalog $Catalog
    if ($catalogContext.sourcePolicyConsumerBoundaryHash -cne $boundaryContext.sourcePolicyConsumerBoundaryHash) { throw 'DIA-15 source policy consumer boundary hash drifted.' }
    if ($catalogContext.sourceDeclarationVocabularyHash -cne $boundaryContext.sourceDeclarationVocabularyHash) { throw 'DIA-15 source declaration vocabulary hash drifted.' }
    if ($catalogContext.contractByBehaviorKey.Count -ne $boundaryContext.boundaryByBehaviorKey.Count) { throw 'DIA-15 contract count drifted from DIA-14 boundaries.' }

    $contracts = New-Object 'System.Collections.Generic.List[object]'
    $policyDecisionCount = 0
    $bridgeProjectionCount = 0
    $frozenCatalogContributionCount = 0
    $sourceBindingCount = 0
    foreach ($behaviorKeyId in $catalogContext.contractByBehaviorKey.Keys) {
        $catalogContract = $catalogContext.contractByBehaviorKey[$behaviorKeyId]
        if (-not $boundaryContext.boundaryByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "DIA-15 catalog references unknown BehaviorKey: $behaviorKeyId" }
        $sourceBoundary = $boundaryContext.boundaryByBehaviorKey[$behaviorKeyId]
        if ($catalogContract.consumerContractId -cne $sourceBoundary.consumerContractId) { throw "DIA-15 consumer contract id drifted for $behaviorKeyId" }
        if ($catalogContract.decisionOwner -cne $sourceBoundary.decisionOwner) { throw "DIA-15 decision owner drifted for $behaviorKeyId" }
        $sourceBindings = New-Object 'System.Collections.Generic.List[object]'
        foreach ($sourceBinding in @($sourceBoundary.sourceBindings)) {
            $sourceBindings.Add((Copy-DialectPolicySurfaceSourceBinding -SourceBinding $sourceBinding -Context "DIA-15 $behaviorKeyId source binding"))
            $sourceBindingCount++
        }
        switch ($catalogContract.contractKind) {
            'PolicyDecision' { $policyDecisionCount++ }
            'BridgeProjection' { $bridgeProjectionCount++ }
            'FrozenCatalogContribution' { $frozenCatalogContributionCount++ }
            default { throw "Invalid DIA-15 contract kind: $($catalogContract.contractKind)" }
        }
        $contracts.Add([pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                consumerContractId = $sourceBoundary.consumerContractId
                decisionOwner = $sourceBoundary.decisionOwner
                portTypeId = $catalogContract.portTypeId
                contractKind = $catalogContract.contractKind
                branchHitCount = [int]$sourceBoundary.branchHitCount
                currentOwners = @($sourceBoundary.currentOwners)
                sourceIntendedOwners = @($sourceBoundary.sourceIntendedOwners)
                sourceClassificationIds = @($sourceBoundary.sourceClassificationIds)
                sourceFilePaths = @($sourceBoundary.sourceFilePaths)
                targetModuleIds = @($sourceBoundary.targetModuleIds)
                fixtureIds = @($sourceBoundary.fixtureIds)
                sourceBindings = $sourceBindings.ToArray()
                contractDraftStatus = 'InterfaceDraftOnly'
                policyValueStatus = 'Unspecified'
                runtimeStatus = 'NotImplemented'
            })
    }
    foreach ($behaviorKeyId in $boundaryContext.boundaryByBehaviorKey.Keys) {
        if (-not $catalogContext.contractByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "DIA-15 BehaviorKey has no policy surface contract: $behaviorKeyId" }
    }

    $surfaceCounts = [ordered]@{
        behaviorKeyCount = [int]$boundaryContext.boundaryByBehaviorKey.Count
        contractCount = [int]$contracts.Count
        policyDecisionCount = [int]$policyDecisionCount
        bridgeProjectionCount = [int]$bridgeProjectionCount
        frozenCatalogContributionCount = [int]$frozenCatalogContributionCount
        sourceBindingCount = [int]$sourceBindingCount
    }
    if ($surfaceCounts.behaviorKeyCount -ne $catalogContext.expectedBehaviorKeyCount -or
        $surfaceCounts.contractCount -ne $catalogContext.expectedContractCount -or
        $surfaceCounts.policyDecisionCount -ne $catalogContext.expectedPolicyDecisionCount -or
        $surfaceCounts.bridgeProjectionCount -ne $catalogContext.expectedBridgeProjectionCount -or
        $surfaceCounts.frozenCatalogContributionCount -ne $catalogContext.expectedFrozenCatalogContributionCount) {
        throw "DIA-15 policy surface count drifted: behavior=$($surfaceCounts.behaviorKeyCount), contract=$($surfaceCounts.contractCount), policy=$($surfaceCounts.policyDecisionCount), bridge=$($surfaceCounts.bridgeProjectionCount), catalog=$($surfaceCounts.frozenCatalogContributionCount)."
    }
    if ($surfaceCounts.sourceBindingCount -ne 14) { throw "DIA-15 source binding count drifted: $($surfaceCounts.sourceBindingCount)" }
    $currentRuntimeIsolation = [ordered]@{ status = 'Failed'; reason = 'DIA-15 allocates static future port names but does not isolate legacy static profile, configuration or registry state.' }
    $parserVmConsumption = [ordered]@{ status = 'NotConsumed'; reason = 'DIA-15 is an offline policy surface report and is not read by the legacy Parser or VM.' }
    $compatibilityPlanRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-15 does not create a runtime CompatibilityPlan, DialectPlan, LegacySessionFacade, feature flag or frozen registry.' }
    $policyManagerRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-15 declares no C# policy interface implementation, policy manager, resolver, policy value or default.' }
    $runtimeTypeDeclarations = [ordered]@{ status = 'NotImplemented'; reason = 'portTypeId is a static interface draft name only; DIA-15 does not add a C# interface, class, assembly or reflection registration.' }
    $m1Eligibility = [ordered]@{ status = 'Blocked'; reason = 'Static port allocation does not establish session isolation, generation guards, A-to-B-to-A rollback or runtime capability binding.' }
    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'; workPackage = 'M0-DIA-15'
        sourcePolicyConsumerBoundaryHash = $boundaryContext.sourcePolicyConsumerBoundaryHash; sourceDeclarationVocabularyHash = $boundaryContext.sourceDeclarationVocabularyHash
        catalogId = $catalogContext.catalogId; catalogVersion = $catalogContext.catalogVersion; catalogHash = $catalogContext.catalogHash
        surfaceCounts = $surfaceCounts; currentDeclarationVocabularyExposure = $boundaryContext.currentDeclarationVocabularyExposure
        contracts = $contracts.ToArray(); currentRuntimeIsolation = $currentRuntimeIsolation; parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime; policyManagerRuntime = $policyManagerRuntime; runtimeTypeDeclarations = $runtimeTypeDeclarations; m1Eligibility = $m1Eligibility
    }
    $policySurfaceSetHash = Get-DialectPolicySurfaceCanonicalHash -Value $setPayload -Depth 100
    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-15'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourcePolicyConsumerBoundaryHash = $boundaryContext.sourcePolicyConsumerBoundaryHash
        sourceDeclarationVocabularyHash = $boundaryContext.sourceDeclarationVocabularyHash
        catalogId = $catalogContext.catalogId
        catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash
        policySurfaceSetHash = $policySurfaceSetHash
        surfaceCounts = $surfaceCounts
        currentDeclarationVocabularyExposure = $boundaryContext.currentDeclarationVocabularyExposure
        contracts = $contracts.ToArray()
        currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime
        policyManagerRuntime = $policyManagerRuntime
        runtimeTypeDeclarations = $runtimeTypeDeclarations
        m1Eligibility = $m1Eligibility
        uncovered = @(
            'DIA-15 allocates only future port names and contract families. It does not define a policy method signature, input DTO, output decision enum, default, error path, completion mode, effect or timing.',
            'Every port remains InterfaceDraftOnly and Unspecified until schema review plus v24/Snake two-sided fixtures establish a script-observable contract.',
            'No C# interface, policy manager, resolver, CompatibilityPlan, LegacySessionFacade, D2 frozen registry, Parser/VM consumption, game run, APK or device evidence is introduced.'
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, (($report | ConvertTo-Json -Depth 100) + "`n"), $script:DialectPolicySurfaceUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectPolicySurfaceReport
