Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# M0-DIA-17 is deliberately an offline evidence gate.  It turns the static
# BehaviorKey/port allocation into fixture requirements, but never creates a
# policy implementation, resolver, CompatibilityPlan, registry, or VM input.
$script:DialectBehaviorFixtureContractsUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-DialectBehaviorFixtureContractsSha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-DialectBehaviorFixtureContractsCanonicalHash {
    param([Parameter(Mandatory = $true)][object]$Value, [int]$Depth = 100)

    return Get-DialectBehaviorFixtureContractsSha256Hex -Bytes $script:DialectBehaviorFixtureContractsUtf8NoBom.GetBytes(($Value | ConvertTo-Json -Depth $Depth -Compress))
}

function Assert-DialectBehaviorFixtureContractsHash {
    param([object]$Value, [Parameter(Mandatory = $true)][string]$Name)

    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid M0-DIA-17 hash: $Name" }
}

function Assert-DialectBehaviorFixtureContractsObjectShape {
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
        if ($property.Name -cnotin $Allowed) { throw "$Context contains unsupported field: $($property.Name)" }
    }
}

function Get-DialectBehaviorFixtureContractsStringSet {
    param(
        [object[]]$Values,
        [Parameter(Mandatory = $true)][string]$Name,
        [bool]$AllowEmpty = $false
    )

    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($value in @($Values)) {
        if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { throw "$Name contains an empty value." }
        if (-not $set.Add([string]$value)) { throw "Duplicate $Name value: $value" }
    }
    if (-not $AllowEmpty -and $set.Count -eq 0) { throw "$Name must not be empty." }
    $result = New-Object 'System.Collections.Generic.List[string]'
    foreach ($value in $set) { $result.Add($value) }
    return $result.ToArray()
}

function Assert-DialectBehaviorFixtureContractsStringSet {
    param(
        [object[]]$Actual,
        [object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Context,
        [bool]$AllowEmpty = $false
    )

    $actualValues = @(Get-DialectBehaviorFixtureContractsStringSet -Values $Actual -Name "$Context actual values" -AllowEmpty $AllowEmpty)
    $expectedValues = @(Get-DialectBehaviorFixtureContractsStringSet -Values $Expected -Name "$Context expected values" -AllowEmpty $AllowEmpty)
    if ($actualValues.Count -ne $expectedValues.Count) { throw "$Context count drifted: expected $($expectedValues.Count), actual $($actualValues.Count)." }
    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -cne $expectedValues[$index]) { throw "$Context value drifted: expected $($expectedValues[$index]), actual $($actualValues[$index])." }
    }
}

function Get-DialectBehaviorFixtureContractsVocabularyContext {
    param([Parameter(Mandatory = $true)][object]$DeclarationVocabulary)

    if ([string]$DeclarationVocabulary.schemaVersion -cne '1.0.0' -or [string]$DeclarationVocabulary.workPackage -cne 'M0-DIA-13') {
        throw 'M0-DIA-17 requires M0-DIA-13 declaration vocabulary evidence.'
    }
    Assert-DialectBehaviorFixtureContractsHash $DeclarationVocabulary.vocabularySetHash 'M0-DIA-13 vocabularySetHash'
    if ([string]$DeclarationVocabulary.executionStatus -cne 'InProgress' -or [string]$DeclarationVocabulary.gateStatus -cne 'Blocked' -or
        [string]$DeclarationVocabulary.blockerCode -cne 'EvidenceMissing' -or [string]$DeclarationVocabulary.result -cne 'Partial') {
        throw 'M0-DIA-13 evidence status was incorrectly advanced.'
    }
    if ([string]$DeclarationVocabulary.currentRuntimeIsolation.status -cne 'Failed' -or
        [string]$DeclarationVocabulary.parserVmConsumption.status -cne 'NotConsumed' -or
        [string]$DeclarationVocabulary.compatibilityPlanRuntime.status -cne 'NotImplemented' -or
        [string]$DeclarationVocabulary.resolverRuntime.status -cne 'NotImplemented' -or
        [string]$DeclarationVocabulary.m1Eligibility.status -cne 'Blocked') {
        throw 'M0-DIA-13 runtime boundary status was incorrectly advanced.'
    }

    $behaviorById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($declaration in @($DeclarationVocabulary.declarations)) {
        $fields = @('kind', 'id', 'branchHitCount', 'sourceClassificationIds', 'targetModuleIds', 'fixtureIds', 'declarationStatus', 'currentPackEligibility', 'runtimeStatus')
        Assert-DialectBehaviorFixtureContractsObjectShape -Value $declaration -Required $fields -Allowed $fields -Context 'M0-DIA-13 declaration'
        if ([string]$declaration.kind -cne 'BehaviorKey') { continue }
        $behaviorKeyId = [string]$declaration.id
        if ($behaviorKeyId -notmatch '^[a-z][a-z0-9.-]+\.v[0-9]+$') { throw "Invalid M0-DIA-13 BehaviorKey id: $behaviorKeyId" }
        if ($behaviorById.ContainsKey($behaviorKeyId)) { throw "Duplicate M0-DIA-13 BehaviorKey: $behaviorKeyId" }
        if ([int]$declaration.branchHitCount -le 0 -or [string]$declaration.declarationStatus -cne 'StaticCandidate' -or
            [string]$declaration.currentPackEligibility -cne 'NotEligible' -or [string]$declaration.runtimeStatus -cne 'NotImplemented') {
            throw "M0-DIA-13 BehaviorKey status drifted: $behaviorKeyId"
        }
        $behaviorById.Add($behaviorKeyId, [pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                branchHitCount = [int]$declaration.branchHitCount
                sourceClassificationIds = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($declaration.sourceClassificationIds) -Name "M0-DIA-13 $behaviorKeyId source classification")
                targetModuleIds = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($declaration.targetModuleIds) -Name "M0-DIA-13 $behaviorKeyId target module")
                fixtureIds = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($declaration.fixtureIds) -Name "M0-DIA-13 $behaviorKeyId fixture")
            })
    }
    if ($behaviorById.Count -ne 10) { throw "M0-DIA-13 BehaviorKey count drifted: $($behaviorById.Count)" }
    return [pscustomobject][ordered]@{ sourceDeclarationVocabularyHash = [string]$DeclarationVocabulary.vocabularySetHash; behaviorById = $behaviorById }
}

function Get-DialectBehaviorFixtureContractsPolicySurfaceContext {
    param([Parameter(Mandatory = $true)][object]$PolicySurface)

    if ([string]$PolicySurface.schemaVersion -cne '1.0.0' -or [string]$PolicySurface.workPackage -cne 'M0-DIA-15') {
        throw 'M0-DIA-17 requires M0-DIA-15 policy surface evidence.'
    }
    Assert-DialectBehaviorFixtureContractsHash $PolicySurface.policySurfaceSetHash 'M0-DIA-15 policySurfaceSetHash'
    Assert-DialectBehaviorFixtureContractsHash $PolicySurface.sourceDeclarationVocabularyHash 'M0-DIA-15 sourceDeclarationVocabularyHash'
    if ([string]$PolicySurface.executionStatus -cne 'InProgress' -or [string]$PolicySurface.gateStatus -cne 'Blocked' -or
        [string]$PolicySurface.blockerCode -cne 'EvidenceMissing' -or [string]$PolicySurface.result -cne 'Partial') {
        throw 'M0-DIA-15 evidence status was incorrectly advanced.'
    }
    if ([string]$PolicySurface.currentRuntimeIsolation.status -cne 'Failed' -or
        [string]$PolicySurface.parserVmConsumption.status -cne 'NotConsumed' -or
        [string]$PolicySurface.compatibilityPlanRuntime.status -cne 'NotImplemented' -or
        [string]$PolicySurface.policyManagerRuntime.status -cne 'NotImplemented' -or
        [string]$PolicySurface.runtimeTypeDeclarations.status -cne 'NotImplemented' -or
        [string]$PolicySurface.m1Eligibility.status -cne 'Blocked') {
        throw 'M0-DIA-15 runtime boundary status was incorrectly advanced.'
    }

    $contractByBehaviorKey = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($contract in @($PolicySurface.contracts)) {
        $fields = @('behaviorKeyId', 'consumerContractId', 'decisionOwner', 'portTypeId', 'contractKind', 'branchHitCount', 'currentOwners', 'sourceIntendedOwners', 'sourceClassificationIds', 'sourceFilePaths', 'targetModuleIds', 'fixtureIds', 'sourceBindings', 'contractDraftStatus', 'policyValueStatus', 'runtimeStatus')
        Assert-DialectBehaviorFixtureContractsObjectShape -Value $contract -Required $fields -Allowed $fields -Context 'M0-DIA-15 policy surface contract'
        $behaviorKeyId = [string]$contract.behaviorKeyId
        if ($behaviorKeyId -notmatch '^[a-z][a-z0-9.-]+\.v[0-9]+$') { throw "Invalid M0-DIA-15 BehaviorKey id: $behaviorKeyId" }
        if ($contractByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "Duplicate M0-DIA-15 BehaviorKey: $behaviorKeyId" }
        if ([string]$contract.portTypeId -notmatch '^I[A-Za-z][A-Za-z0-9]*$' -or
            [string]$contract.contractKind -notin @('PolicyDecision', 'BridgeProjection', 'FrozenCatalogContribution') -or
            [string]$contract.contractDraftStatus -cne 'InterfaceDraftOnly' -or
            [string]$contract.policyValueStatus -cne 'Unspecified' -or
            [string]$contract.runtimeStatus -cne 'NotImplemented') {
            throw "M0-DIA-15 contract status drifted: $behaviorKeyId"
        }
        $contractByBehaviorKey.Add($behaviorKeyId, [pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                consumerContractId = [string]$contract.consumerContractId
                decisionOwner = [string]$contract.decisionOwner
                portTypeId = [string]$contract.portTypeId
                contractKind = [string]$contract.contractKind
                branchHitCount = [int]$contract.branchHitCount
                sourceClassificationIds = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($contract.sourceClassificationIds) -Name "M0-DIA-15 $behaviorKeyId source classification")
                sourceFilePaths = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($contract.sourceFilePaths) -Name "M0-DIA-15 $behaviorKeyId source file")
                targetModuleIds = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($contract.targetModuleIds) -Name "M0-DIA-15 $behaviorKeyId target module")
                fixtureIds = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($contract.fixtureIds) -Name "M0-DIA-15 $behaviorKeyId fixture")
            })
    }
    if ($contractByBehaviorKey.Count -ne 10) { throw "M0-DIA-15 port contract count drifted: $($contractByBehaviorKey.Count)" }
    return [pscustomobject][ordered]@{
        sourcePolicySurfaceHash = [string]$PolicySurface.policySurfaceSetHash
        sourceDeclarationVocabularyHash = [string]$PolicySurface.sourceDeclarationVocabularyHash
        contractByBehaviorKey = $contractByBehaviorKey
    }
}

function Get-DialectBehaviorFixtureContractsCatalogContext {
    param([Parameter(Mandatory = $true)][object]$Catalog)

    $fields = @('schemaVersion', 'catalogId', 'catalogVersion', 'sourceWorkPackage', 'sourceDeclarationVocabularyWorkPackage', 'sourcePolicySurfaceWorkPackage', 'sourceDeclarationVocabularyHash', 'sourcePolicySurfaceHash', 'expectedContractCount', 'contracts')
    Assert-DialectBehaviorFixtureContractsObjectShape -Value $Catalog -Required $fields -Allowed $fields -Context 'M0-DIA-17 catalog'
    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or [string]$Catalog.catalogId -cne 'gemuera.dialect-behavior-fixture-contracts' -or
        [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-17' -or [string]$Catalog.sourceDeclarationVocabularyWorkPackage -cne 'M0-DIA-13' -or
        [string]$Catalog.sourcePolicySurfaceWorkPackage -cne 'M0-DIA-15' -or [string]$Catalog.catalogVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
        throw 'Unsupported M0-DIA-17 fixture contract catalog.'
    }
    Assert-DialectBehaviorFixtureContractsHash $Catalog.sourceDeclarationVocabularyHash 'catalog sourceDeclarationVocabularyHash'
    Assert-DialectBehaviorFixtureContractsHash $Catalog.sourcePolicySurfaceHash 'catalog sourcePolicySurfaceHash'
    if ([int]$Catalog.expectedContractCount -ne 10) { throw 'M0-DIA-17 catalog must retain exactly ten fixture contracts.' }

    $contractByBehaviorKey = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($contract in @($Catalog.contracts)) {
        $contractFields = @('behaviorKeyId', 'fixtureId', 'requiredProfileIds', 'requiredEvidenceKinds', 'requiredTraceFacets', 'fixtureContractStatus', 'behaviorEvidenceStatus', 'runtimeStatus', 'interfaceImplementationStatus')
        Assert-DialectBehaviorFixtureContractsObjectShape -Value $contract -Required $contractFields -Allowed $contractFields -Context 'M0-DIA-17 fixture contract catalog entry'
        $behaviorKeyId = [string]$contract.behaviorKeyId
        if ($behaviorKeyId -notmatch '^[a-z][a-z0-9.-]+\.v[0-9]+$') { throw "Invalid M0-DIA-17 BehaviorKey id: $behaviorKeyId" }
        if ([string]$contract.fixtureId -notmatch '^DIA-[A-Z0-9-]+$') { throw "Invalid M0-DIA-17 fixture id: $behaviorKeyId" }
        if ($contractByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "Duplicate M0-DIA-17 fixture contract: $behaviorKeyId" }
        Assert-DialectBehaviorFixtureContractsStringSet -Actual @($contract.requiredProfileIds) -Expected @('v24pure', 'snake') -Context "M0-DIA-17 $behaviorKeyId required profile"
        Assert-DialectBehaviorFixtureContractsStringSet -Actual @($contract.requiredEvidenceKinds) -Expected @('baseline', 'extension', 'undeclared') -Context "M0-DIA-17 $behaviorKeyId required evidence"
        Assert-DialectBehaviorFixtureContractsStringSet -Actual @($contract.requiredTraceFacets) -Expected @('input', 'observable-result', 'error', 'completion', 'effect') -Context "M0-DIA-17 $behaviorKeyId required trace facet"
        if ([string]$contract.fixtureContractStatus -cne 'Planned' -or [string]$contract.behaviorEvidenceStatus -cne 'Uncovered' -or
            [string]$contract.runtimeStatus -cne 'NotImplemented' -or [string]$contract.interfaceImplementationStatus -cne 'BlockedByFixture') {
            throw "M0-DIA-17 fixture contract status drifted: $behaviorKeyId"
        }
        $contractByBehaviorKey.Add($behaviorKeyId, [pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                fixtureId = [string]$contract.fixtureId
                requiredProfileIds = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($contract.requiredProfileIds) -Name "M0-DIA-17 $behaviorKeyId profile")
                requiredEvidenceKinds = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($contract.requiredEvidenceKinds) -Name "M0-DIA-17 $behaviorKeyId evidence kind")
                requiredTraceFacets = @(Get-DialectBehaviorFixtureContractsStringSet -Values @($contract.requiredTraceFacets) -Name "M0-DIA-17 $behaviorKeyId trace facet")
            })
    }
    if ($contractByBehaviorKey.Count -ne [int]$Catalog.expectedContractCount) { throw "M0-DIA-17 catalog contract count drifted: $($contractByBehaviorKey.Count)" }

    $canonicalContracts = New-Object 'System.Collections.Generic.List[object]'
    foreach ($contract in $contractByBehaviorKey.Values) {
        $canonicalContracts.Add([ordered]@{
                behaviorKeyId = $contract.behaviorKeyId; fixtureId = $contract.fixtureId; requiredProfileIds = @($contract.requiredProfileIds)
                requiredEvidenceKinds = @($contract.requiredEvidenceKinds); requiredTraceFacets = @($contract.requiredTraceFacets)
                fixtureContractStatus = 'Planned'; behaviorEvidenceStatus = 'Uncovered'; runtimeStatus = 'NotImplemented'; interfaceImplementationStatus = 'BlockedByFixture'
            })
    }
    $canonicalCatalog = [ordered]@{
        schemaVersion = '1.0.0'; catalogId = [string]$Catalog.catalogId; catalogVersion = [string]$Catalog.catalogVersion; sourceWorkPackage = 'M0-DIA-17'
        sourceDeclarationVocabularyWorkPackage = 'M0-DIA-13'; sourcePolicySurfaceWorkPackage = 'M0-DIA-15'
        sourceDeclarationVocabularyHash = [string]$Catalog.sourceDeclarationVocabularyHash; sourcePolicySurfaceHash = [string]$Catalog.sourcePolicySurfaceHash
        expectedContractCount = [int]$Catalog.expectedContractCount; contracts = $canonicalContracts.ToArray()
    }
    return [pscustomobject][ordered]@{
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = Get-DialectBehaviorFixtureContractsCanonicalHash -Value $canonicalCatalog
        sourceDeclarationVocabularyHash = [string]$Catalog.sourceDeclarationVocabularyHash
        sourcePolicySurfaceHash = [string]$Catalog.sourcePolicySurfaceHash
        contractByBehaviorKey = $contractByBehaviorKey
    }
}

function New-DialectBehaviorFixtureContractReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][object]$DeclarationVocabulary,
        [Parameter(Mandatory = $true, Position = 1)][object]$PolicySurface,
        [Parameter(Mandatory = $true, Position = 2)][object]$Catalog,
        [string]$OutputPath = ''
    )

    $vocabularyContext = Get-DialectBehaviorFixtureContractsVocabularyContext -DeclarationVocabulary $DeclarationVocabulary
    $surfaceContext = Get-DialectBehaviorFixtureContractsPolicySurfaceContext -PolicySurface $PolicySurface
    $catalogContext = Get-DialectBehaviorFixtureContractsCatalogContext -Catalog $Catalog
    if ($surfaceContext.sourceDeclarationVocabularyHash -cne $vocabularyContext.sourceDeclarationVocabularyHash) { throw 'M0-DIA-15 source declaration vocabulary hash drifted.' }
    if ($catalogContext.sourceDeclarationVocabularyHash -cne $vocabularyContext.sourceDeclarationVocabularyHash -or
        $catalogContext.sourcePolicySurfaceHash -cne $surfaceContext.sourcePolicySurfaceHash) {
        throw 'M0-DIA-17 catalog source hash drifted.'
    }

    $contracts = New-Object 'System.Collections.Generic.List[object]'
    foreach ($behaviorKeyId in $vocabularyContext.behaviorById.Keys) {
        if (-not $surfaceContext.contractByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "M0-DIA-17 has no M0-DIA-15 port for BehaviorKey: $behaviorKeyId" }
        if (-not $catalogContext.contractByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "M0-DIA-17 catalog has no fixture contract for BehaviorKey: $behaviorKeyId" }
        $declaration = $vocabularyContext.behaviorById[$behaviorKeyId]
        $surface = $surfaceContext.contractByBehaviorKey[$behaviorKeyId]
        $catalogContract = $catalogContext.contractByBehaviorKey[$behaviorKeyId]
        Assert-DialectBehaviorFixtureContractsStringSet -Actual @($surface.sourceClassificationIds) -Expected @($declaration.sourceClassificationIds) -Context "M0-DIA-17 $behaviorKeyId source classification"
        Assert-DialectBehaviorFixtureContractsStringSet -Actual @($surface.targetModuleIds) -Expected @($declaration.targetModuleIds) -Context "M0-DIA-17 $behaviorKeyId target module"
        Assert-DialectBehaviorFixtureContractsStringSet -Actual @($surface.fixtureIds) -Expected @($declaration.fixtureIds) -Context "M0-DIA-17 $behaviorKeyId source fixture"
        Assert-DialectBehaviorFixtureContractsStringSet -Actual @($surface.fixtureIds) -Expected @($catalogContract.fixtureId) -Context "M0-DIA-17 $behaviorKeyId catalog fixture"
        $contracts.Add([pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                fixtureId = $catalogContract.fixtureId
                requiredProfileIds = @($catalogContract.requiredProfileIds)
                requiredEvidenceKinds = @($catalogContract.requiredEvidenceKinds)
                requiredTraceFacets = @($catalogContract.requiredTraceFacets)
                portTypeId = $surface.portTypeId
                contractKind = $surface.contractKind
                decisionOwner = $surface.decisionOwner
                consumerContractId = $surface.consumerContractId
                sourceClassificationIds = @($surface.sourceClassificationIds)
                sourceFilePaths = @($surface.sourceFilePaths)
                targetModuleIds = @($surface.targetModuleIds)
                fixtureContractStatus = 'Planned'
                behaviorEvidenceStatus = 'Uncovered'
                runtimeStatus = 'NotImplemented'
                interfaceImplementationStatus = 'BlockedByFixture'
            })
    }
    foreach ($behaviorKeyId in $catalogContext.contractByBehaviorKey.Keys) {
        if (-not $vocabularyContext.behaviorById.ContainsKey($behaviorKeyId)) { throw "M0-DIA-17 catalog references unknown BehaviorKey: $behaviorKeyId" }
    }

    $contractCounts = [ordered]@{
        contractCount = [int]$contracts.Count
        plannedFixtureContractCount = [int]$contracts.Count
        behaviorEvidenceCoveredCount = 0
        profilePairRequirementCount = [int]$contracts.Count
    }
    if ($contractCounts.contractCount -ne 10) { throw 'M0-DIA-17 must retain exactly ten BehaviorKey fixture contracts.' }
    $currentRuntimeIsolation = [ordered]@{ status = 'Failed'; reason = 'M0-DIA-17 is an offline fixture-plan report; Program.CoreProfile and legacy registries remain process-wide mutable state.' }
    $parserVmConsumption = [ordered]@{ status = 'NotConsumed'; reason = 'M0-DIA-17 is not read by the legacy Parser or VM.' }
    $compatibilityPlanRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'M0-DIA-17 does not create a CompatibilityPlan, LegacySessionFacade, feature flag, resolver or frozen registry.' }
    $policyManagerRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'M0-DIA-17 does not create a policy manager or select any policy value.' }
    $runtimeTypeDeclarations = [ordered]@{ status = 'NotImplemented'; reason = 'M0-DIA-17 intentionally contains no C# interface, method signature, input DTO or decision enum.' }
    $m1Eligibility = [ordered]@{ status = 'Blocked'; reason = 'Planned fixture requirements do not establish M0 approval, session isolation, generation guards, candidate commit or behavior evidence.' }
    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'; workPackage = 'M0-DIA-17'; sourceDeclarationVocabularyHash = $vocabularyContext.sourceDeclarationVocabularyHash
        sourcePolicySurfaceHash = $surfaceContext.sourcePolicySurfaceHash; catalogId = $catalogContext.catalogId; catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash; contractCounts = $contractCounts; contracts = $contracts.ToArray(); currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption; compatibilityPlanRuntime = $compatibilityPlanRuntime; policyManagerRuntime = $policyManagerRuntime
        runtimeTypeDeclarations = $runtimeTypeDeclarations; m1Eligibility = $m1Eligibility
    }
    $behaviorFixtureContractSetHash = Get-DialectBehaviorFixtureContractsCanonicalHash -Value $setPayload -Depth 100
    $report = [ordered]@{
        schemaVersion = '1.0.0'; workPackage = 'M0-DIA-17'; generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'; gateStatus = 'Blocked'; blockerCode = 'EvidenceMissing'; result = 'Partial'
        sourceDeclarationVocabularyHash = $vocabularyContext.sourceDeclarationVocabularyHash; sourcePolicySurfaceHash = $surfaceContext.sourcePolicySurfaceHash
        catalogId = $catalogContext.catalogId; catalogVersion = $catalogContext.catalogVersion; catalogHash = $catalogContext.catalogHash
        behaviorFixtureContractSetHash = $behaviorFixtureContractSetHash; contractCounts = $contractCounts; contracts = $contracts.ToArray()
        currentRuntimeIsolation = $currentRuntimeIsolation; parserVmConsumption = $parserVmConsumption; compatibilityPlanRuntime = $compatibilityPlanRuntime
        policyManagerRuntime = $policyManagerRuntime; runtimeTypeDeclarations = $runtimeTypeDeclarations; m1Eligibility = $m1Eligibility
        uncovered = @(
            'Each contract is a fixture requirement only. No baseline, extension or undeclared-policy behavior has been captured or approved.',
            'The report deliberately stores no policy value, method signature, input DTO, decision enum, executable payload, module assembly, resolver input or Parser/VM wiring.',
            'Future implementation must attach reproducible v24pure and snake traces for every listed fixture before the corresponding port can leave InterfaceDraftOnly.'
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, (($report | ConvertTo-Json -Depth 100) + "`n"), $script:DialectBehaviorFixtureContractsUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectBehaviorFixtureContractReport
