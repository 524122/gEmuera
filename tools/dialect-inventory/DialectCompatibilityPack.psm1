Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# M0-DIA-12 validates declaration data only. It must never inspect a game
# directory, load a DLL, resolve a runtime plan, or wire Parser/VM behavior.
$script:DialectCompatibilityPackUtf8NoBom = New-Object Text.UTF8Encoding($false)
$script:DialectCompatibilityPackSupportedProfileIds = @('snake', 'v24pure')
$script:DialectCompatibilityPackForbiddenPayloadFields = @(
    'assemblypath', 'assembly', 'dllpath', 'pluginpath', 'typename', 'script',
    'callback', 'executablepayload', 'url', 'filepath', 'entrypoint'
)

function Get-DialectCompatibilityPackSha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-DialectCompatibilityPackCanonicalHash {
    param([Parameter(Mandatory = $true)][object]$Value, [int]$Depth = 40)

    return Get-DialectCompatibilityPackSha256Hex -Bytes $script:DialectCompatibilityPackUtf8NoBom.GetBytes(($Value | ConvertTo-Json -Depth $Depth -Compress))
}

function Assert-DialectCompatibilityPackHash {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid DIA-12 hash: $Name" }
}

function Assert-DialectCompatibilityPackSemanticVersion {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Invalid DIA-12 semantic version: $Name" }
}

function Get-DialectCompatibilityPackStringSet {
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

function Assert-DialectCompatibilityPackStringSet {
    param(
        [object[]]$Actual,
        [object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Context,
        [bool]$AllowEmpty = $false
    )

    $actualValues = @(Get-DialectCompatibilityPackStringSet -Values $Actual -Name "$Context actual values" -AllowEmpty $AllowEmpty)
    $expectedValues = @(Get-DialectCompatibilityPackStringSet -Values $Expected -Name "$Context expected values" -AllowEmpty $AllowEmpty)
    if ($actualValues.Count -ne $expectedValues.Count) { throw "$Context count drifted: expected $($expectedValues.Count), actual $($actualValues.Count)." }
    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -cne $expectedValues[$index]) { throw "$Context value drifted: expected $($expectedValues[$index]), actual $($actualValues[$index])." }
    }
}

function Assert-DialectCompatibilityPackObjectShape {
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

function Assert-DialectCompatibilityPackNoExecutablePayload {
    param([object]$Value, [Parameter(Mandatory = $true)][string]$Context)

    if ($null -eq $Value -or $Value -is [string] -or $Value -is [ValueType]) { return }
    if ($Value -is [System.Collections.IEnumerable]) {
        $index = 0
        foreach ($item in $Value) {
            Assert-DialectCompatibilityPackNoExecutablePayload -Value $item -Context "$Context[$index]"
            $index++
        }
        return
    }
    foreach ($property in @($Value.PSObject.Properties)) {
        $fieldName = $property.Name.ToLowerInvariant()
        if ($script:DialectCompatibilityPackForbiddenPayloadFields -contains $fieldName) {
            throw "DIA-12 forbids executable payload field: $Context.$($property.Name)"
        }
        Assert-DialectCompatibilityPackNoExecutablePayload -Value $property.Value -Context "$Context.$($property.Name)"
    }
}

function Assert-DialectCompatibilityPackSupportedProfileId {
    param([Parameter(Mandatory = $true)][string]$ProfileId)

    if ($ProfileId -ceq 'snake-modern-mobile') {
        throw 'Unsupported DIA-12 profile: snake-modern-mobile (SnakeModernMobile has no independent static projection evidence).'
    }
    if ($script:DialectCompatibilityPackSupportedProfileIds -notcontains $ProfileId) {
        throw "Unsupported DIA-12 profile: $ProfileId"
    }
}

function Get-DialectCompatibilityPackCatalogContext {
    param([Parameter(Mandatory = $true)][object]$Catalog)

    Assert-DialectCompatibilityPackNoExecutablePayload -Value $Catalog -Context 'catalog'
    $catalogFields = @('schemaVersion', 'catalogId', 'catalogVersion', 'sourceWorkPackage', 'sourcePlanPreflightWorkPackage', 'sourceProfileSelectionWorkPackage', 'sourcePlanPreflightHash', 'sourceProfileSelectionHash', 'compatibilityPackSchema', 'externalCodePolicy', 'allowedCapabilityIds', 'builtInModules', 'profileContracts', 'packs')
    Assert-DialectCompatibilityPackObjectShape -Value $Catalog -Required $catalogFields -Allowed $catalogFields -Context 'DIA-12 catalog'
    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or
        [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-12' -or
        [string]$Catalog.sourcePlanPreflightWorkPackage -cne 'M0-DIA-10' -or
        [string]$Catalog.sourceProfileSelectionWorkPackage -cne 'M0-DIA-11' -or
        [string]$Catalog.compatibilityPackSchema -cne 'emuera.compatibility-pack/v1' -or
        [string]$Catalog.externalCodePolicy -cne 'BuiltInCompiledOnly' -or
        [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported DIA-12 CompatibilityPack catalog.'
    }
    Assert-DialectCompatibilityPackSemanticVersion $Catalog.catalogVersion 'catalogVersion'
    Assert-DialectCompatibilityPackHash $Catalog.sourcePlanPreflightHash 'catalog sourcePlanPreflightHash'
    Assert-DialectCompatibilityPackHash $Catalog.sourceProfileSelectionHash 'catalog sourceProfileSelectionHash'

    $allowedCapabilityIds = @(Get-DialectCompatibilityPackStringSet -Values @($Catalog.allowedCapabilityIds) -Name 'DIA-12 allowed capability id' -AllowEmpty $true)
    $allowedCapabilitySet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($capabilityId in $allowedCapabilityIds) { [void]$allowedCapabilitySet.Add($capabilityId) }

    $moduleById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($module in @($Catalog.builtInModules)) {
        Assert-DialectCompatibilityPackNoExecutablePayload -Value $module -Context 'catalog.builtInModules'
        $moduleFields = @('id', 'declarationVersion', 'profileIds', 'payloadKind', 'distributionStatus')
        Assert-DialectCompatibilityPackObjectShape -Value $module -Required $moduleFields -Allowed $moduleFields -Context 'DIA-12 built-in module'
        $id = [string]$module.id
        if ($id -notmatch '^[a-z0-9][a-z0-9.\-]+$' -or $moduleById.ContainsKey($id)) { throw "Invalid or duplicate DIA-12 built-in module: $id" }
        Assert-DialectCompatibilityPackSemanticVersion $module.declarationVersion "built-in module $id declarationVersion"
        if ([string]$module.payloadKind -cne 'BuiltInCompiledOnly' -or [string]$module.distributionStatus -cne 'StaticEvidenceOnly') {
            throw "DIA-12 built-in module $id is not constrained to static built-in evidence."
        }
        $profileIds = @(Get-DialectCompatibilityPackStringSet -Values @($module.profileIds) -Name "built-in module $id profile id")
        foreach ($profileId in $profileIds) { Assert-DialectCompatibilityPackSupportedProfileId $profileId }
        $moduleById.Add($id, [pscustomobject][ordered]@{
                id = $id
                declarationVersion = [string]$module.declarationVersion
                profileIds = @($profileIds)
                payloadKind = 'BuiltInCompiledOnly'
                distributionStatus = 'StaticEvidenceOnly'
            })
    }
    if ($moduleById.Count -eq 0) { throw 'DIA-12 catalog must declare at least one built-in module.' }

    $contractByProfileId = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($contract in @($Catalog.profileContracts)) {
        Assert-DialectCompatibilityPackNoExecutablePayload -Value $contract -Context 'catalog.profileContracts'
        $contractFields = @('profileId', 'packId', 'legacyCoreProfileEnum', 'builtInModuleIds', 'projectionEvidenceModuleIds')
        Assert-DialectCompatibilityPackObjectShape -Value $contract -Required $contractFields -Allowed $contractFields -Context 'DIA-12 profile contract'
        $profileId = [string]$contract.profileId
        Assert-DialectCompatibilityPackSupportedProfileId $profileId
        if ($contractByProfileId.ContainsKey($profileId) -or [string]::IsNullOrWhiteSpace([string]$contract.packId)) { throw "Invalid or duplicate DIA-12 profile contract: $profileId" }
        $builtInModuleIds = @(Get-DialectCompatibilityPackStringSet -Values @($contract.builtInModuleIds) -Name "DIA-12 profile $profileId built-in module id")
        $projectionEvidenceModuleIds = @(Get-DialectCompatibilityPackStringSet -Values @($contract.projectionEvidenceModuleIds) -Name "DIA-12 profile $profileId projection evidence module id")
        $contractByProfileId.Add($profileId, [pscustomobject][ordered]@{
                profileId = $profileId
                packId = [string]$contract.packId
                legacyCoreProfileEnum = [string]$contract.legacyCoreProfileEnum
                builtInModuleIds = @($builtInModuleIds)
                projectionEvidenceModuleIds = @($projectionEvidenceModuleIds)
            })
    }
    Assert-DialectCompatibilityPackStringSet -Actual @($contractByProfileId.Keys) -Expected $script:DialectCompatibilityPackSupportedProfileIds -Context 'DIA-12 profile contract set'

    $packById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($pack in @($Catalog.packs)) {
        Assert-DialectCompatibilityPackNoExecutablePayload -Value $pack -Context 'catalog.packs'
        $packFields = @('packId', 'declarationVersion', 'profileId', 'builtInModuleIds', 'projectionEvidenceModuleIds', 'requiredCapabilities', 'optionalCapabilities', 'saveProfileEvidenceStatus', 'fixtureEvidenceStatus', 'contentBindingStatus', 'distributionEligibility')
        Assert-DialectCompatibilityPackObjectShape -Value $pack -Required $packFields -Allowed $packFields -Context 'DIA-12 CompatibilityPack'
        $packId = [string]$pack.packId
        if ([string]::IsNullOrWhiteSpace($packId) -or $packById.ContainsKey($packId)) { throw "Invalid or duplicate DIA-12 CompatibilityPack id: $packId" }
        Assert-DialectCompatibilityPackSemanticVersion $pack.declarationVersion "CompatibilityPack $packId declarationVersion"
        $profileId = [string]$pack.profileId
        Assert-DialectCompatibilityPackSupportedProfileId $profileId
        $builtInModuleIds = @(Get-DialectCompatibilityPackStringSet -Values @($pack.builtInModuleIds) -Name "CompatibilityPack $packId built-in module id")
        $projectionEvidenceModuleIds = @(Get-DialectCompatibilityPackStringSet -Values @($pack.projectionEvidenceModuleIds) -Name "CompatibilityPack $packId projection evidence module id")
        $requiredCapabilities = @(Get-DialectCompatibilityPackStringSet -Values @($pack.requiredCapabilities) -Name "CompatibilityPack $packId required capability" -AllowEmpty $true)
        $optionalCapabilities = @(Get-DialectCompatibilityPackStringSet -Values @($pack.optionalCapabilities) -Name "CompatibilityPack $packId optional capability" -AllowEmpty $true)
        foreach ($capabilityId in @($requiredCapabilities) + @($optionalCapabilities)) {
            if (-not $allowedCapabilitySet.Contains($capabilityId)) { throw "CompatibilityPack $packId requests unsupported capability: $capabilityId" }
        }
        foreach ($capabilityId in $requiredCapabilities) {
            if ($optionalCapabilities -contains $capabilityId) { throw "CompatibilityPack $packId declares capability as both required and optional: $capabilityId" }
        }
        if ([string]$pack.saveProfileEvidenceStatus -cne 'Uncovered' -or
            [string]$pack.fixtureEvidenceStatus -cne 'Uncovered' -or
            [string]$pack.contentBindingStatus -cne 'NotBound' -or
            [string]$pack.distributionEligibility -cne 'Blocked') {
            throw "CompatibilityPack $packId incorrectly advances runtime or distribution evidence."
        }
        $packById.Add($packId, [pscustomobject][ordered]@{
                packId = $packId
                declarationVersion = [string]$pack.declarationVersion
                profileId = $profileId
                builtInModuleIds = @($builtInModuleIds)
                projectionEvidenceModuleIds = @($projectionEvidenceModuleIds)
                requiredCapabilities = @($requiredCapabilities)
                optionalCapabilities = @($optionalCapabilities)
                saveProfileEvidenceStatus = 'Uncovered'
                fixtureEvidenceStatus = 'Uncovered'
                contentBindingStatus = 'NotBound'
                distributionEligibility = 'Blocked'
            })
    }
    if ($packById.Count -ne 2) { throw 'DIA-12 catalog must declare exactly two evidence-backed CompatibilityPacks.' }

    $canonicalModules = New-Object 'System.Collections.Generic.List[object]'
    foreach ($module in $moduleById.Values) {
        $canonicalModules.Add([ordered]@{
                id = $module.id; declarationVersion = $module.declarationVersion; profileIds = @($module.profileIds)
                payloadKind = $module.payloadKind; distributionStatus = $module.distributionStatus
            })
    }
    $canonicalContracts = New-Object 'System.Collections.Generic.List[object]'
    foreach ($contract in $contractByProfileId.Values) {
        $canonicalContracts.Add([ordered]@{
                profileId = $contract.profileId; packId = $contract.packId; legacyCoreProfileEnum = $contract.legacyCoreProfileEnum
                builtInModuleIds = @($contract.builtInModuleIds); projectionEvidenceModuleIds = @($contract.projectionEvidenceModuleIds)
            })
    }
    $canonicalPacks = New-Object 'System.Collections.Generic.List[object]'
    foreach ($pack in $packById.Values | Sort-Object profileId, packId) {
        $canonicalPacks.Add([ordered]@{
                packId = $pack.packId; declarationVersion = $pack.declarationVersion; profileId = $pack.profileId
                builtInModuleIds = @($pack.builtInModuleIds); projectionEvidenceModuleIds = @($pack.projectionEvidenceModuleIds)
                requiredCapabilities = @($pack.requiredCapabilities); optionalCapabilities = @($pack.optionalCapabilities)
                saveProfileEvidenceStatus = $pack.saveProfileEvidenceStatus; fixtureEvidenceStatus = $pack.fixtureEvidenceStatus
                contentBindingStatus = $pack.contentBindingStatus; distributionEligibility = $pack.distributionEligibility
            })
    }
    $canonicalCatalog = [ordered]@{
        schemaVersion = '1.0.0'; catalogId = [string]$Catalog.catalogId; catalogVersion = [string]$Catalog.catalogVersion
        sourceWorkPackage = 'M0-DIA-12'; sourcePlanPreflightWorkPackage = 'M0-DIA-10'; sourceProfileSelectionWorkPackage = 'M0-DIA-11'
        sourcePlanPreflightHash = [string]$Catalog.sourcePlanPreflightHash; sourceProfileSelectionHash = [string]$Catalog.sourceProfileSelectionHash
        compatibilityPackSchema = 'emuera.compatibility-pack/v1'; externalCodePolicy = 'BuiltInCompiledOnly'
        allowedCapabilityIds = @($allowedCapabilityIds); builtInModules = $canonicalModules.ToArray()
        profileContracts = $canonicalContracts.ToArray(); packs = $canonicalPacks.ToArray()
    }
    return [pscustomobject][ordered]@{
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = Get-DialectCompatibilityPackCanonicalHash -Value $canonicalCatalog
        sourcePlanPreflightHash = [string]$Catalog.sourcePlanPreflightHash
        sourceProfileSelectionHash = [string]$Catalog.sourceProfileSelectionHash
        compatibilityPackSchema = 'emuera.compatibility-pack/v1'
        externalCodePolicy = 'BuiltInCompiledOnly'
        allowedCapabilityIds = @($allowedCapabilityIds)
        moduleById = $moduleById
        contractByProfileId = $contractByProfileId
        packById = $packById
    }
}

function Assert-DialectCompatibilityPackPlanPreflight {
    param([Parameter(Mandatory = $true)][object]$PlanPreflight)

    if ([string]$PlanPreflight.schemaVersion -cne '1.0.0' -or [string]$PlanPreflight.workPackage -cne 'M0-DIA-10') { throw 'DIA-12 requires M0-DIA-10 plan preflight evidence.' }
    Assert-DialectCompatibilityPackHash $PlanPreflight.preflightSetHash 'M0-DIA-10 preflightSetHash'
    if ([string]$PlanPreflight.executionStatus -cne 'InProgress' -or [string]$PlanPreflight.gateStatus -cne 'Blocked' -or [string]$PlanPreflight.blockerCode -cne 'EvidenceMissing' -or [string]$PlanPreflight.result -cne 'Partial') { throw 'DIA-10 evidence status was incorrectly advanced.' }
    if ([string]$PlanPreflight.currentRuntimeIsolation.status -cne 'Failed') { throw 'DIA-10 current runtime isolation must remain Failed.' }
    if ([string]$PlanPreflight.parserVmConsumption.status -cne 'NotConsumed') { throw 'DIA-10 parser/VM consumption must remain NotConsumed.' }
    if ([string]$PlanPreflight.compatibilityPlanRuntime.status -cne 'NotImplemented') { throw 'DIA-12 runtime compatibility plan status must remain NotImplemented.' }
    if ([string]$PlanPreflight.m1Eligibility.status -cne 'Blocked') { throw 'DIA-10 M1 eligibility must remain Blocked.' }
    $profiles = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($profile in @($PlanPreflight.profiles)) {
        $profileId = [string]$profile.profileId
        if ([string]::IsNullOrWhiteSpace($profileId) -or $profiles.ContainsKey($profileId)) { throw "Invalid DIA-10 profile evidence: $profileId" }
        Assert-DialectCompatibilityPackSupportedProfileId $profileId
        if ([string]::IsNullOrWhiteSpace([string]$profile.legacyCoreProfileEnum) -or $null -eq $profile.registryEvidence) { throw "Incomplete DIA-10 profile evidence: $profileId" }
        $projectionIds = @(Get-DialectCompatibilityPackStringSet -Values @($profile.registryEvidence.selectedTestProjectionModuleIds) -Name "DIA-10 $profileId projection module")
        if ([int]$profile.registryEvidence.instructionCount -le 0 -or [int]$profile.registryEvidence.expressionFunctionCount -le 0) { throw "Invalid DIA-10 registry counts: $profileId" }
        $profiles.Add($profileId, $profile)
    }
    Assert-DialectCompatibilityPackStringSet -Actual @($profiles.Keys) -Expected $script:DialectCompatibilityPackSupportedProfileIds -Context 'DIA-10 evidence profile set'
    $modern = @($PlanPreflight.unsupportedLegacyCoreProfiles | Where-Object { $_.legacyCoreProfileEnum -ceq 'SnakeModernMobile' -and $_.status -ceq 'Uncovered' })
    if ($modern.Count -ne 1) { throw 'DIA-10 must keep SnakeModernMobile Uncovered.' }
    return $profiles
}

function Assert-DialectCompatibilityPackProfileSelection {
    param(
        [Parameter(Mandatory = $true)][object]$ProfileSelection,
        [Parameter(Mandatory = $true)][object]$PlanPreflight
    )

    if ([string]$ProfileSelection.schemaVersion -cne '1.0.0' -or [string]$ProfileSelection.workPackage -cne 'M0-DIA-11') { throw 'DIA-12 requires M0-DIA-11 profile selection evidence.' }
    Assert-DialectCompatibilityPackHash $ProfileSelection.selectionSetHash 'M0-DIA-11 selectionSetHash'
    if ([string]$ProfileSelection.sourcePlanPreflightHash -cne [string]$PlanPreflight.preflightSetHash) { throw 'DIA-11 source plan preflight hash does not match DIA-10 evidence.' }
    if ([string]$ProfileSelection.executionStatus -cne 'InProgress' -or [string]$ProfileSelection.gateStatus -cne 'Blocked' -or [string]$ProfileSelection.blockerCode -cne 'EvidenceMissing' -or [string]$ProfileSelection.result -cne 'Partial') { throw 'DIA-11 evidence status was incorrectly advanced.' }
    if ([string]$ProfileSelection.currentRuntimeIsolation.status -cne 'Failed') { throw 'DIA-11 current runtime isolation must remain Failed.' }
    if ([string]$ProfileSelection.parserVmConsumption.status -cne 'NotConsumed') { throw 'DIA-11 parser/VM consumption must remain NotConsumed.' }
    if ([string]$ProfileSelection.compatibilityPlanRuntime.status -cne 'NotImplemented') { throw 'DIA-11 runtime compatibility plan status must remain NotImplemented.' }
    if ([string]$ProfileSelection.m1Eligibility.status -cne 'Blocked') { throw 'DIA-11 M1 eligibility must remain Blocked.' }

    $profileByLauncherId = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $modern = @($ProfileSelection.legacyCoreProfiles | Where-Object { $_.legacyCoreProfileEnum -ceq 'SnakeModernMobile' -and $_.preflightEvidenceStatus -ceq 'Uncovered' -and $_.runnerProfileStatus -ceq 'Unsupported' })
    if ($modern.Count -ne 1) { throw 'DIA-11 must retain SnakeModernMobile as Uncovered and runner Unsupported.' }
    foreach ($profile in @($ProfileSelection.legacyCoreProfiles)) {
        $launcherProfileId = [string]$profile.launcherProfileId
        if ([string]::IsNullOrWhiteSpace($launcherProfileId)) { continue }
        Assert-DialectCompatibilityPackSupportedProfileId $launcherProfileId
        if ($profileByLauncherId.ContainsKey($launcherProfileId)) { throw "Duplicate DIA-11 launcher profile evidence: $launcherProfileId" }
        if ([string]$profile.preflightEvidenceStatus -cne 'EvidenceBacked' -or [string]$profile.runnerProfileStatus -cne 'Supported') { throw "DIA-11 launcher profile is not evidence-backed: $launcherProfileId" }
        $profileByLauncherId.Add($launcherProfileId, $profile)
    }
    Assert-DialectCompatibilityPackStringSet -Actual @($profileByLauncherId.Keys) -Expected $script:DialectCompatibilityPackSupportedProfileIds -Context 'DIA-11 launcher evidence profile set'
    return $profileByLauncherId
}

function New-DialectCompatibilityPackReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][object]$PlanPreflight,
        [Parameter(Mandatory = $true, Position = 1)][object]$ProfileSelection,
        [Parameter(Mandatory = $true, Position = 2)][object]$Catalog,
        [string]$OutputPath = ''
    )

    $preflightProfiles = Assert-DialectCompatibilityPackPlanPreflight -PlanPreflight $PlanPreflight
    $selectionProfiles = Assert-DialectCompatibilityPackProfileSelection -ProfileSelection $ProfileSelection -PlanPreflight $PlanPreflight
    $catalogContext = Get-DialectCompatibilityPackCatalogContext -Catalog $Catalog
    if ($catalogContext.sourcePlanPreflightHash -cne [string]$PlanPreflight.preflightSetHash) { throw 'DIA-12 source plan preflight hash drifted.' }
    if ($catalogContext.sourceProfileSelectionHash -cne [string]$ProfileSelection.selectionSetHash) { throw 'DIA-12 source profile selection hash drifted.' }

    $packReports = New-Object 'System.Collections.Generic.List[object]'
    foreach ($profileId in $catalogContext.contractByProfileId.Keys) {
        $contract = $catalogContext.contractByProfileId[$profileId]
        $preflightProfile = $preflightProfiles[$profileId]
        $selectionProfile = $selectionProfiles[$profileId]
        if ($null -eq $preflightProfile -or $null -eq $selectionProfile) { throw "DIA-12 lacks evidence-backed profile: $profileId" }
        if ([string]$contract.legacyCoreProfileEnum -cne [string]$preflightProfile.legacyCoreProfileEnum -or
            [string]$selectionProfile.legacyCoreProfileEnum -cne [string]$preflightProfile.legacyCoreProfileEnum) {
            throw "DIA-12 legacy CoreProfile mapping drifted: $profileId"
        }

        $projectionModuleIds = @(Get-DialectCompatibilityPackStringSet -Values @($preflightProfile.registryEvidence.selectedTestProjectionModuleIds) -Name "DIA-10 $profileId projection module")
        Assert-DialectCompatibilityPackStringSet -Actual @($contract.projectionEvidenceModuleIds) -Expected $projectionModuleIds -Context "DIA-12 profile $profileId projection evidence module set"
        $concreteBuiltInModuleIds = @($projectionModuleIds | Where-Object { -not $_.StartsWith('legacy.current.', [StringComparison]::Ordinal) })
        Assert-DialectCompatibilityPackStringSet -Actual @($contract.builtInModuleIds) -Expected $concreteBuiltInModuleIds -Context "DIA-12 profile $profileId built-in module set"
        foreach ($moduleId in @($contract.builtInModuleIds)) {
            if (-not $catalogContext.moduleById.ContainsKey($moduleId)) { throw "DIA-12 profile $profileId references unverified built-in module: $moduleId" }
            $module = $catalogContext.moduleById[$moduleId]
            if ($module.profileIds -notcontains $profileId) { throw "DIA-12 built-in module $moduleId is not evidence-backed for profile: $profileId" }
            if ($projectionModuleIds -notcontains $moduleId) { throw "DIA-12 built-in module $moduleId is absent from DIA-10 projection: $profileId" }
        }
        if (-not $catalogContext.packById.ContainsKey($contract.packId)) { throw "DIA-12 profile $profileId has no CompatibilityPack declaration: $($contract.packId)" }
        $pack = $catalogContext.packById[$contract.packId]
        if ($pack.profileId -cne $profileId) { throw "DIA-12 CompatibilityPack profile mismatch: $($pack.packId)" }
        Assert-DialectCompatibilityPackStringSet -Actual @($pack.builtInModuleIds) -Expected @($contract.builtInModuleIds) -Context "DIA-12 CompatibilityPack $($pack.packId) built-in module set"
        Assert-DialectCompatibilityPackStringSet -Actual @($pack.projectionEvidenceModuleIds) -Expected @($contract.projectionEvidenceModuleIds) -Context "DIA-12 CompatibilityPack $($pack.packId) projection evidence module set"

        $staticProjectionSupportModuleIds = @($projectionModuleIds | Where-Object { $_.StartsWith('legacy.current.', [StringComparison]::Ordinal) })
        $packReports.Add([pscustomobject][ordered]@{
                packId = $pack.packId
                declarationVersion = $pack.declarationVersion
                profileId = $profileId
                legacyCoreProfileEnum = [string]$preflightProfile.legacyCoreProfileEnum
                builtInModuleIds = @($pack.builtInModuleIds)
                staticProjectionSupportModuleIds = @($staticProjectionSupportModuleIds)
                requiredCapabilityIds = @($pack.requiredCapabilities)
                optionalCapabilityIds = @($pack.optionalCapabilities)
                contentBindingStatus = 'NotBound'
                saveProfileEvidenceStatus = 'Uncovered'
                fixtureEvidenceStatus = 'Uncovered'
                distributionEligibility = 'Blocked'
            })
    }

    $declarationPolicy = [ordered]@{
        compatibilityPackSchema = $catalogContext.compatibilityPackSchema
        externalCodePolicy = $catalogContext.externalCodePolicy
        allowedCapabilityIds = @($catalogContext.allowedCapabilityIds)
        contentBindingStatus = 'NotBound'
        runtimeModuleLoading = 'NotImplemented'
        runtimeResolverStatus = 'NotImplemented'
    }
    $currentRuntimeIsolation = [ordered]@{ status = 'Failed'; reason = 'CoreProfile and legacy registries remain process-wide; DIA-12 only validates static CompatibilityPack declarations.' }
    $parserVmConsumption = [ordered]@{ status = 'NotConsumed'; reason = 'DIA-12 is an offline catalog/report and is not read by the legacy Parser or VM.' }
    $compatibilityPlanRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-12 does not create a runtime CompatibilityPlan, LegacySessionFacade, feature flag or frozen registry.' }
    $resolverRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-12 does not inspect game content, calculate fingerprints, read manifests from games or resolve a runtime candidate.' }
    $m1Eligibility = [ordered]@{ status = 'Blocked'; reason = 'M0-DIA-10/11 remain static evidence; declaration validation does not isolate a session.' }
    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'; workPackage = 'M0-DIA-12'
        sourcePlanPreflightHash = [string]$PlanPreflight.preflightSetHash
        sourceProfileSelectionHash = [string]$ProfileSelection.selectionSetHash
        catalogId = $catalogContext.catalogId; catalogVersion = $catalogContext.catalogVersion; catalogHash = $catalogContext.catalogHash
        declarationPolicy = $declarationPolicy; packs = $packReports.ToArray()
        currentRuntimeIsolation = $currentRuntimeIsolation; parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime; resolverRuntime = $resolverRuntime; m1Eligibility = $m1Eligibility
    }
    $compatibilityPackSetHash = Get-DialectCompatibilityPackCanonicalHash -Value $setPayload -Depth 50
    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-12'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourcePlanPreflightHash = [string]$PlanPreflight.preflightSetHash
        sourceProfileSelectionHash = [string]$ProfileSelection.selectionSetHash
        catalogId = $catalogContext.catalogId
        catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash
        compatibilityPackSetHash = $compatibilityPackSetHash
        declarationPolicy = $declarationPolicy
        packs = $packReports.ToArray()
        currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime
        resolverRuntime = $resolverRuntime
        m1Eligibility = $m1Eligibility
        uncovered = @(
            'DIA-12 validates only an offline declaration catalog; no game directory, game manifest, package fingerprint or user pin is inspected.',
            'Only v24pure and snake consume evidence-backed static projections. SnakeModernMobile remains explicitly unsupported by this contract.',
            'Declared capability arrays are intentionally explicit and empty because M0 has not yet proven runtime capability bindings, save profiles or behavior fixtures.',
            'No runtime CompatibilityPlan, resolver, LegacySessionFacade, feature flag, Parser/VM consumption, D2 frozen registry, game run, APK or device evidence is introduced.'
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, (($report | ConvertTo-Json -Depth 50) + "`n"), $script:DialectCompatibilityPackUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectCompatibilityPackReport
