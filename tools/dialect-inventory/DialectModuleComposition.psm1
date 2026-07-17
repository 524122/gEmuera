Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# M0-DIA-16 turns existing static pack selections and static future port names
# into a fail-fast module dependency graph. It is deliberately not a runtime
# module catalog, resolver, CompatibilityPlan, assembly loader, or VM input.
$script:DialectModuleCompositionUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-DialectModuleCompositionSha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-DialectModuleCompositionCanonicalHash {
    param([Parameter(Mandatory = $true)][object]$Value, [int]$Depth = 100)

    return Get-DialectModuleCompositionSha256Hex -Bytes $script:DialectModuleCompositionUtf8NoBom.GetBytes(($Value | ConvertTo-Json -Depth $Depth -Compress))
}

function Assert-DialectModuleCompositionHash {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid DIA-16 hash: $Name" }
}

function Assert-DialectModuleCompositionSemanticVersion {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Invalid DIA-16 semantic version: $Name" }
}

function Get-DialectModuleCompositionStringSet {
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

function Assert-DialectModuleCompositionStringSet {
    param(
        [object[]]$Actual,
        [object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Context,
        [bool]$AllowEmpty = $false
    )

    $actualValues = @(Get-DialectModuleCompositionStringSet -Values $Actual -Name "$Context actual values" -AllowEmpty $AllowEmpty)
    $expectedValues = @(Get-DialectModuleCompositionStringSet -Values $Expected -Name "$Context expected values" -AllowEmpty $AllowEmpty)
    if ($actualValues.Count -ne $expectedValues.Count) { throw "$Context count drifted: expected $($expectedValues.Count), actual $($actualValues.Count)." }
    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -cne $expectedValues[$index]) { throw "$Context value drifted: expected $($expectedValues[$index]), actual $($actualValues[$index])." }
    }
}

function Assert-DialectModuleCompositionObjectShape {
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

function Get-DialectModuleCompositionCompatibilityPackContext {
    param([Parameter(Mandatory = $true)][object]$CompatibilityPack)

    if ([string]$CompatibilityPack.schemaVersion -cne '1.0.0' -or [string]$CompatibilityPack.workPackage -cne 'M0-DIA-12') {
        throw 'DIA-16 requires M0-DIA-12 CompatibilityPack evidence.'
    }
    Assert-DialectModuleCompositionHash $CompatibilityPack.compatibilityPackSetHash 'M0-DIA-12 compatibilityPackSetHash'
    if ([string]$CompatibilityPack.executionStatus -cne 'InProgress' -or [string]$CompatibilityPack.gateStatus -cne 'Blocked' -or [string]$CompatibilityPack.blockerCode -cne 'EvidenceMissing' -or [string]$CompatibilityPack.result -cne 'Partial') {
        throw 'DIA-12 evidence status was incorrectly advanced.'
    }
    if ([string]$CompatibilityPack.currentRuntimeIsolation.status -cne 'Failed' -or
        [string]$CompatibilityPack.parserVmConsumption.status -cne 'NotConsumed' -or
        [string]$CompatibilityPack.compatibilityPlanRuntime.status -cne 'NotImplemented' -or
        [string]$CompatibilityPack.resolverRuntime.status -cne 'NotImplemented' -or
        [string]$CompatibilityPack.m1Eligibility.status -cne 'Blocked') {
        throw 'DIA-12 runtime boundary status was incorrectly advanced.'
    }
    if ($null -eq $CompatibilityPack.PSObject.Properties['declarationPolicy']) { throw 'DIA-12 CompatibilityPack declaration policy is missing.' }
    $declarationPolicy = $CompatibilityPack.declarationPolicy
    if ($null -eq $declarationPolicy.PSObject.Properties['allowedCapabilityIds']) { throw 'DIA-12 declaration policy is missing allowedCapabilityIds.' }
    $allowedCapabilityIds = @(Get-DialectModuleCompositionStringSet -Values @($declarationPolicy.allowedCapabilityIds) -Name 'DIA-12 allowed capability id' -AllowEmpty $true)
    if ($allowedCapabilityIds.Count -ne 0) { throw 'DIA-12 must retain no runtime capability exposure.' }

    $packByProfile = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($pack in @($CompatibilityPack.packs)) {
        $fields = @('packId', 'declarationVersion', 'profileId', 'legacyCoreProfileEnum', 'builtInModuleIds', 'staticProjectionSupportModuleIds', 'requiredCapabilityIds', 'optionalCapabilityIds', 'contentBindingStatus', 'saveProfileEvidenceStatus', 'fixtureEvidenceStatus', 'distributionEligibility')
        Assert-DialectModuleCompositionObjectShape -Value $pack -Required $fields -Allowed $fields -Context 'DIA-12 static CompatibilityPack entry'
        $profileId = [string]$pack.profileId
        $packId = [string]$pack.packId
        if ([string]::IsNullOrWhiteSpace($profileId) -or [string]::IsNullOrWhiteSpace($packId)) { throw 'DIA-12 pack profile identity is incomplete.' }
        if ($packByProfile.ContainsKey($profileId)) { throw "Duplicate DIA-12 pack profile: $profileId" }
        $builtInModuleIds = @(Get-DialectModuleCompositionStringSet -Values @($pack.builtInModuleIds) -Name "DIA-12 $profileId built-in module")
        foreach ($moduleId in $builtInModuleIds) {
            if ($moduleId -match '^legacy\.current\.') { throw "DIA-12 promoted static projection support into a module: $moduleId" }
        }
        $requiredCapabilityIds = @(Get-DialectModuleCompositionStringSet -Values @($pack.requiredCapabilityIds) -Name "DIA-12 $profileId required capability" -AllowEmpty $true)
        $optionalCapabilityIds = @(Get-DialectModuleCompositionStringSet -Values @($pack.optionalCapabilityIds) -Name "DIA-12 $profileId optional capability" -AllowEmpty $true)
        if ($requiredCapabilityIds.Count -ne 0 -or $optionalCapabilityIds.Count -ne 0) { throw 'DIA-12 must retain no runtime capability exposure.' }
        if ([string]$pack.contentBindingStatus -cne 'NotBound' -or [string]$pack.saveProfileEvidenceStatus -cne 'Uncovered' -or [string]$pack.fixtureEvidenceStatus -cne 'Uncovered' -or [string]$pack.distributionEligibility -cne 'Blocked') {
            throw "DIA-12 pack status was incorrectly advanced: $profileId"
        }
        $packByProfile.Add($profileId, [pscustomobject][ordered]@{
                packId = $packId
                profileId = $profileId
                legacyCoreProfileEnum = [string]$pack.legacyCoreProfileEnum
                builtInModuleIds = @($builtInModuleIds)
            })
    }
    if ($packByProfile.Count -ne 2 -or -not $packByProfile.ContainsKey('v24pure') -or -not $packByProfile.ContainsKey('snake')) {
        throw 'DIA-12 must retain exactly v24pure and snake static packs.'
    }
    return [pscustomobject][ordered]@{
        sourceCompatibilityPackHash = [string]$CompatibilityPack.compatibilityPackSetHash
        packByProfile = $packByProfile
        currentCompatibilityPackExposure = [ordered]@{
            status = 'NoRuntimeCapabilitiesDeclared'
            allowedCapabilityIds = @()
            reason = 'DIA-12 static CompatibilityPacks intentionally declare no runtime capability ids.'
        }
    }
}

function Copy-DialectModuleCompositionSourceBinding {
    param([Parameter(Mandatory = $true)][object]$SourceBinding, [Parameter(Mandatory = $true)][string]$Context)

    $fields = @('classificationId', 'role', 'branchHitCount', 'sourceFilePaths', 'currentOwners', 'intendedOwners', 'targetModuleIds', 'fixtureIds')
    Assert-DialectModuleCompositionObjectShape -Value $SourceBinding -Required $fields -Allowed $fields -Context $Context
    $role = [string]$SourceBinding.role
    if ($role -cne 'DecisionConsumer' -and $role -cne 'ConfigurationInput') { throw "Invalid DIA-16 source role: $role" }
    return [pscustomobject][ordered]@{
        classificationId = [string]$SourceBinding.classificationId
        role = $role
        branchHitCount = [int]$SourceBinding.branchHitCount
        sourceFilePaths = @(Get-DialectModuleCompositionStringSet -Values @($SourceBinding.sourceFilePaths) -Name "$Context source file")
        currentOwners = @(Get-DialectModuleCompositionStringSet -Values @($SourceBinding.currentOwners) -Name "$Context current owner")
        intendedOwners = @(Get-DialectModuleCompositionStringSet -Values @($SourceBinding.intendedOwners) -Name "$Context intended owner")
        targetModuleIds = @(Get-DialectModuleCompositionStringSet -Values @($SourceBinding.targetModuleIds) -Name "$Context target module")
        fixtureIds = @(Get-DialectModuleCompositionStringSet -Values @($SourceBinding.fixtureIds) -Name "$Context fixture")
    }
}

function Get-DialectModuleCompositionPolicySurfaceContext {
    param([Parameter(Mandatory = $true)][object]$PolicySurface)

    if ([string]$PolicySurface.schemaVersion -cne '1.0.0' -or [string]$PolicySurface.workPackage -cne 'M0-DIA-15') {
        throw 'DIA-16 requires M0-DIA-15 policy surface evidence.'
    }
    Assert-DialectModuleCompositionHash $PolicySurface.policySurfaceSetHash 'M0-DIA-15 policySurfaceSetHash'
    Assert-DialectModuleCompositionHash $PolicySurface.sourceDeclarationVocabularyHash 'M0-DIA-15 sourceDeclarationVocabularyHash'
    if ([string]$PolicySurface.executionStatus -cne 'InProgress' -or [string]$PolicySurface.gateStatus -cne 'Blocked' -or [string]$PolicySurface.blockerCode -cne 'EvidenceMissing' -or [string]$PolicySurface.result -cne 'Partial') {
        throw 'DIA-15 evidence status was incorrectly advanced.'
    }
    if ([string]$PolicySurface.currentRuntimeIsolation.status -cne 'Failed' -or
        [string]$PolicySurface.parserVmConsumption.status -cne 'NotConsumed' -or
        [string]$PolicySurface.compatibilityPlanRuntime.status -cne 'NotImplemented' -or
        [string]$PolicySurface.policyManagerRuntime.status -cne 'NotImplemented' -or
        [string]$PolicySurface.runtimeTypeDeclarations.status -cne 'NotImplemented' -or
        [string]$PolicySurface.m1Eligibility.status -cne 'Blocked') {
        throw 'DIA-15 runtime boundary status was incorrectly advanced.'
    }
    if ($null -eq $PolicySurface.PSObject.Properties['currentDeclarationVocabularyExposure']) { throw 'DIA-15 current declaration vocabulary exposure is missing.' }
    $exposure = $PolicySurface.currentDeclarationVocabularyExposure
    if ([string]$exposure.status -cne 'NoRuntimeCapabilitiesDeclared' -or $null -eq $exposure.PSObject.Properties['allowedCapabilityIds']) {
        throw 'DIA-15 must retain no runtime capability exposure.'
    }
    $allowedCapabilityIds = @(Get-DialectModuleCompositionStringSet -Values @($exposure.allowedCapabilityIds) -Name 'DIA-15 allowed capability id' -AllowEmpty $true)
    if ($allowedCapabilityIds.Count -ne 0) { throw 'DIA-15 must retain no runtime capability exposure.' }

    $contractByPortType = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($contract in @($PolicySurface.contracts)) {
        $fields = @('behaviorKeyId', 'consumerContractId', 'decisionOwner', 'portTypeId', 'contractKind', 'branchHitCount', 'currentOwners', 'sourceIntendedOwners', 'sourceClassificationIds', 'sourceFilePaths', 'targetModuleIds', 'fixtureIds', 'sourceBindings', 'contractDraftStatus', 'policyValueStatus', 'runtimeStatus')
        Assert-DialectModuleCompositionObjectShape -Value $contract -Required $fields -Allowed $fields -Context 'DIA-15 policy surface contract'
        $portTypeId = [string]$contract.portTypeId
        if ($portTypeId -notmatch '^I[A-Za-z][A-Za-z0-9]*$') { throw "Invalid DIA-15 port type id: $portTypeId" }
        if ($contractByPortType.ContainsKey($portTypeId)) { throw "Duplicate DIA-15 port type id: $portTypeId" }
        if ([string]$contract.contractDraftStatus -cne 'InterfaceDraftOnly' -or [string]$contract.policyValueStatus -cne 'Unspecified' -or [string]$contract.runtimeStatus -cne 'NotImplemented') {
            throw "DIA-15 contract implementation status drifted: $portTypeId"
        }
        $targetModuleIds = @(Get-DialectModuleCompositionStringSet -Values @($contract.targetModuleIds) -Name "DIA-15 $portTypeId target module")
        $sourceBindings = New-Object 'System.Collections.Generic.List[object]'
        foreach ($sourceBinding in @($contract.sourceBindings)) { $sourceBindings.Add((Copy-DialectModuleCompositionSourceBinding -SourceBinding $sourceBinding -Context "DIA-15 $portTypeId source binding")) }
        $contractByPortType.Add($portTypeId, [pscustomobject][ordered]@{
                behaviorKeyId = [string]$contract.behaviorKeyId
                portTypeId = $portTypeId
                targetModuleIds = @($targetModuleIds)
                sourceBindings = $sourceBindings.ToArray()
            })
    }
    if ($contractByPortType.Count -ne 10 -or [int]$PolicySurface.surfaceCounts.contractCount -ne 10) { throw 'DIA-15 must retain exactly ten policy surface ports.' }
    return [pscustomobject][ordered]@{
        sourcePolicySurfaceHash = [string]$PolicySurface.policySurfaceSetHash
        sourceDeclarationVocabularyHash = [string]$PolicySurface.sourceDeclarationVocabularyHash
        contractByPortType = $contractByPortType
    }
}

function Get-DialectModuleCompositionCatalogContext {
    param([Parameter(Mandatory = $true)][object]$Catalog)

    $catalogFields = @('schemaVersion', 'catalogId', 'catalogVersion', 'sourceWorkPackage', 'sourceCompatibilityPackWorkPackage', 'sourcePolicySurfaceWorkPackage', 'sourceCompatibilityPackHash', 'sourcePolicySurfaceHash', 'expectedModuleCount', 'expectedDependencyEdgeCount', 'expectedPortDeclarationCount', 'modules')
    Assert-DialectModuleCompositionObjectShape -Value $Catalog -Required $catalogFields -Allowed $catalogFields -Context 'DIA-16 catalog'
    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-16' -or
        [string]$Catalog.sourceCompatibilityPackWorkPackage -cne 'M0-DIA-12' -or [string]$Catalog.sourcePolicySurfaceWorkPackage -cne 'M0-DIA-15' -or
        [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported DIA-16 module composition catalog.'
    }
    Assert-DialectModuleCompositionSemanticVersion $Catalog.catalogVersion 'catalogVersion'
    Assert-DialectModuleCompositionHash $Catalog.sourceCompatibilityPackHash 'catalog sourceCompatibilityPackHash'
    Assert-DialectModuleCompositionHash $Catalog.sourcePolicySurfaceHash 'catalog sourcePolicySurfaceHash'
    foreach ($countName in @('expectedModuleCount', 'expectedDependencyEdgeCount', 'expectedPortDeclarationCount')) {
        if ([int]$Catalog.$countName -lt 0) { throw "DIA-16 catalog $countName is invalid." }
    }

    $moduleById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $portOwnerById = New-Object 'System.Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($module in @($Catalog.modules)) {
        $moduleFields = @('moduleId', 'moduleVersion', 'moduleApiVersion', 'dependencies', 'declaredPortTypeIds', 'descriptorStatus', 'runtimeStatus', 'distributionEligibility')
        Assert-DialectModuleCompositionObjectShape -Value $module -Required $moduleFields -Allowed $moduleFields -Context 'DIA-16 module catalog entry'
        $moduleId = [string]$module.moduleId
        if ($moduleId -notmatch '^[a-z][a-z0-9.\-]*$') { throw "Invalid DIA-16 module id: $moduleId" }
        Assert-DialectModuleCompositionSemanticVersion $module.moduleVersion "moduleVersion $moduleId"
        if ([int]$module.moduleApiVersion -le 0) { throw "Invalid DIA-16 module API version: $moduleId" }
        if ([string]$module.descriptorStatus -cne 'StaticCandidate' -or [string]$module.runtimeStatus -cne 'NotImplemented' -or [string]$module.distributionEligibility -cne 'Blocked') {
            throw "DIA-16 module descriptor status drifted: $moduleId"
        }
        if ($moduleById.ContainsKey($moduleId)) { throw "Duplicate DIA-16 module id: $moduleId" }
        $dependencies = New-Object 'System.Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
        foreach ($dependency in @($module.dependencies)) {
            $dependencyFields = @('moduleId', 'versionRange')
            Assert-DialectModuleCompositionObjectShape -Value $dependency -Required $dependencyFields -Allowed $dependencyFields -Context "DIA-16 $moduleId dependency"
            $dependencyModuleId = [string]$dependency.moduleId
            $versionRange = [string]$dependency.versionRange
            if ($dependencyModuleId -notmatch '^[a-z][a-z0-9.\-]*$' -or $versionRange -notmatch '^\[[0-9]+\.[0-9]+\.[0-9]+,[0-9]+\.[0-9]+\.[0-9]+\)$') { throw "Invalid DIA-16 dependency declaration: $moduleId" }
            if ($dependencies.ContainsKey($dependencyModuleId)) { throw "Duplicate DIA-16 dependency module: $moduleId -> $dependencyModuleId" }
            $dependencies.Add($dependencyModuleId, $versionRange)
        }
        $declaredPortTypeIds = @(Get-DialectModuleCompositionStringSet -Values @($module.declaredPortTypeIds) -Name "DIA-16 $moduleId declared port" -AllowEmpty $true)
        foreach ($portTypeId in $declaredPortTypeIds) {
            if ($portTypeId -notmatch '^I[A-Za-z][A-Za-z0-9]*$') { throw "Invalid DIA-16 declared port type id: $portTypeId" }
            if ($portOwnerById.ContainsKey($portTypeId)) { throw "Duplicate DIA-16 port declaration: $portTypeId" }
            $portOwnerById.Add($portTypeId, $moduleId)
        }
        $moduleById.Add($moduleId, [pscustomobject][ordered]@{
                moduleId = $moduleId
                moduleVersion = [string]$module.moduleVersion
                moduleApiVersion = [int]$module.moduleApiVersion
                dependencies = $dependencies
                declaredPortTypeIds = @($declaredPortTypeIds)
            })
    }
    if ($moduleById.Count -eq 0) { throw 'DIA-16 catalog must contain module descriptors.' }

    $edgeCount = 0
    foreach ($module in $moduleById.Values) {
        foreach ($dependencyModuleId in $module.dependencies.Keys) {
            if (-not $moduleById.ContainsKey($dependencyModuleId)) { throw "DIA-16 module $($module.moduleId) references unknown dependency module: $dependencyModuleId" }
            $edgeCount++
        }
    }
    $canonicalModules = New-Object 'System.Collections.Generic.List[object]'
    foreach ($module in $moduleById.Values) {
        $canonicalDependencies = New-Object 'System.Collections.Generic.List[object]'
        foreach ($dependencyModuleId in $module.dependencies.Keys) { $canonicalDependencies.Add([ordered]@{ moduleId = $dependencyModuleId; versionRange = $module.dependencies[$dependencyModuleId] }) }
        $canonicalModules.Add([ordered]@{
                moduleId = $module.moduleId; moduleVersion = $module.moduleVersion; moduleApiVersion = $module.moduleApiVersion
                dependencies = $canonicalDependencies.ToArray(); declaredPortTypeIds = @($module.declaredPortTypeIds)
                descriptorStatus = 'StaticCandidate'; runtimeStatus = 'NotImplemented'; distributionEligibility = 'Blocked'
            })
    }
    $canonicalCatalog = [ordered]@{
        schemaVersion = '1.0.0'; catalogId = [string]$Catalog.catalogId; catalogVersion = [string]$Catalog.catalogVersion
        sourceWorkPackage = 'M0-DIA-16'; sourceCompatibilityPackWorkPackage = 'M0-DIA-12'; sourcePolicySurfaceWorkPackage = 'M0-DIA-15'
        sourceCompatibilityPackHash = [string]$Catalog.sourceCompatibilityPackHash; sourcePolicySurfaceHash = [string]$Catalog.sourcePolicySurfaceHash
        expectedModuleCount = [int]$Catalog.expectedModuleCount; expectedDependencyEdgeCount = [int]$Catalog.expectedDependencyEdgeCount; expectedPortDeclarationCount = [int]$Catalog.expectedPortDeclarationCount
        modules = $canonicalModules.ToArray()
    }
    return [pscustomobject][ordered]@{
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = Get-DialectModuleCompositionCanonicalHash -Value $canonicalCatalog
        sourceCompatibilityPackHash = [string]$Catalog.sourceCompatibilityPackHash
        sourcePolicySurfaceHash = [string]$Catalog.sourcePolicySurfaceHash
        expectedModuleCount = [int]$Catalog.expectedModuleCount
        expectedDependencyEdgeCount = [int]$Catalog.expectedDependencyEdgeCount
        expectedPortDeclarationCount = [int]$Catalog.expectedPortDeclarationCount
        edgeCount = $edgeCount
        moduleById = $moduleById
        portOwnerById = $portOwnerById
    }
}

function Get-DialectModuleCompositionResolvedClosure {
    param([Parameter(Mandatory = $true)][string[]]$SelectedModuleIds, [Parameter(Mandatory = $true)][System.Collections.Generic.SortedDictionary[string,object]]$ModuleById)

    $visited = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $visiting = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $ordered = New-Object 'System.Collections.Generic.List[string]'
    function Visit-DialectModuleCompositionNode {
        param([string]$ModuleId)

        if ($visited.Contains($ModuleId)) { return }
        if (-not $ModuleById.ContainsKey($ModuleId)) { throw "DIA-16 selected unknown module: $ModuleId" }
        if (-not $visiting.Add($ModuleId)) { throw "DIA-16 dependency cycle detected at module: $ModuleId" }
        foreach ($dependencyModuleId in $ModuleById[$ModuleId].dependencies.Keys) { Visit-DialectModuleCompositionNode -ModuleId $dependencyModuleId }
        [void]$visiting.Remove($ModuleId)
        [void]$visited.Add($ModuleId)
        $ordered.Add($ModuleId)
    }

    foreach ($moduleId in @(Get-DialectModuleCompositionStringSet -Values $SelectedModuleIds -Name 'DIA-16 selected module')) { Visit-DialectModuleCompositionNode -ModuleId $moduleId }
    return $ordered.ToArray()
}

function New-DialectModuleCompositionReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][object]$CompatibilityPack,
        [Parameter(Mandatory = $true, Position = 1)][object]$PolicySurface,
        [Parameter(Mandatory = $true, Position = 2)][object]$Catalog,
        [string]$OutputPath = ''
    )

    $packContext = Get-DialectModuleCompositionCompatibilityPackContext -CompatibilityPack $CompatibilityPack
    $surfaceContext = Get-DialectModuleCompositionPolicySurfaceContext -PolicySurface $PolicySurface
    $catalogContext = Get-DialectModuleCompositionCatalogContext -Catalog $Catalog
    if ($catalogContext.sourceCompatibilityPackHash -cne $packContext.sourceCompatibilityPackHash) { throw 'DIA-16 source CompatibilityPack hash drifted.' }
    if ($catalogContext.sourcePolicySurfaceHash -cne $surfaceContext.sourcePolicySurfaceHash) { throw 'DIA-16 source policy surface hash drifted.' }
    # Report descriptors in a deterministic dependency-first topological order.
    # The closure helper starts from ordinal module ids, while each visit emits
    # dependencies before their consumers. This keeps a base module before an
    # extension module without trusting catalog enumeration order.
    $moduleDescriptorOrder = @(Get-DialectModuleCompositionResolvedClosure -SelectedModuleIds @($catalogContext.moduleById.Keys) -ModuleById $catalogContext.moduleById)
    if ($catalogContext.moduleById.Count -ne $catalogContext.expectedModuleCount -or $catalogContext.edgeCount -ne $catalogContext.expectedDependencyEdgeCount -or $catalogContext.portOwnerById.Count -ne $catalogContext.expectedPortDeclarationCount) {
        throw "DIA-16 catalog count drifted: module=$($catalogContext.moduleById.Count), edge=$($catalogContext.edgeCount), port=$($catalogContext.portOwnerById.Count)."
    }

    $modules = New-Object 'System.Collections.Generic.List[object]'
    foreach ($moduleId in $moduleDescriptorOrder) {
        $module = $catalogContext.moduleById[$moduleId]
        $sourceBehaviorKeyIds = New-Object 'System.Collections.Generic.List[string]'
        foreach ($portTypeId in @($module.declaredPortTypeIds)) {
            if (-not $surfaceContext.contractByPortType.ContainsKey($portTypeId)) { throw "DIA-16 module $($module.moduleId) declares unknown port type: $portTypeId" }
            $contract = $surfaceContext.contractByPortType[$portTypeId]
            Assert-DialectModuleCompositionStringSet -Actual @($contract.targetModuleIds) -Expected @($module.moduleId) -Context "DIA-16 port declaration owner drifted for $portTypeId"
            $sourceBehaviorKeyIds.Add($contract.behaviorKeyId)
        }
        $dependencyModuleIds = @($module.dependencies.Keys)
        $modules.Add([pscustomobject][ordered]@{
                moduleId = $module.moduleId
                moduleVersion = $module.moduleVersion
                moduleApiVersion = $module.moduleApiVersion
                dependencyModuleIds = @($dependencyModuleIds)
                declaredPortTypeIds = @($module.declaredPortTypeIds)
                sourceBehaviorKeyIds = @(Get-DialectModuleCompositionStringSet -Values $sourceBehaviorKeyIds.ToArray() -Name "DIA-16 $($module.moduleId) source BehaviorKey" -AllowEmpty $true)
                descriptorStatus = 'StaticCandidate'
                runtimeStatus = 'NotImplemented'
                distributionEligibility = 'Blocked'
            })
    }
    foreach ($portTypeId in $surfaceContext.contractByPortType.Keys) {
        if (-not $catalogContext.portOwnerById.ContainsKey($portTypeId)) { throw "DIA-16 policy surface port has no module declaration: $portTypeId" }
    }

    $profileClosures = New-Object 'System.Collections.Generic.List[object]'
    foreach ($profileId in $packContext.packByProfile.Keys) {
        $pack = $packContext.packByProfile[$profileId]
        $selectedModuleIds = @(Get-DialectModuleCompositionStringSet -Values @($pack.builtInModuleIds) -Name "DIA-16 $profileId selected module")
        $resolvedModuleIds = @(Get-DialectModuleCompositionResolvedClosure -SelectedModuleIds $selectedModuleIds -ModuleById $catalogContext.moduleById)
        $profileClosures.Add([pscustomobject][ordered]@{
                profileId = $profileId
                packId = $pack.packId
                legacyCoreProfileEnum = $pack.legacyCoreProfileEnum
                selectedModuleIds = @($selectedModuleIds)
                resolvedModuleIds = @($resolvedModuleIds)
                profileStatus = 'StaticCandidate'
                runtimeStatus = 'NotImplemented'
            })
    }
    $compositionCounts = [ordered]@{
        moduleDescriptorCount = [int]$modules.Count
        dependencyEdgeCount = [int]$catalogContext.edgeCount
        portDeclarationCount = [int]$catalogContext.portOwnerById.Count
        profileClosureCount = [int]$profileClosures.Count
    }
    if ($compositionCounts.moduleDescriptorCount -ne 2 -or $compositionCounts.dependencyEdgeCount -ne 1 -or $compositionCounts.portDeclarationCount -ne 10 -or $compositionCounts.profileClosureCount -ne 2) {
        throw 'DIA-16 expected static composition counts drifted.'
    }
    $currentRuntimeIsolation = [ordered]@{ status = 'Failed'; reason = 'DIA-16 only models static module composition; legacy profile and registries remain process-wide mutable state.' }
    $parserVmConsumption = [ordered]@{ status = 'NotConsumed'; reason = 'DIA-16 is an offline module composition report and is not read by the legacy Parser or VM.' }
    $compatibilityPlanRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-16 does not create a runtime CompatibilityPlan, DialectPlan, LegacySessionFacade, feature flag or frozen registry.' }
    $resolverRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-16 does not inspect game content, parse a manifest, pin a user choice or resolve a runtime candidate.' }
    $runtimeModuleCatalog = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-16 does not load an assembly, reflect a type, register a runtime module or bind a port implementation.' }
    $m1Eligibility = [ordered]@{ status = 'Blocked'; reason = 'Static module descriptors do not establish session isolation, generation guards, candidate commit or runtime capability binding.' }
    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'; workPackage = 'M0-DIA-16'
        sourceCompatibilityPackHash = $packContext.sourceCompatibilityPackHash; sourcePolicySurfaceHash = $surfaceContext.sourcePolicySurfaceHash
        catalogId = $catalogContext.catalogId; catalogVersion = $catalogContext.catalogVersion; catalogHash = $catalogContext.catalogHash
        compositionCounts = $compositionCounts; currentCompatibilityPackExposure = $packContext.currentCompatibilityPackExposure
        modules = $modules.ToArray(); profileClosures = $profileClosures.ToArray(); currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption; compatibilityPlanRuntime = $compatibilityPlanRuntime; resolverRuntime = $resolverRuntime
        runtimeModuleCatalog = $runtimeModuleCatalog; m1Eligibility = $m1Eligibility
    }
    $moduleCompositionSetHash = Get-DialectModuleCompositionCanonicalHash -Value $setPayload -Depth 100
    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-16'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourceCompatibilityPackHash = $packContext.sourceCompatibilityPackHash
        sourcePolicySurfaceHash = $surfaceContext.sourcePolicySurfaceHash
        catalogId = $catalogContext.catalogId
        catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash
        moduleCompositionSetHash = $moduleCompositionSetHash
        compositionCounts = $compositionCounts
        currentCompatibilityPackExposure = $packContext.currentCompatibilityPackExposure
        modules = $modules.ToArray()
        profileClosures = $profileClosures.ToArray()
        currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime
        resolverRuntime = $resolverRuntime
        runtimeModuleCatalog = $runtimeModuleCatalog
        m1Eligibility = $m1Eligibility
        uncovered = @(
            'DIA-16 proves only a source-backed static descriptor graph. It does not prove module behavior, dependency version semantics, port implementation, conflict resolution, save compatibility or distribution safety.',
            'The profile closures are derived from DIA-12 static packs only; they do not inspect content, parse a game manifest, calculate a fingerprint, resolve a user pin or select a runtime profile.',
            'No C# module assembly, reflection scan, runtime catalog, resolver, CompatibilityPlan, LegacySessionFacade, D2 frozen registry, Parser/VM consumption, game run, APK or device evidence is introduced.'
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, (($report | ConvertTo-Json -Depth 100) + "`n"), $script:DialectModuleCompositionUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectModuleCompositionReport
