Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# M0-DIA-13 turns DIA-01 classification provenance into a static declaration
# vocabulary. It is deliberately not a runtime policy registry or resolver.
$script:DialectDeclarationVocabularyUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-DialectDeclarationVocabularySha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-DialectDeclarationVocabularyCanonicalHash {
    param([Parameter(Mandatory = $true)][object]$Value, [int]$Depth = 50)

    return Get-DialectDeclarationVocabularySha256Hex -Bytes $script:DialectDeclarationVocabularyUtf8NoBom.GetBytes(($Value | ConvertTo-Json -Depth $Depth -Compress))
}

function Assert-DialectDeclarationVocabularyHash {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid DIA-13 hash: $Name" }
}

function Assert-DialectDeclarationVocabularySemanticVersion {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Invalid DIA-13 semantic version: $Name" }
}

function Get-DialectDeclarationVocabularyStringSet {
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

function Get-DialectDeclarationVocabularyDistinctStringSet {
    param(
        [object[]]$Values,
        [Parameter(Mandatory = $true)][string]$Name,
        [bool]$AllowEmpty = $false
    )

    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($value in @($Values)) {
        if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { throw "$Name contains an empty value." }
        [void]$set.Add([string]$value)
    }
    if (-not $AllowEmpty -and $set.Count -eq 0) { throw "$Name must not be empty." }
    $result = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in $set) { $result.Add($item) }
    return $result.ToArray()
}

function Assert-DialectDeclarationVocabularyStringSet {
    param(
        [object[]]$Actual,
        [object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Context,
        [bool]$AllowEmpty = $false
    )

    $actualValues = @(Get-DialectDeclarationVocabularyStringSet -Values $Actual -Name "$Context actual values" -AllowEmpty $AllowEmpty)
    $expectedValues = @(Get-DialectDeclarationVocabularyStringSet -Values $Expected -Name "$Context expected values" -AllowEmpty $AllowEmpty)
    if ($actualValues.Count -ne $expectedValues.Count) { throw "$Context count drifted: expected $($expectedValues.Count), actual $($actualValues.Count)." }
    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -cne $expectedValues[$index]) { throw "$Context value drifted: expected $($expectedValues[$index]), actual $($actualValues[$index])." }
    }
}

function Assert-DialectDeclarationVocabularyObjectShape {
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

function Get-DialectDeclarationVocabularyKey {
    param([Parameter(Mandatory = $true)][string]$Kind, [Parameter(Mandatory = $true)][string]$Id)

    return "$Kind|$Id"
}

function Get-DialectDeclarationVocabularyInventoryContext {
    param([Parameter(Mandatory = $true)][object]$Inventory)

    if ([string]$Inventory.schemaVersion -cne '1.0.0' -or [string]$Inventory.workPackage -cne 'M0-DIA-01') {
        throw 'DIA-13 requires M0-DIA-01 inventory evidence.'
    }
    if ($null -ne $Inventory.PSObject.Properties['compatibilityPlanRuntime']) {
        throw 'DIA-13 requires M0-DIA-01 inventory evidence, not a runtime compatibility plan.'
    }
    Assert-DialectDeclarationVocabularyHash $Inventory.canonicalHash 'M0-DIA-01 canonicalHash'
    if ([string]$Inventory.executionStatus -cne 'InProgress' -or [string]$Inventory.gateStatus -cne 'Blocked' -or [string]$Inventory.blockerCode -cne 'EvidenceMissing' -or [string]$Inventory.result -cne 'Partial') {
        throw 'DIA-01 evidence status was incorrectly advanced.'
    }
    if ([int]$Inventory.unmappedHitCount -ne 0) { throw 'DIA-01 inventory contains unmapped branch hits.' }
    $branchHits = @($Inventory.branchHits)
    if ($branchHits.Count -ne [int]$Inventory.branchHitCount -or $branchHits.Count -eq 0) { throw 'DIA-01 branch hit count is invalid.' }

    $groupByKey = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $classificationIds = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($branchHit in $branchHits) {
        $classificationId = [string]$branchHit.classificationId
        $targetModule = [string]$branchHit.targetModule
        $fixtureId = [string]$branchHit.fixtureId
        $behaviorKey = [string]$branchHit.behaviorKey
        $capabilityId = [string]$branchHit.capabilityId
        if ([string]::IsNullOrWhiteSpace($classificationId) -or [string]::IsNullOrWhiteSpace($targetModule) -or [string]::IsNullOrWhiteSpace($fixtureId)) {
            throw 'DIA-01 branch hit is missing declaration provenance.'
        }
        if ([string]::IsNullOrWhiteSpace($behaviorKey) -and [string]::IsNullOrWhiteSpace($capabilityId)) {
            throw "DIA-01 branch hit has no BehaviorKey or CapabilityId: $classificationId"
        }
        if (-not [string]::IsNullOrWhiteSpace($behaviorKey) -and -not [string]::IsNullOrWhiteSpace($capabilityId)) {
            throw "DIA-01 branch hit maps to both BehaviorKey and CapabilityId: $classificationId"
        }
        $kind = if (-not [string]::IsNullOrWhiteSpace($behaviorKey)) { 'BehaviorKey' } else { 'CapabilityId' }
        $id = if ($kind -ceq 'BehaviorKey') { $behaviorKey } else { $capabilityId }
        $key = Get-DialectDeclarationVocabularyKey -Kind $kind -Id $id
        if (-not $groupByKey.ContainsKey($key)) {
            $groupByKey.Add($key, [pscustomobject][ordered]@{
                    kind = $kind
                    id = $id
                    branchHitCount = 0
                    sourceClassificationIds = (New-Object 'System.Collections.Generic.List[string]')
                    targetModuleIds = (New-Object 'System.Collections.Generic.List[string]')
                    fixtureIds = (New-Object 'System.Collections.Generic.List[string]')
                })
        }
        $group = $groupByKey[$key]
        $group.branchHitCount++
        $group.sourceClassificationIds.Add($classificationId)
        $group.targetModuleIds.Add($targetModule)
        $group.fixtureIds.Add($fixtureId)
        [void]$classificationIds.Add($classificationId)
    }

    $groups = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($key in $groupByKey.Keys) {
        $group = $groupByKey[$key]
        $groups.Add($key, [pscustomobject][ordered]@{
                kind = $group.kind
                id = $group.id
                branchHitCount = [int]$group.branchHitCount
                sourceClassificationIds = @(Get-DialectDeclarationVocabularyDistinctStringSet -Values $group.sourceClassificationIds.ToArray() -Name "DIA-01 $($group.kind) $($group.id) classification")
                targetModuleIds = @(Get-DialectDeclarationVocabularyDistinctStringSet -Values $group.targetModuleIds.ToArray() -Name "DIA-01 $($group.kind) $($group.id) target module")
                fixtureIds = @(Get-DialectDeclarationVocabularyDistinctStringSet -Values $group.fixtureIds.ToArray() -Name "DIA-01 $($group.kind) $($group.id) fixture")
            })
    }
    return [pscustomobject][ordered]@{
        sourceInventoryHash = [string]$Inventory.canonicalHash
        branchHitCount = $branchHits.Count
        sourceClassificationIds = @($classificationIds)
        groups = $groups
    }
}

function Get-DialectDeclarationVocabularyCompatibilityPackContext {
    param([Parameter(Mandatory = $true)][object]$CompatibilityPack)

    if ([string]$CompatibilityPack.schemaVersion -cne '1.0.0' -or [string]$CompatibilityPack.workPackage -cne 'M0-DIA-12') {
        throw 'DIA-13 requires M0-DIA-12 CompatibilityPack evidence.'
    }
    Assert-DialectDeclarationVocabularyHash $CompatibilityPack.compatibilityPackSetHash 'M0-DIA-12 compatibilityPackSetHash'
    if ([string]$CompatibilityPack.executionStatus -cne 'InProgress' -or [string]$CompatibilityPack.gateStatus -cne 'Blocked' -or [string]$CompatibilityPack.blockerCode -cne 'EvidenceMissing' -or [string]$CompatibilityPack.result -cne 'Partial') {
        throw 'DIA-12 evidence status was incorrectly advanced.'
    }
    if ([string]$CompatibilityPack.currentRuntimeIsolation.status -cne 'Failed') { throw 'DIA-12 current runtime isolation must remain Failed.' }
    if ([string]$CompatibilityPack.parserVmConsumption.status -cne 'NotConsumed') { throw 'DIA-12 parser/VM consumption must remain NotConsumed.' }
    if ([string]$CompatibilityPack.compatibilityPlanRuntime.status -cne 'NotImplemented') { throw 'DIA-12 runtime compatibility plan status must remain NotImplemented.' }
    if ([string]$CompatibilityPack.resolverRuntime.status -cne 'NotImplemented') { throw 'DIA-12 runtime resolver status must remain NotImplemented.' }
    if ([string]$CompatibilityPack.m1Eligibility.status -cne 'Blocked') { throw 'DIA-12 M1 eligibility must remain Blocked.' }
    if ($null -eq $CompatibilityPack.declarationPolicy.PSObject.Properties['allowedCapabilityIds']) { throw 'DIA-12 declaration policy is missing allowedCapabilityIds.' }
    $allowedCapabilityIds = @(Get-DialectDeclarationVocabularyStringSet -Values @($CompatibilityPack.declarationPolicy.allowedCapabilityIds) -Name 'DIA-12 allowed capability id' -AllowEmpty $true)
    if ($allowedCapabilityIds.Count -ne 0) { throw 'DIA-12 must not expose runtime capability ids while DIA-13 vocabulary remains static.' }
    $packCount = 0
    foreach ($pack in @($CompatibilityPack.packs)) {
        $packCount++
        if ($null -eq $pack.PSObject.Properties['requiredCapabilityIds'] -or $null -eq $pack.PSObject.Properties['optionalCapabilityIds']) {
            throw 'DIA-12 CompatibilityPack is missing capability declaration fields.'
        }
        $requiredCapabilityIds = @(Get-DialectDeclarationVocabularyStringSet -Values @($pack.requiredCapabilityIds) -Name "DIA-12 pack $($pack.packId) required capability" -AllowEmpty $true)
        $optionalCapabilityIds = @(Get-DialectDeclarationVocabularyStringSet -Values @($pack.optionalCapabilityIds) -Name "DIA-12 pack $($pack.packId) optional capability" -AllowEmpty $true)
        if ($requiredCapabilityIds.Count -ne 0 -or $optionalCapabilityIds.Count -ne 0) { throw 'DIA-12 must not expose runtime capability ids in static packs.' }
    }
    if ($packCount -ne 2) { throw 'DIA-12 must retain exactly two static CompatibilityPacks.' }
    return [pscustomobject][ordered]@{
        sourceCompatibilityPackHash = [string]$CompatibilityPack.compatibilityPackSetHash
        packCount = $packCount
        allowedCapabilityIds = @()
    }
}

function Get-DialectDeclarationVocabularyCatalogContext {
    param([Parameter(Mandatory = $true)][object]$Catalog)

    $catalogFields = @('schemaVersion', 'catalogId', 'catalogVersion', 'sourceWorkPackage', 'sourceInventoryWorkPackage', 'sourceCompatibilityPackWorkPackage', 'sourceInventoryHash', 'sourceCompatibilityPackHash', 'expectedSourceClassificationCount', 'expectedBranchHitCount', 'declarations')
    Assert-DialectDeclarationVocabularyObjectShape -Value $Catalog -Required $catalogFields -Allowed $catalogFields -Context 'DIA-13 catalog'
    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or
        [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-13' -or
        [string]$Catalog.sourceInventoryWorkPackage -cne 'M0-DIA-01' -or
        [string]$Catalog.sourceCompatibilityPackWorkPackage -cne 'M0-DIA-12' -or
        [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported DIA-13 declaration vocabulary catalog.'
    }
    Assert-DialectDeclarationVocabularySemanticVersion $Catalog.catalogVersion 'catalogVersion'
    Assert-DialectDeclarationVocabularyHash $Catalog.sourceInventoryHash 'catalog sourceInventoryHash'
    Assert-DialectDeclarationVocabularyHash $Catalog.sourceCompatibilityPackHash 'catalog sourceCompatibilityPackHash'
    if ([int]$Catalog.expectedSourceClassificationCount -le 0 -or [int]$Catalog.expectedBranchHitCount -le 0) { throw 'DIA-13 catalog expected source counts are invalid.' }

    $declarationByKey = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($declaration in @($Catalog.declarations)) {
        $declarationFields = @('kind', 'id', 'expectedBranchHitCount', 'sourceClassificationIds', 'targetModuleIds', 'fixtureIds', 'declarationStatus', 'currentPackEligibility')
        Assert-DialectDeclarationVocabularyObjectShape -Value $declaration -Required $declarationFields -Allowed $declarationFields -Context 'DIA-13 declaration catalog entry'
        $kind = [string]$declaration.kind
        $id = [string]$declaration.id
        if ($kind -cne 'BehaviorKey' -and $kind -cne 'CapabilityId') { throw "Invalid DIA-13 declaration kind: $kind" }
        if ($id -notmatch '^[a-z][a-z0-9.\-]*\.v[0-9]+$') { throw "Invalid DIA-13 declaration id: $id" }
        if ([int]$declaration.expectedBranchHitCount -le 0) { throw "Invalid DIA-13 expected branch hit count: $kind $id" }
        if ([string]$declaration.declarationStatus -cne 'StaticCandidate' -or [string]$declaration.currentPackEligibility -cne 'NotEligible') {
            throw "DIA-13 declaration incorrectly advances runtime eligibility: $kind $id"
        }
        $key = Get-DialectDeclarationVocabularyKey -Kind $kind -Id $id
        if ($declarationByKey.ContainsKey($key)) { throw "Duplicate DIA-13 declaration catalog entry: $kind $id" }
        $declarationByKey.Add($key, [pscustomobject][ordered]@{
                kind = $kind
                id = $id
                expectedBranchHitCount = [int]$declaration.expectedBranchHitCount
                sourceClassificationIds = @(Get-DialectDeclarationVocabularyStringSet -Values @($declaration.sourceClassificationIds) -Name "DIA-13 $kind $id source classification")
                targetModuleIds = @(Get-DialectDeclarationVocabularyStringSet -Values @($declaration.targetModuleIds) -Name "DIA-13 $kind $id target module")
                fixtureIds = @(Get-DialectDeclarationVocabularyStringSet -Values @($declaration.fixtureIds) -Name "DIA-13 $kind $id fixture")
                declarationStatus = 'StaticCandidate'
                currentPackEligibility = 'NotEligible'
            })
    }
    if ($declarationByKey.Count -eq 0) { throw 'DIA-13 catalog must contain declarations.' }
    $canonicalDeclarations = New-Object 'System.Collections.Generic.List[object]'
    foreach ($declaration in $declarationByKey.Values) {
        $canonicalDeclarations.Add([ordered]@{
                kind = $declaration.kind; id = $declaration.id; expectedBranchHitCount = $declaration.expectedBranchHitCount
                sourceClassificationIds = @($declaration.sourceClassificationIds); targetModuleIds = @($declaration.targetModuleIds)
                fixtureIds = @($declaration.fixtureIds); declarationStatus = $declaration.declarationStatus
                currentPackEligibility = $declaration.currentPackEligibility
            })
    }
    $canonicalCatalog = [ordered]@{
        schemaVersion = '1.0.0'; catalogId = [string]$Catalog.catalogId; catalogVersion = [string]$Catalog.catalogVersion
        sourceWorkPackage = 'M0-DIA-13'; sourceInventoryWorkPackage = 'M0-DIA-01'; sourceCompatibilityPackWorkPackage = 'M0-DIA-12'
        sourceInventoryHash = [string]$Catalog.sourceInventoryHash; sourceCompatibilityPackHash = [string]$Catalog.sourceCompatibilityPackHash
        expectedSourceClassificationCount = [int]$Catalog.expectedSourceClassificationCount; expectedBranchHitCount = [int]$Catalog.expectedBranchHitCount
        declarations = $canonicalDeclarations.ToArray()
    }
    return [pscustomobject][ordered]@{
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = Get-DialectDeclarationVocabularyCanonicalHash -Value $canonicalCatalog
        sourceInventoryHash = [string]$Catalog.sourceInventoryHash
        sourceCompatibilityPackHash = [string]$Catalog.sourceCompatibilityPackHash
        expectedSourceClassificationCount = [int]$Catalog.expectedSourceClassificationCount
        expectedBranchHitCount = [int]$Catalog.expectedBranchHitCount
        declarationByKey = $declarationByKey
    }
}

function New-DialectDeclarationVocabularyReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][object]$Inventory,
        [Parameter(Mandatory = $true, Position = 1)][object]$CompatibilityPack,
        [Parameter(Mandatory = $true, Position = 2)][object]$Catalog,
        [string]$OutputPath = ''
    )

    $inventoryContext = Get-DialectDeclarationVocabularyInventoryContext -Inventory $Inventory
    $compatibilityPackContext = Get-DialectDeclarationVocabularyCompatibilityPackContext -CompatibilityPack $CompatibilityPack
    $catalogContext = Get-DialectDeclarationVocabularyCatalogContext -Catalog $Catalog
    if ($catalogContext.sourceInventoryHash -cne $inventoryContext.sourceInventoryHash) { throw 'DIA-13 source inventory hash drifted.' }
    if ($catalogContext.sourceCompatibilityPackHash -cne $compatibilityPackContext.sourceCompatibilityPackHash) { throw 'DIA-13 source CompatibilityPack hash drifted.' }
    if ($catalogContext.expectedSourceClassificationCount -ne $inventoryContext.sourceClassificationIds.Count) {
        throw "DIA-13 source classification count drifted: expected $($catalogContext.expectedSourceClassificationCount), actual $($inventoryContext.sourceClassificationIds.Count)."
    }
    if ($catalogContext.expectedBranchHitCount -ne $inventoryContext.branchHitCount) {
        throw "DIA-13 branch hit count drifted: expected $($catalogContext.expectedBranchHitCount), actual $($inventoryContext.branchHitCount)."
    }
    if ($catalogContext.declarationByKey.Count -ne $inventoryContext.groups.Count) {
        throw "DIA-13 declaration count drifted: catalog $($catalogContext.declarationByKey.Count), source $($inventoryContext.groups.Count)."
    }

    $seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $declarations = New-Object 'System.Collections.Generic.List[object]'
    foreach ($key in $catalogContext.declarationByKey.Keys) {
        $catalogDeclaration = $catalogContext.declarationByKey[$key]
        if (-not $inventoryContext.groups.ContainsKey($key)) {
            throw "DIA-13 catalog references unknown static declaration: $($catalogDeclaration.kind) $($catalogDeclaration.id)"
        }
        $sourceDeclaration = $inventoryContext.groups[$key]
        if ($catalogDeclaration.expectedBranchHitCount -ne $sourceDeclaration.branchHitCount) {
            throw "DIA-13 branch hit count drifted for $($catalogDeclaration.kind) $($catalogDeclaration.id): expected $($catalogDeclaration.expectedBranchHitCount), actual $($sourceDeclaration.branchHitCount)."
        }
        Assert-DialectDeclarationVocabularyStringSet -Actual @($catalogDeclaration.sourceClassificationIds) -Expected @($sourceDeclaration.sourceClassificationIds) -Context "DIA-13 $($catalogDeclaration.kind) $($catalogDeclaration.id) source classification set"
        Assert-DialectDeclarationVocabularyStringSet -Actual @($catalogDeclaration.targetModuleIds) -Expected @($sourceDeclaration.targetModuleIds) -Context "DIA-13 $($catalogDeclaration.kind) $($catalogDeclaration.id) target module set"
        Assert-DialectDeclarationVocabularyStringSet -Actual @($catalogDeclaration.fixtureIds) -Expected @($sourceDeclaration.fixtureIds) -Context "DIA-13 $($catalogDeclaration.kind) $($catalogDeclaration.id) fixture set"
        $declarations.Add([pscustomobject][ordered]@{
                kind = $sourceDeclaration.kind
                id = $sourceDeclaration.id
                branchHitCount = [int]$sourceDeclaration.branchHitCount
                sourceClassificationIds = @($sourceDeclaration.sourceClassificationIds)
                targetModuleIds = @($sourceDeclaration.targetModuleIds)
                fixtureIds = @($sourceDeclaration.fixtureIds)
                declarationStatus = 'StaticCandidate'
                currentPackEligibility = 'NotEligible'
                runtimeStatus = 'NotImplemented'
            })
        [void]$seen.Add($key)
    }
    foreach ($key in $inventoryContext.groups.Keys) {
        if (-not $seen.Contains($key)) { throw "DIA-13 source declaration has no catalog entry: $key" }
    }
    $behaviorKeyCount = @($declarations | Where-Object kind -ceq 'BehaviorKey').Count
    $capabilityIdCount = @($declarations | Where-Object kind -ceq 'CapabilityId').Count
    $currentCompatibilityPackExposure = [ordered]@{
        status = 'NoRuntimeCapabilitiesDeclared'
        packCount = [int]$compatibilityPackContext.packCount
        allowedCapabilityIds = @()
        reason = 'DIA-12 static CompatibilityPacks intentionally declare no runtime capability ids; DIA-13 vocabulary candidates cannot be selected yet.'
    }
    $declarationCounts = [ordered]@{
        behaviorKeyCount = $behaviorKeyCount
        capabilityIdCount = $capabilityIdCount
        sourceClassificationCount = [int]$inventoryContext.sourceClassificationIds.Count
        branchHitCount = [int]$inventoryContext.branchHitCount
    }
    $currentRuntimeIsolation = [ordered]@{ status = 'Failed'; reason = 'DIA-13 only aggregates source classification provenance and does not isolate legacy static profile or registry state.' }
    $parserVmConsumption = [ordered]@{ status = 'NotConsumed'; reason = 'DIA-13 is an offline declaration vocabulary report and is not read by the legacy Parser or VM.' }
    $compatibilityPlanRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-13 does not create a runtime CompatibilityPlan, policy manager, LegacySessionFacade, feature flag or frozen registry.' }
    $resolverRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-13 does not inspect content, calculate fingerprints, parse manifests or resolve a runtime candidate.' }
    $m1Eligibility = [ordered]@{ status = 'Blocked'; reason = 'Static vocabulary provenance does not establish M1 session isolation or runtime capability binding.' }
    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'; workPackage = 'M0-DIA-13'
        sourceInventoryHash = $inventoryContext.sourceInventoryHash; sourceCompatibilityPackHash = $compatibilityPackContext.sourceCompatibilityPackHash
        catalogId = $catalogContext.catalogId; catalogVersion = $catalogContext.catalogVersion; catalogHash = $catalogContext.catalogHash
        declarationCounts = $declarationCounts; currentCompatibilityPackExposure = $currentCompatibilityPackExposure
        declarations = $declarations.ToArray(); currentRuntimeIsolation = $currentRuntimeIsolation; parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime; resolverRuntime = $resolverRuntime; m1Eligibility = $m1Eligibility
    }
    $vocabularySetHash = Get-DialectDeclarationVocabularyCanonicalHash -Value $setPayload -Depth 60
    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-13'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourceInventoryHash = $inventoryContext.sourceInventoryHash
        sourceCompatibilityPackHash = $compatibilityPackContext.sourceCompatibilityPackHash
        catalogId = $catalogContext.catalogId
        catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash
        vocabularySetHash = $vocabularySetHash
        declarationCounts = $declarationCounts
        currentCompatibilityPackExposure = $currentCompatibilityPackExposure
        declarations = $declarations.ToArray()
        currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime
        resolverRuntime = $resolverRuntime
        m1Eligibility = $m1Eligibility
        uncovered = @(
            'DIA-13 records only static source provenance. It does not prove any BehaviorKey default, error path, completion mode, effect, policy value or script-observable runtime capability.',
            'No declaration is eligible for the current DIA-12 static CompatibilityPacks; content identity, manifest parsing, user pinning, save profile and fixture binding remain absent.',
            'No runtime CompatibilityPlan, resolver, policy manager, LegacySessionFacade, feature flag, Parser/VM consumption, D2 frozen registry, game run, APK or device evidence is introduced.'
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, (($report | ConvertTo-Json -Depth 60) + "`n"), $script:DialectDeclarationVocabularyUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectDeclarationVocabularyReport
