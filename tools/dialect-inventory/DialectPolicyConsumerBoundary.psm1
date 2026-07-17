Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# M0-DIA-14 is an offline audit of the future narrow consumer boundary for
# each DIA-13 BehaviorKey.  It intentionally does not create a runtime policy
# object, resolver, CompatibilityPlan, or Parser/VM integration.
$script:DialectPolicyConsumerBoundaryUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-DialectPolicyConsumerBoundarySha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-DialectPolicyConsumerBoundaryCanonicalHash {
    param([Parameter(Mandatory = $true)][object]$Value, [int]$Depth = 80)

    return Get-DialectPolicyConsumerBoundarySha256Hex -Bytes $script:DialectPolicyConsumerBoundaryUtf8NoBom.GetBytes(($Value | ConvertTo-Json -Depth $Depth -Compress))
}

function Assert-DialectPolicyConsumerBoundaryHash {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid DIA-14 hash: $Name" }
}

function Assert-DialectPolicyConsumerBoundarySemanticVersion {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Invalid DIA-14 semantic version: $Name" }
}

function Get-DialectPolicyConsumerBoundaryStringSet {
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

function Get-DialectPolicyConsumerBoundaryDistinctStringSet {
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

function Assert-DialectPolicyConsumerBoundaryStringSet {
    param(
        [object[]]$Actual,
        [object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Context,
        [bool]$AllowEmpty = $false
    )

    $actualValues = @(Get-DialectPolicyConsumerBoundaryStringSet -Values $Actual -Name "$Context actual values" -AllowEmpty $AllowEmpty)
    $expectedValues = @(Get-DialectPolicyConsumerBoundaryStringSet -Values $Expected -Name "$Context expected values" -AllowEmpty $AllowEmpty)
    if ($actualValues.Count -ne $expectedValues.Count) { throw "$Context count drifted: expected $($expectedValues.Count), actual $($actualValues.Count)." }
    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -cne $expectedValues[$index]) { throw "$Context value drifted: expected $($expectedValues[$index]), actual $($actualValues[$index])." }
    }
}

function Assert-DialectPolicyConsumerBoundaryObjectShape {
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

function Get-DialectPolicyConsumerBoundaryInventoryContext {
    param([Parameter(Mandatory = $true)][object]$Inventory)

    if ([string]$Inventory.schemaVersion -cne '1.0.0' -or [string]$Inventory.workPackage -cne 'M0-DIA-01') {
        throw 'DIA-14 requires M0-DIA-01 inventory evidence.'
    }
    if ($null -ne $Inventory.PSObject.Properties['compatibilityPlanRuntime']) {
        throw 'DIA-14 requires M0-DIA-01 inventory evidence, not a runtime compatibility plan.'
    }
    Assert-DialectPolicyConsumerBoundaryHash $Inventory.canonicalHash 'M0-DIA-01 canonicalHash'
    if ([string]$Inventory.executionStatus -cne 'InProgress' -or [string]$Inventory.gateStatus -cne 'Blocked' -or [string]$Inventory.blockerCode -cne 'EvidenceMissing' -or [string]$Inventory.result -cne 'Partial') {
        throw 'DIA-01 evidence status was incorrectly advanced.'
    }
    if ([int]$Inventory.unmappedHitCount -ne 0) { throw 'DIA-01 inventory contains unmapped branch hits.' }

    $behaviorById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($branchHit in @($Inventory.branchHits)) {
        $behaviorKeyId = [string]$branchHit.behaviorKey
        if ([string]::IsNullOrWhiteSpace($behaviorKeyId)) { continue }
        $classificationId = [string]$branchHit.classificationId
        $sourceFile = [string]$branchHit.sourceFile
        $currentOwner = [string]$branchHit.currentOwner
        $intendedOwner = [string]$branchHit.intendedOwner
        $targetModule = [string]$branchHit.targetModule
        $fixtureId = [string]$branchHit.fixtureId
        if ([string]::IsNullOrWhiteSpace($classificationId) -or [string]::IsNullOrWhiteSpace($sourceFile) -or
            [string]::IsNullOrWhiteSpace($currentOwner) -or [string]::IsNullOrWhiteSpace($intendedOwner) -or
            [string]::IsNullOrWhiteSpace($targetModule) -or [string]::IsNullOrWhiteSpace($fixtureId)) {
            throw "DIA-01 BehaviorKey provenance is incomplete: $behaviorKeyId"
        }
        if (-not $behaviorById.ContainsKey($behaviorKeyId)) {
            $behaviorById.Add($behaviorKeyId, [pscustomobject][ordered]@{
                    classificationById = (New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal))
                })
        }
        $behavior = $behaviorById[$behaviorKeyId]
        if (-not $behavior.classificationById.ContainsKey($classificationId)) {
            $behavior.classificationById.Add($classificationId, [pscustomobject][ordered]@{
                    branchHitCount = 0
                    sourceFiles = (New-Object 'System.Collections.Generic.List[string]')
                    currentOwners = (New-Object 'System.Collections.Generic.List[string]')
                    intendedOwners = (New-Object 'System.Collections.Generic.List[string]')
                    targetModules = (New-Object 'System.Collections.Generic.List[string]')
                    fixtureIds = (New-Object 'System.Collections.Generic.List[string]')
                })
        }
        $classification = $behavior.classificationById[$classificationId]
        $classification.branchHitCount++
        $classification.sourceFiles.Add($sourceFile)
        $classification.currentOwners.Add($currentOwner)
        $classification.intendedOwners.Add($intendedOwner)
        $classification.targetModules.Add($targetModule)
        $classification.fixtureIds.Add($fixtureId)
    }

    if ($behaviorById.Count -eq 0) { throw 'DIA-01 inventory contains no BehaviorKey branch hits.' }
    $canonicalBehaviorById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($behaviorKeyId in $behaviorById.Keys) {
        $rawBehavior = $behaviorById[$behaviorKeyId]
        $canonicalClassificationById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
        $allSourceFiles = New-Object 'System.Collections.Generic.List[string]'
        $allCurrentOwners = New-Object 'System.Collections.Generic.List[string]'
        $allIntendedOwners = New-Object 'System.Collections.Generic.List[string]'
        $allTargetModules = New-Object 'System.Collections.Generic.List[string]'
        $allFixtureIds = New-Object 'System.Collections.Generic.List[string]'
        $branchHitCount = 0
        foreach ($classificationId in $rawBehavior.classificationById.Keys) {
            $rawClassification = $rawBehavior.classificationById[$classificationId]
            $sourceFilePaths = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $rawClassification.sourceFiles.ToArray() -Name "DIA-01 $behaviorKeyId $classificationId source file")
            $currentOwners = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $rawClassification.currentOwners.ToArray() -Name "DIA-01 $behaviorKeyId $classificationId current owner")
            $intendedOwners = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $rawClassification.intendedOwners.ToArray() -Name "DIA-01 $behaviorKeyId $classificationId intended owner")
            $targetModuleIds = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $rawClassification.targetModules.ToArray() -Name "DIA-01 $behaviorKeyId $classificationId target module")
            $fixtureIds = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $rawClassification.fixtureIds.ToArray() -Name "DIA-01 $behaviorKeyId $classificationId fixture")
            $canonicalClassificationById.Add($classificationId, [pscustomobject][ordered]@{
                    classificationId = $classificationId
                    branchHitCount = [int]$rawClassification.branchHitCount
                    sourceFilePaths = @($sourceFilePaths)
                    currentOwners = @($currentOwners)
                    intendedOwners = @($intendedOwners)
                    targetModuleIds = @($targetModuleIds)
                    fixtureIds = @($fixtureIds)
                })
            $branchHitCount += [int]$rawClassification.branchHitCount
            foreach ($value in $sourceFilePaths) { $allSourceFiles.Add($value) }
            foreach ($value in $currentOwners) { $allCurrentOwners.Add($value) }
            foreach ($value in $intendedOwners) { $allIntendedOwners.Add($value) }
            foreach ($value in $targetModuleIds) { $allTargetModules.Add($value) }
            foreach ($value in $fixtureIds) { $allFixtureIds.Add($value) }
        }
        $canonicalBehaviorById.Add($behaviorKeyId, [pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                branchHitCount = $branchHitCount
                sourceClassificationIds = @($canonicalClassificationById.Keys)
                sourceFilePaths = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $allSourceFiles.ToArray() -Name "DIA-01 $behaviorKeyId source file")
                currentOwners = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $allCurrentOwners.ToArray() -Name "DIA-01 $behaviorKeyId current owner")
                sourceIntendedOwners = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $allIntendedOwners.ToArray() -Name "DIA-01 $behaviorKeyId intended owner")
                targetModuleIds = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $allTargetModules.ToArray() -Name "DIA-01 $behaviorKeyId target module")
                fixtureIds = @(Get-DialectPolicyConsumerBoundaryDistinctStringSet -Values $allFixtureIds.ToArray() -Name "DIA-01 $behaviorKeyId fixture")
                classificationById = $canonicalClassificationById
            })
    }
    return [pscustomobject][ordered]@{
        sourceInventoryHash = [string]$Inventory.canonicalHash
        behaviorById = $canonicalBehaviorById
    }
}

function Get-DialectPolicyConsumerBoundaryVocabularyContext {
    param([Parameter(Mandatory = $true)][object]$DeclarationVocabulary)

    if ([string]$DeclarationVocabulary.schemaVersion -cne '1.0.0' -or [string]$DeclarationVocabulary.workPackage -cne 'M0-DIA-13') {
        throw 'DIA-14 requires M0-DIA-13 declaration vocabulary evidence.'
    }
    Assert-DialectPolicyConsumerBoundaryHash $DeclarationVocabulary.vocabularySetHash 'M0-DIA-13 vocabularySetHash'
    if ([string]$DeclarationVocabulary.executionStatus -cne 'InProgress' -or [string]$DeclarationVocabulary.gateStatus -cne 'Blocked' -or [string]$DeclarationVocabulary.blockerCode -cne 'EvidenceMissing' -or [string]$DeclarationVocabulary.result -cne 'Partial') {
        throw 'DIA-13 evidence status was incorrectly advanced.'
    }
    if ($null -eq $DeclarationVocabulary.PSObject.Properties['currentCompatibilityPackExposure']) { throw 'DIA-13 is missing current CompatibilityPack exposure.' }
    $exposure = $DeclarationVocabulary.currentCompatibilityPackExposure
    if ([string]$exposure.status -cne 'NoRuntimeCapabilitiesDeclared' -or $null -eq $exposure.PSObject.Properties['allowedCapabilityIds']) {
        throw 'DIA-13 must retain no runtime capability exposure.'
    }
    $allowedCapabilityIds = @(Get-DialectPolicyConsumerBoundaryStringSet -Values @($exposure.allowedCapabilityIds) -Name 'DIA-13 allowed capability id' -AllowEmpty $true)
    if ($allowedCapabilityIds.Count -ne 0) { throw 'DIA-13 must retain no runtime capability exposure.' }
    if ([string]$DeclarationVocabulary.currentRuntimeIsolation.status -cne 'Failed' -or
        [string]$DeclarationVocabulary.parserVmConsumption.status -cne 'NotConsumed' -or
        [string]$DeclarationVocabulary.compatibilityPlanRuntime.status -cne 'NotImplemented' -or
        [string]$DeclarationVocabulary.resolverRuntime.status -cne 'NotImplemented' -or
        [string]$DeclarationVocabulary.m1Eligibility.status -cne 'Blocked') {
        throw 'DIA-13 runtime boundary status was incorrectly advanced.'
    }

    $behaviorById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($declaration in @($DeclarationVocabulary.declarations)) {
        if ([string]$declaration.kind -cne 'BehaviorKey') { continue }
        $behaviorKeyId = [string]$declaration.id
        if ([string]::IsNullOrWhiteSpace($behaviorKeyId)) { throw 'DIA-13 BehaviorKey declaration is missing id.' }
        if ([string]$declaration.declarationStatus -cne 'StaticCandidate' -or [string]$declaration.currentPackEligibility -cne 'NotEligible' -or [string]$declaration.runtimeStatus -cne 'NotImplemented') {
            throw "DIA-13 BehaviorKey declaration incorrectly advances runtime status: $behaviorKeyId"
        }
        if ($behaviorById.ContainsKey($behaviorKeyId)) { throw "Duplicate DIA-13 BehaviorKey declaration: $behaviorKeyId" }
        $behaviorById.Add($behaviorKeyId, [pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                branchHitCount = [int]$declaration.branchHitCount
                sourceClassificationIds = @(Get-DialectPolicyConsumerBoundaryStringSet -Values @($declaration.sourceClassificationIds) -Name "DIA-13 $behaviorKeyId source classification")
                targetModuleIds = @(Get-DialectPolicyConsumerBoundaryStringSet -Values @($declaration.targetModuleIds) -Name "DIA-13 $behaviorKeyId target module")
                fixtureIds = @(Get-DialectPolicyConsumerBoundaryStringSet -Values @($declaration.fixtureIds) -Name "DIA-13 $behaviorKeyId fixture")
            })
    }
    if ($behaviorById.Count -ne 10 -or [int]$DeclarationVocabulary.declarationCounts.behaviorKeyCount -ne 10) {
        throw 'DIA-13 must retain exactly ten static BehaviorKey declarations.'
    }
    return [pscustomobject][ordered]@{
        sourceDeclarationVocabularyHash = [string]$DeclarationVocabulary.vocabularySetHash
        behaviorById = $behaviorById
        currentDeclarationVocabularyExposure = [ordered]@{
            status = 'NoRuntimeCapabilitiesDeclared'
            allowedCapabilityIds = @()
            reason = 'DIA-13 remains a static declaration vocabulary; its CompatibilityPack exposure has no runtime capability ids.'
        }
    }
}

function Get-DialectPolicyConsumerBoundaryCatalogContext {
    param([Parameter(Mandatory = $true)][object]$Catalog)

    $catalogFields = @('schemaVersion', 'catalogId', 'catalogVersion', 'sourceWorkPackage', 'sourceInventoryWorkPackage', 'sourceDeclarationVocabularyWorkPackage', 'sourceInventoryHash', 'sourceDeclarationVocabularyHash', 'expectedBehaviorKeyCount', 'expectedDecisionBoundaryCount', 'expectedSourceClassificationBindingCount', 'expectedSourceFileBindingCount', 'boundaries')
    Assert-DialectPolicyConsumerBoundaryObjectShape -Value $Catalog -Required $catalogFields -Allowed $catalogFields -Context 'DIA-14 catalog'
    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-14' -or
        [string]$Catalog.sourceInventoryWorkPackage -cne 'M0-DIA-01' -or [string]$Catalog.sourceDeclarationVocabularyWorkPackage -cne 'M0-DIA-13' -or
        [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported DIA-14 policy consumer boundary catalog.'
    }
    Assert-DialectPolicyConsumerBoundarySemanticVersion $Catalog.catalogVersion 'catalogVersion'
    Assert-DialectPolicyConsumerBoundaryHash $Catalog.sourceInventoryHash 'catalog sourceInventoryHash'
    Assert-DialectPolicyConsumerBoundaryHash $Catalog.sourceDeclarationVocabularyHash 'catalog sourceDeclarationVocabularyHash'
    foreach ($expectedCountName in @('expectedBehaviorKeyCount', 'expectedDecisionBoundaryCount', 'expectedSourceClassificationBindingCount', 'expectedSourceFileBindingCount')) {
        if ([int]$Catalog.$expectedCountName -le 0) { throw "DIA-14 catalog $expectedCountName is invalid." }
    }

    $boundaryByBehaviorKey = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $consumerContractIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $decisionOwners = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($boundary in @($Catalog.boundaries)) {
        $boundaryFields = @('behaviorKeyId', 'consumerContractId', 'decisionOwner', 'sourceBindings', 'boundaryDraftStatus', 'runtimeStatus')
        Assert-DialectPolicyConsumerBoundaryObjectShape -Value $boundary -Required $boundaryFields -Allowed $boundaryFields -Context 'DIA-14 boundary catalog entry'
        $behaviorKeyId = [string]$boundary.behaviorKeyId
        $consumerContractId = [string]$boundary.consumerContractId
        $decisionOwner = [string]$boundary.decisionOwner
        if ($behaviorKeyId -notmatch '^[a-z][a-z0-9.\-]*\.v[0-9]+$') { throw "Invalid DIA-14 BehaviorKey id: $behaviorKeyId" }
        if ($consumerContractId -notmatch '^[a-z][a-z0-9.\-]*\.v[0-9]+$') { throw "Invalid DIA-14 consumer contract id: $consumerContractId" }
        if ([string]::IsNullOrWhiteSpace($decisionOwner)) { throw "DIA-14 decision owner is empty: $behaviorKeyId" }
        if ([string]$boundary.boundaryDraftStatus -cne 'BoundaryDraftOnly' -or [string]$boundary.runtimeStatus -cne 'NotImplemented') {
            throw "DIA-14 boundary incorrectly advances runtime implementation: $behaviorKeyId"
        }
        if ($boundaryByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "Duplicate DIA-14 BehaviorKey boundary: $behaviorKeyId" }
        if (-not $consumerContractIds.Add($consumerContractId)) { throw "Duplicate DIA-14 consumer contract id: $consumerContractId" }
        if (-not $decisionOwners.Add($decisionOwner)) { throw "Duplicate DIA-14 decision owner: $decisionOwner" }

        $sourceBindingByClassification = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
        foreach ($sourceBinding in @($boundary.sourceBindings)) {
            $sourceBindingFields = @('classificationId', 'role')
            Assert-DialectPolicyConsumerBoundaryObjectShape -Value $sourceBinding -Required $sourceBindingFields -Allowed $sourceBindingFields -Context "DIA-14 $behaviorKeyId source binding"
            $classificationId = [string]$sourceBinding.classificationId
            $role = [string]$sourceBinding.role
            if ([string]::IsNullOrWhiteSpace($classificationId)) { throw "DIA-14 source classification id is empty: $behaviorKeyId" }
            if ($role -cne 'DecisionConsumer' -and $role -cne 'ConfigurationInput') { throw "Invalid DIA-14 source role: $role" }
            if ($sourceBindingByClassification.ContainsKey($classificationId)) { throw "Duplicate DIA-14 source classification binding: $behaviorKeyId $classificationId" }
            $sourceBindingByClassification.Add($classificationId, [pscustomobject][ordered]@{ classificationId = $classificationId; role = $role })
        }
        if ($sourceBindingByClassification.Count -eq 0) { throw "DIA-14 boundary has no source bindings: $behaviorKeyId" }
        $hasDecisionConsumer = $false
        foreach ($sourceBinding in $sourceBindingByClassification.Values) {
            if ($sourceBinding.role -ceq 'DecisionConsumer') { $hasDecisionConsumer = $true; break }
        }
        if (-not $hasDecisionConsumer) { throw "DIA-14 boundary has no DecisionConsumer source binding: $behaviorKeyId" }
        $boundaryByBehaviorKey.Add($behaviorKeyId, [pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                consumerContractId = $consumerContractId
                decisionOwner = $decisionOwner
                sourceBindingByClassification = $sourceBindingByClassification
                boundaryDraftStatus = 'BoundaryDraftOnly'
                runtimeStatus = 'NotImplemented'
            })
    }
    if ($boundaryByBehaviorKey.Count -eq 0) { throw 'DIA-14 catalog must contain boundaries.' }

    $canonicalBoundaries = New-Object 'System.Collections.Generic.List[object]'
    foreach ($boundary in $boundaryByBehaviorKey.Values) {
        $canonicalSourceBindings = New-Object 'System.Collections.Generic.List[object]'
        foreach ($sourceBinding in $boundary.sourceBindingByClassification.Values) {
            $canonicalSourceBindings.Add([ordered]@{ classificationId = $sourceBinding.classificationId; role = $sourceBinding.role })
        }
        $canonicalBoundaries.Add([ordered]@{
                behaviorKeyId = $boundary.behaviorKeyId
                consumerContractId = $boundary.consumerContractId
                decisionOwner = $boundary.decisionOwner
                sourceBindings = $canonicalSourceBindings.ToArray()
                boundaryDraftStatus = $boundary.boundaryDraftStatus
                runtimeStatus = $boundary.runtimeStatus
            })
    }
    $canonicalCatalog = [ordered]@{
        schemaVersion = '1.0.0'; catalogId = [string]$Catalog.catalogId; catalogVersion = [string]$Catalog.catalogVersion
        sourceWorkPackage = 'M0-DIA-14'; sourceInventoryWorkPackage = 'M0-DIA-01'; sourceDeclarationVocabularyWorkPackage = 'M0-DIA-13'
        sourceInventoryHash = [string]$Catalog.sourceInventoryHash; sourceDeclarationVocabularyHash = [string]$Catalog.sourceDeclarationVocabularyHash
        expectedBehaviorKeyCount = [int]$Catalog.expectedBehaviorKeyCount; expectedDecisionBoundaryCount = [int]$Catalog.expectedDecisionBoundaryCount
        expectedSourceClassificationBindingCount = [int]$Catalog.expectedSourceClassificationBindingCount; expectedSourceFileBindingCount = [int]$Catalog.expectedSourceFileBindingCount
        boundaries = $canonicalBoundaries.ToArray()
    }
    return [pscustomobject][ordered]@{
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = Get-DialectPolicyConsumerBoundaryCanonicalHash -Value $canonicalCatalog
        sourceInventoryHash = [string]$Catalog.sourceInventoryHash
        sourceDeclarationVocabularyHash = [string]$Catalog.sourceDeclarationVocabularyHash
        expectedBehaviorKeyCount = [int]$Catalog.expectedBehaviorKeyCount
        expectedDecisionBoundaryCount = [int]$Catalog.expectedDecisionBoundaryCount
        expectedSourceClassificationBindingCount = [int]$Catalog.expectedSourceClassificationBindingCount
        expectedSourceFileBindingCount = [int]$Catalog.expectedSourceFileBindingCount
        boundaryByBehaviorKey = $boundaryByBehaviorKey
    }
}

function New-DialectPolicyConsumerBoundaryReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][object]$Inventory,
        [Parameter(Mandatory = $true, Position = 1)][object]$DeclarationVocabulary,
        [Parameter(Mandatory = $true, Position = 2)][object]$Catalog,
        [string]$OutputPath = ''
    )

    $inventoryContext = Get-DialectPolicyConsumerBoundaryInventoryContext -Inventory $Inventory
    $vocabularyContext = Get-DialectPolicyConsumerBoundaryVocabularyContext -DeclarationVocabulary $DeclarationVocabulary
    $catalogContext = Get-DialectPolicyConsumerBoundaryCatalogContext -Catalog $Catalog
    if ($catalogContext.sourceInventoryHash -cne $inventoryContext.sourceInventoryHash) { throw 'DIA-14 source inventory hash drifted.' }
    if ($catalogContext.sourceDeclarationVocabularyHash -cne $vocabularyContext.sourceDeclarationVocabularyHash) { throw 'DIA-14 source declaration vocabulary hash drifted.' }
    if ($vocabularyContext.behaviorById.Count -ne $inventoryContext.behaviorById.Count) { throw 'DIA-14 DIA-01 and DIA-13 BehaviorKey count drifted.' }
    if ($catalogContext.boundaryByBehaviorKey.Count -ne $vocabularyContext.behaviorById.Count) { throw 'DIA-14 boundary count drifted from DIA-13 BehaviorKey vocabulary.' }

    $boundaries = New-Object 'System.Collections.Generic.List[object]'
    $sourceFileBindings = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $sourceClassificationBindingCount = 0
    foreach ($behaviorKeyId in $catalogContext.boundaryByBehaviorKey.Keys) {
        $catalogBoundary = $catalogContext.boundaryByBehaviorKey[$behaviorKeyId]
        if (-not $vocabularyContext.behaviorById.ContainsKey($behaviorKeyId)) { throw "DIA-14 catalog references unknown BehaviorKey: $behaviorKeyId" }
        if (-not $inventoryContext.behaviorById.ContainsKey($behaviorKeyId)) { throw "DIA-14 source inventory is missing BehaviorKey: $behaviorKeyId" }
        $vocabularyBehavior = $vocabularyContext.behaviorById[$behaviorKeyId]
        $inventoryBehavior = $inventoryContext.behaviorById[$behaviorKeyId]
        Assert-DialectPolicyConsumerBoundaryStringSet -Actual @($inventoryBehavior.sourceClassificationIds) -Expected @($vocabularyBehavior.sourceClassificationIds) -Context "DIA-14 $behaviorKeyId DIA-01/DIA-13 source classification set"
        Assert-DialectPolicyConsumerBoundaryStringSet -Actual @($inventoryBehavior.targetModuleIds) -Expected @($vocabularyBehavior.targetModuleIds) -Context "DIA-14 $behaviorKeyId DIA-01/DIA-13 target module set"
        Assert-DialectPolicyConsumerBoundaryStringSet -Actual @($inventoryBehavior.fixtureIds) -Expected @($vocabularyBehavior.fixtureIds) -Context "DIA-14 $behaviorKeyId DIA-01/DIA-13 fixture set"
        if ([int]$inventoryBehavior.branchHitCount -ne [int]$vocabularyBehavior.branchHitCount) { throw "DIA-14 $behaviorKeyId branch hit count drifted." }
        Assert-DialectPolicyConsumerBoundaryStringSet -Actual @($catalogBoundary.sourceBindingByClassification.Keys) -Expected @($vocabularyBehavior.sourceClassificationIds) -Context "DIA-14 $behaviorKeyId catalog source classification set"

        $sourceBindings = New-Object 'System.Collections.Generic.List[object]'
        foreach ($classificationId in $catalogBoundary.sourceBindingByClassification.Keys) {
            if (-not $inventoryBehavior.classificationById.ContainsKey($classificationId)) { throw "DIA-14 $behaviorKeyId source classification is absent from DIA-01: $classificationId" }
            $catalogSourceBinding = $catalogBoundary.sourceBindingByClassification[$classificationId]
            $inventoryClassification = $inventoryBehavior.classificationById[$classificationId]
            if ($catalogSourceBinding.role -ceq 'DecisionConsumer') {
                Assert-DialectPolicyConsumerBoundaryStringSet -Actual @($inventoryClassification.intendedOwners) -Expected @($catalogBoundary.decisionOwner) -Context "DIA-14 DecisionConsumer owner drifted for $behaviorKeyId $classificationId"
            }
            elseif ($catalogSourceBinding.role -ceq 'ConfigurationInput') {
                $sameOwner = @($inventoryClassification.intendedOwners | Where-Object { $_ -ceq $catalogBoundary.decisionOwner })
                if ($sameOwner.Count -ne 0) { throw "DIA-14 ConfigurationInput owner drifted for $behaviorKeyId $classificationId" }
            }
            else {
                throw "Invalid DIA-14 source role: $($catalogSourceBinding.role)"
            }
            foreach ($sourceFilePath in @($inventoryClassification.sourceFilePaths)) {
                # A source-file binding is unique per future consumer boundary;
                # several legacy classifications in the same file do not create
                # several future file consumers.
                [void]$sourceFileBindings.Add("$behaviorKeyId|$sourceFilePath")
            }
            $sourceClassificationBindingCount++
            $sourceBindings.Add([pscustomobject][ordered]@{
                    classificationId = $classificationId
                    role = $catalogSourceBinding.role
                    branchHitCount = [int]$inventoryClassification.branchHitCount
                    sourceFilePaths = @($inventoryClassification.sourceFilePaths)
                    currentOwners = @($inventoryClassification.currentOwners)
                    intendedOwners = @($inventoryClassification.intendedOwners)
                    targetModuleIds = @($inventoryClassification.targetModuleIds)
                    fixtureIds = @($inventoryClassification.fixtureIds)
                })
        }
        $boundaries.Add([pscustomobject][ordered]@{
                behaviorKeyId = $behaviorKeyId
                consumerContractId = $catalogBoundary.consumerContractId
                decisionOwner = $catalogBoundary.decisionOwner
                branchHitCount = [int]$inventoryBehavior.branchHitCount
                currentOwners = @($inventoryBehavior.currentOwners)
                sourceIntendedOwners = @($inventoryBehavior.sourceIntendedOwners)
                sourceClassificationIds = @($inventoryBehavior.sourceClassificationIds)
                sourceFilePaths = @($inventoryBehavior.sourceFilePaths)
                targetModuleIds = @($inventoryBehavior.targetModuleIds)
                fixtureIds = @($inventoryBehavior.fixtureIds)
                sourceBindings = $sourceBindings.ToArray()
                boundaryDraftStatus = 'BoundaryDraftOnly'
                runtimeStatus = 'NotImplemented'
            })
    }
    foreach ($behaviorKeyId in $vocabularyContext.behaviorById.Keys) {
        if (-not $catalogContext.boundaryByBehaviorKey.ContainsKey($behaviorKeyId)) { throw "DIA-14 BehaviorKey has no consumer boundary: $behaviorKeyId" }
    }

    $boundaryCounts = [ordered]@{
        behaviorKeyCount = [int]$vocabularyContext.behaviorById.Count
        decisionBoundaryCount = [int]$boundaries.Count
        sourceClassificationBindingCount = [int]$sourceClassificationBindingCount
        sourceFileBindingCount = [int]$sourceFileBindings.Count
    }
    if ($boundaryCounts.behaviorKeyCount -ne $catalogContext.expectedBehaviorKeyCount -or
        $boundaryCounts.decisionBoundaryCount -ne $catalogContext.expectedDecisionBoundaryCount -or
        $boundaryCounts.sourceClassificationBindingCount -ne $catalogContext.expectedSourceClassificationBindingCount -or
        $boundaryCounts.sourceFileBindingCount -ne $catalogContext.expectedSourceFileBindingCount) {
        throw "DIA-14 boundary count drifted: behavior=$($boundaryCounts.behaviorKeyCount), decision=$($boundaryCounts.decisionBoundaryCount), classification=$($boundaryCounts.sourceClassificationBindingCount), file=$($boundaryCounts.sourceFileBindingCount)."
    }
    $currentRuntimeIsolation = [ordered]@{ status = 'Failed'; reason = 'DIA-14 documents static decision boundaries but does not isolate legacy process-wide profile, configuration or registry state.' }
    $parserVmConsumption = [ordered]@{ status = 'NotConsumed'; reason = 'DIA-14 is an offline boundary audit and is not read by the legacy Parser or VM.' }
    $compatibilityPlanRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-14 does not create a runtime CompatibilityPlan, DialectPlan, LegacySessionFacade, feature flag or frozen registry.' }
    $policyManagerRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-14 records future narrow consumer contracts only; it does not implement a policy manager, resolver or policy value.' }
    $m1Eligibility = [ordered]@{ status = 'Blocked'; reason = 'Static consumer boundaries do not establish session isolation, generation guards, A-to-B-to-A rollback or runtime capability binding.' }
    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'; workPackage = 'M0-DIA-14'
        sourceInventoryHash = $inventoryContext.sourceInventoryHash; sourceDeclarationVocabularyHash = $vocabularyContext.sourceDeclarationVocabularyHash
        catalogId = $catalogContext.catalogId; catalogVersion = $catalogContext.catalogVersion; catalogHash = $catalogContext.catalogHash
        boundaryCounts = $boundaryCounts; currentDeclarationVocabularyExposure = $vocabularyContext.currentDeclarationVocabularyExposure
        boundaries = $boundaries.ToArray(); currentRuntimeIsolation = $currentRuntimeIsolation; parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime; policyManagerRuntime = $policyManagerRuntime; m1Eligibility = $m1Eligibility
    }
    $boundarySetHash = Get-DialectPolicyConsumerBoundaryCanonicalHash -Value $setPayload -Depth 100
    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-14'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourceInventoryHash = $inventoryContext.sourceInventoryHash
        sourceDeclarationVocabularyHash = $vocabularyContext.sourceDeclarationVocabularyHash
        catalogId = $catalogContext.catalogId
        catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash
        boundarySetHash = $boundarySetHash
        boundaryCounts = $boundaryCounts
        currentDeclarationVocabularyExposure = $vocabularyContext.currentDeclarationVocabularyExposure
        boundaries = $boundaries.ToArray()
        currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime
        policyManagerRuntime = $policyManagerRuntime
        m1Eligibility = $m1Eligibility
        uncovered = @(
            'DIA-14 fixes only source-backed future consumer ownership. It does not prove a policy default, error path, completion mode, effect, timing or compatibility result.',
            'ConfigurationInput records source provenance only. It is not a runtime configuration object and must not become a sibling direct call to the future decision consumer.',
            'No runtime policy manager, resolver, CompatibilityPlan, LegacySessionFacade, D2 frozen registry, Parser/VM consumption, game run, APK or device evidence is introduced.'
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, (($report | ConvertTo-Json -Depth 100) + "`n"), $script:DialectPolicyConsumerBoundaryUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectPolicyConsumerBoundaryReport
