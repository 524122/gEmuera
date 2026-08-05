Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# M0-DIA-10 deliberately emits evidence DTOs only.  It does not create a
# runtime CompatibilityPlan, instantiate handlers, or read the Parser/VM.
$script:DialectPlanPreflightUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-DialectPlanPreflightSha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-DialectPlanPreflightCanonicalHash {
    param([Parameter(Mandatory = $true)][object]$Value, [int]$Depth = 30)

    $json = $Value | ConvertTo-Json -Depth $Depth -Compress
    return Get-DialectPlanPreflightSha256Hex -Bytes $script:DialectPlanPreflightUtf8NoBom.GetBytes($json)
}

function Assert-DialectPlanPreflightHash {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9a-f]{64}$') {
        throw "Invalid DIA-10 hash: $Name"
    }
}

function Get-DialectPlanPreflightSortedStrings {
    param(
        [object[]]$Values,
        [Parameter(Mandatory = $true)][string]$Name,
        [switch]$AllowEmpty
    )

    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($value in @($Values)) {
        if ($null -eq $value) {
            throw "$Name contains a null value."
        }
        $text = [string]$value
        if ([string]::IsNullOrWhiteSpace($text)) {
            throw "$Name contains an empty value."
        }
        if (-not $set.Add($text)) {
            throw "Duplicate $Name value: $text"
        }
    }
    if (-not $AllowEmpty -and $set.Count -eq 0) {
        throw "$Name must not be empty."
    }

    $result = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in $set) {
        $result.Add($item)
    }
    return $result.ToArray()
}

function Assert-DialectPlanPreflightStringSet {
    param(
        [object[]]$Actual,
        [object[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Context
    )

    $actualValues = @(Get-DialectPlanPreflightSortedStrings -Values $Actual -Name "$Context actual values")
    $expectedValues = @(Get-DialectPlanPreflightSortedStrings -Values $Expected -Name "$Context expected values")
    if ($actualValues.Count -ne $expectedValues.Count) {
        throw "$Context count drifted: expected $($expectedValues.Count), actual $($actualValues.Count)."
    }
    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -cne $expectedValues[$index]) {
            throw "$Context value drifted: expected $($expectedValues[$index]), actual $($actualValues[$index])."
        }
    }
}

function ConvertTo-DialectPlanPreflightRequestProfileId {
    param([Parameter(Mandatory = $true)][string]$LegacyCoreProfileEnum)

    return ([regex]::Replace($LegacyCoreProfileEnum, '(?<!^)([A-Z])', '-$1')).ToLowerInvariant()
}

function Get-DialectPlanPreflightCatalogContext {
    param([Parameter(Mandatory = $true)][object]$Catalog)

    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or
        [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-10' -or
        [string]$Catalog.sourceRegistrySnapshotWorkPackage -cne 'M0-DIA-02' -or
        [string]$Catalog.sourceSessionInventoryWorkPackage -cne 'M0-SES-01' -or
        [string]$Catalog.catalogVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
        [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported DIA-10 dialect plan preflight catalog.'
    }

    $supportedSelectionSources = @(Get-DialectPlanPreflightSortedStrings -Values @($Catalog.supportedSelectionSources) -Name 'DIA-10 supported selection source')
    $selectionSourceSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($source in $supportedSelectionSources) {
        [void]$selectionSourceSet.Add($source)
    }
    foreach ($requiredSource in @('StaticContract', 'LegacyLauncherManual', 'M0RunnerFixture')) {
        if (-not $selectionSourceSet.Contains($requiredSource)) {
            throw "DIA-10 catalog is missing required selection source: $requiredSource"
        }
    }

    $profilesById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($profile in @($Catalog.profiles)) {
        $profileId = [string]$profile.profileId
        if ([string]::IsNullOrWhiteSpace($profileId)) {
            throw 'DIA-10 catalog contains an empty profile id.'
        }
        if ($profilesById.ContainsKey($profileId)) {
            throw "Duplicate DIA-10 catalog profile id: $profileId"
        }
        $legacyCoreProfileEnum = [string]$profile.legacyCoreProfileEnum
        $registryProjectionId = [string]$profile.registryProjectionId
        if ([string]::IsNullOrWhiteSpace($legacyCoreProfileEnum) -or [string]::IsNullOrWhiteSpace($registryProjectionId)) {
            throw "DIA-10 catalog profile is missing a legacy enum or registry projection: $profileId"
        }
        if ($null -eq $profile.expected -or [int]$profile.expected.instructionCount -lt 0 -or [int]$profile.expected.expressionFunctionCount -lt 0) {
            throw "Invalid DIA-10 catalog expected counts: $profileId"
        }
        $expectedModuleIds = @(Get-DialectPlanPreflightSortedStrings -Values @($profile.expected.selectedTestProjectionModuleIds) -Name "DIA-10 expected selected test projection module for $profileId")
        $profilesById.Add($profileId, [pscustomobject][ordered]@{
            profileId = $profileId
            legacyCoreProfileEnum = $legacyCoreProfileEnum
            registryProjectionId = $registryProjectionId
            expectedInstructionCount = [int]$profile.expected.instructionCount
            expectedExpressionFunctionCount = [int]$profile.expected.expressionFunctionCount
            expectedSelectedTestProjectionModuleIds = $expectedModuleIds
        })
    }

    if ($profilesById.Count -ne 2 -or -not $profilesById.ContainsKey('v24pure') -or -not $profilesById.ContainsKey('snake')) {
        throw 'DIA-10 catalog must contain exactly the v24pure and snake evidence-backed profiles.'
    }
    $expectedProfileMappings = @(
        [pscustomobject]@{ profileId = 'v24pure'; legacyCoreProfileEnum = 'V24Pure'; registryProjectionId = 'v24' },
        [pscustomobject]@{ profileId = 'snake'; legacyCoreProfileEnum = 'Snake'; registryProjectionId = 'snake' }
    )
    foreach ($expectedProfile in $expectedProfileMappings) {
        $actualProfile = $profilesById[$expectedProfile.profileId]
        if ($actualProfile.legacyCoreProfileEnum -cne $expectedProfile.legacyCoreProfileEnum -or
            $actualProfile.registryProjectionId -cne $expectedProfile.registryProjectionId) {
            throw "DIA-10 catalog profile mapping drifted: $($expectedProfile.profileId)"
        }
    }

    $unsupportedByEnum = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $unsupportedByRequestProfileId = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($unsupportedProfile in @($Catalog.unsupportedLegacyCoreProfiles)) {
        $legacyCoreProfileEnum = [string]$unsupportedProfile.legacyCoreProfileEnum
        $status = [string]$unsupportedProfile.status
        $reason = [string]$unsupportedProfile.reason
        if ([string]::IsNullOrWhiteSpace($legacyCoreProfileEnum) -or $status -cne 'Uncovered' -or [string]::IsNullOrWhiteSpace($reason)) {
            throw 'Invalid DIA-10 unsupported legacy core profile declaration.'
        }
        if ($unsupportedByEnum.ContainsKey($legacyCoreProfileEnum)) {
            throw "Duplicate DIA-10 unsupported legacy core profile: $legacyCoreProfileEnum"
        }
        $requestProfileId = ConvertTo-DialectPlanPreflightRequestProfileId -LegacyCoreProfileEnum $legacyCoreProfileEnum
        if ($unsupportedByRequestProfileId.ContainsKey($requestProfileId)) {
            throw "Duplicate DIA-10 unsupported request profile id: $requestProfileId"
        }
        $item = [pscustomobject][ordered]@{
            legacyCoreProfileEnum = $legacyCoreProfileEnum
            requestProfileId = $requestProfileId
            status = 'Uncovered'
            reason = $reason
        }
        $unsupportedByEnum.Add($legacyCoreProfileEnum, $item)
        $unsupportedByRequestProfileId.Add($requestProfileId, $item)
    }
    if (-not $unsupportedByEnum.ContainsKey('SnakeModernMobile')) {
        throw 'DIA-10 catalog must explicitly keep SnakeModernMobile Uncovered.'
    }

    $canonicalProfiles = New-Object 'System.Collections.Generic.List[object]'
    foreach ($profileId in $profilesById.Keys) {
        $profile = $profilesById[$profileId]
        $canonicalProfiles.Add([ordered]@{
            profileId = $profile.profileId
            legacyCoreProfileEnum = $profile.legacyCoreProfileEnum
            registryProjectionId = $profile.registryProjectionId
            expected = [ordered]@{
                instructionCount = $profile.expectedInstructionCount
                expressionFunctionCount = $profile.expectedExpressionFunctionCount
                selectedTestProjectionModuleIds = @($profile.expectedSelectedTestProjectionModuleIds)
            }
        })
    }
    $canonicalUnsupportedProfiles = New-Object 'System.Collections.Generic.List[object]'
    foreach ($legacyCoreProfileEnum in $unsupportedByEnum.Keys) {
        $unsupportedProfile = $unsupportedByEnum[$legacyCoreProfileEnum]
        $canonicalUnsupportedProfiles.Add([ordered]@{
            legacyCoreProfileEnum = $unsupportedProfile.legacyCoreProfileEnum
            status = $unsupportedProfile.status
            reason = $unsupportedProfile.reason
        })
    }
    $catalogCanonical = [ordered]@{
        schemaVersion = '1.0.0'
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        sourceWorkPackage = 'M0-DIA-10'
        sourceRegistrySnapshotWorkPackage = 'M0-DIA-02'
        sourceSessionInventoryWorkPackage = 'M0-SES-01'
        supportedSelectionSources = @($supportedSelectionSources)
        profiles = $canonicalProfiles.ToArray()
        unsupportedLegacyCoreProfiles = $canonicalUnsupportedProfiles.ToArray()
    }

    return [pscustomobject][ordered]@{
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = Get-DialectPlanPreflightCanonicalHash -Value $catalogCanonical
        profilesById = $profilesById
        supportedSelectionSources = @($supportedSelectionSources)
        selectionSourceSet = $selectionSourceSet
        unsupportedByEnum = $unsupportedByEnum
        unsupportedByRequestProfileId = $unsupportedByRequestProfileId
        canonicalUnsupportedProfiles = $canonicalUnsupportedProfiles.ToArray()
    }
}

function Assert-DialectPlanPreflightRegistrySnapshot {
    param([Parameter(Mandatory = $true)][object]$RegistrySnapshot)

    if ([string]$RegistrySnapshot.schemaVersion -cne '1.0.0' -or [string]$RegistrySnapshot.workPackage -cne 'M0-DIA-02') {
        throw 'DIA-10 requires M0-DIA-02 registry snapshots.'
    }
    Assert-DialectPlanPreflightHash $RegistrySnapshot.snapshotSetHash 'DIA-02 snapshotSetHash'
    Assert-DialectPlanPreflightHash $RegistrySnapshot.sourceInventoryHash 'DIA-02 sourceInventoryHash'
    if ([string]$RegistrySnapshot.executionStatus -cne 'InProgress' -or
        [string]$RegistrySnapshot.gateStatus -cne 'Blocked' -or
        [string]$RegistrySnapshot.blockerCode -cne 'EvidenceMissing' -or
        [string]$RegistrySnapshot.result -cne 'Partial') {
        throw 'DIA-02 static evidence status was incorrectly advanced.'
    }
    if ([string]$RegistrySnapshot.testProjectionInvariant.status -cne 'Passed') {
        throw 'DIA-02 test projection invariant must remain Passed.'
    }
    if ([string]$RegistrySnapshot.currentRuntimeIsolation.status -cne 'Failed') {
        throw 'DIA-02 current runtime isolation must remain Failed.'
    }
    foreach ($projectionId in @('v24', 'snake')) {
        $profile = $RegistrySnapshot.profiles.$projectionId
        if ($null -eq $profile) {
            throw "DIA-02 registry projection is missing: $projectionId"
        }
        if ([string]$profile.sourceInventoryHash -cne [string]$RegistrySnapshot.sourceInventoryHash) {
            throw "DIA-02 profile source inventory hash does not match the registry source inventory hash: $projectionId"
        }
        Assert-DialectPlanPreflightHash $profile.sourceInventoryHash "DIA-02 $projectionId profile sourceInventoryHash"
        Assert-DialectPlanPreflightHash $profile.canonicalHash "DIA-02 $projectionId profile canonicalHash"
    }
}

function Assert-DialectPlanPreflightSessionInventory {
    param([Parameter(Mandatory = $true)][object]$SessionInventory)

    if ([string]$SessionInventory.schemaVersion -cne '1.0.0' -or [string]$SessionInventory.workPackage -cne 'M0-SES-01') {
        throw 'DIA-10 requires M0-SES-01 session inventory.'
    }
    Assert-DialectPlanPreflightHash $SessionInventory.inventorySetHash 'M0-SES-01 inventorySetHash'
    if ([string]$SessionInventory.executionStatus -cne 'InProgress' -or
        [string]$SessionInventory.gateStatus -cne 'Blocked' -or
        [string]$SessionInventory.blockerCode -cne 'EvidenceMissing' -or
        [string]$SessionInventory.result -cne 'Partial') {
        throw 'M0-SES-01 static evidence status was incorrectly advanced.'
    }
    if ([string]$SessionInventory.currentRuntimeIsolation.status -cne 'Failed') {
        throw 'M0-SES-01 current runtime isolation must remain Failed.'
    }
    if ([string]$SessionInventory.parserVmConsumption.status -cne 'NotConsumed') {
        throw 'M0-SES-01 parser/VM consumption must remain NotConsumed.'
    }
    if ([string]$SessionInventory.m1Eligibility.status -cne 'Blocked') {
        throw 'M1 eligibility must remain Blocked.'
    }
    if ($null -eq $SessionInventory.coverage -or [int]$SessionInventory.coverage.observedStateCount -le 0) {
        throw 'M0-SES-01 observed root-state coverage is missing.'
    }
}

function Get-DialectPlanPreflightRegistryEvidence {
    param(
        [Parameter(Mandatory = $true)][object]$RegistrySnapshot,
        [Parameter(Mandatory = $true)][object]$ProfileContext
    )

    $projection = $RegistrySnapshot.profiles.($ProfileContext.registryProjectionId)
    if ($null -eq $projection) {
        throw "DIA-02 registry projection is missing: $($ProfileContext.registryProjectionId)"
    }
    $expectedLegacyProjectionId = $ProfileContext.registryProjectionId + '-projection'
    if ([string]$projection.profileId -cne $expectedLegacyProjectionId) {
        throw "DIA-02 registry projection identity drifted: $($ProfileContext.registryProjectionId)"
    }

    $selectedModuleIds = @(Get-DialectPlanPreflightSortedStrings -Values @($projection.selectedModuleIds) -Name "DIA-02 selected module for $($ProfileContext.registryProjectionId)")
    Assert-DialectPlanPreflightStringSet -Actual $selectedModuleIds -Expected @($ProfileContext.expectedSelectedTestProjectionModuleIds) -Context "DIA-10 selected test projection modules for $($ProfileContext.profileId)"

    $instructionCount = [int]$projection.instructionCount
    $expressionFunctionCount = [int]$projection.expressionFunctionCount
    if ($instructionCount -ne [int]$ProfileContext.expectedInstructionCount) {
        throw "DIA-10 expected instruction count drifted for $($ProfileContext.profileId): expected $($ProfileContext.expectedInstructionCount), actual $instructionCount."
    }
    if ($expressionFunctionCount -ne [int]$ProfileContext.expectedExpressionFunctionCount) {
        throw "DIA-10 expected expression function count drifted for $($ProfileContext.profileId): expected $($ProfileContext.expectedExpressionFunctionCount), actual $expressionFunctionCount."
    }
    if (@($projection.instructions).Count -ne $instructionCount -or @($projection.expressionFunctions).Count -ne $expressionFunctionCount) {
        throw "DIA-02 registry projection entry count does not match its static count: $($ProfileContext.registryProjectionId)"
    }

    return [pscustomobject][ordered]@{
        sourceWorkPackage = 'M0-DIA-02'
        sourceRegistrySnapshotHash = [string]$RegistrySnapshot.snapshotSetHash
        sourceInventoryHash = [string]$RegistrySnapshot.sourceInventoryHash
        registryProjectionId = [string]$ProfileContext.registryProjectionId
        projectionCanonicalHash = [string]$projection.canonicalHash
        selectedTestProjectionModuleIds = @($selectedModuleIds)
        instructionCount = $instructionCount
        expressionFunctionCount = $expressionFunctionCount
    }
}

function New-LegacyDialectPlanPreflight {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][object]$RegistrySnapshot,
        [Parameter(Mandatory = $true, Position = 1)][object]$SessionInventory,
        [Parameter(Mandatory = $true, Position = 2)][object]$Catalog,
        [Parameter(Mandatory = $true, Position = 3)][ValidateNotNullOrEmpty()][string]$ProfileId,
        [Parameter(Mandatory = $true, Position = 4)][ValidateNotNullOrEmpty()][string]$SelectionSource,
        [Parameter(Mandatory = $true, Position = 5)][int]$RequestedGeneration
    )

    if ($RequestedGeneration -lt 0) {
        throw 'DIA-10 requested generation must be zero or greater.'
    }
    Assert-DialectPlanPreflightRegistrySnapshot -RegistrySnapshot $RegistrySnapshot
    Assert-DialectPlanPreflightSessionInventory -SessionInventory $SessionInventory
    $catalogContext = Get-DialectPlanPreflightCatalogContext -Catalog $Catalog

    if (-not $catalogContext.selectionSourceSet.Contains($SelectionSource)) {
        throw "Unsupported DIA-10 selection source: $SelectionSource"
    }
    if (-not $catalogContext.profilesById.ContainsKey($ProfileId)) {
        $unsupportedProfile = $null
        if ($catalogContext.unsupportedByRequestProfileId.ContainsKey($ProfileId)) {
            $unsupportedProfile = $catalogContext.unsupportedByRequestProfileId[$ProfileId]
        }
        elseif ($catalogContext.unsupportedByEnum.ContainsKey($ProfileId)) {
            $unsupportedProfile = $catalogContext.unsupportedByEnum[$ProfileId]
        }
        if ($null -ne $unsupportedProfile) {
            throw "Unsupported legacy profile for static preflight: $($unsupportedProfile.legacyCoreProfileEnum) is $($unsupportedProfile.status) because no evidence-backed DIA-02 projection exists."
        }
        throw "Unsupported legacy profile: $ProfileId"
    }

    $profileContext = $catalogContext.profilesById[$ProfileId]
    $registryEvidence = Get-DialectPlanPreflightRegistryEvidence -RegistrySnapshot $RegistrySnapshot -ProfileContext $profileContext
    $planSemanticPayload = [ordered]@{
        schemaVersion = '1.0.0'
        profileId = [string]$profileContext.profileId
        legacyCoreProfileEnum = [string]$profileContext.legacyCoreProfileEnum
        registryProjectionId = [string]$registryEvidence.registryProjectionId
        registryProjectionCanonicalHash = [string]$registryEvidence.projectionCanonicalHash
        selectedTestProjectionModuleIds = @($registryEvidence.selectedTestProjectionModuleIds)
        instructionCount = [int]$registryEvidence.instructionCount
        expressionFunctionCount = [int]$registryEvidence.expressionFunctionCount
    }
    $planSemanticHash = Get-DialectPlanPreflightCanonicalHash -Value $planSemanticPayload

    return [pscustomobject][ordered]@{
        schemaVersion = '1.0.0'
        preflightKind = 'StaticEvidenceSnapshot'
        profileId = [string]$profileContext.profileId
        legacyCoreProfileEnum = [string]$profileContext.legacyCoreProfileEnum
        requestMetadata = [ordered]@{
            selectionSource = $SelectionSource
            requestedGeneration = $RequestedGeneration
        }
        registryEvidence = $registryEvidence
        sessionEvidence = [ordered]@{
            sourceWorkPackage = 'M0-SES-01'
            sourceSessionInventoryHash = [string]$SessionInventory.inventorySetHash
            rootStateInventoryCount = [int]$SessionInventory.coverage.observedStateCount
            currentRuntimeIsolationStatus = [string]$SessionInventory.currentRuntimeIsolation.status
            parserVmConsumptionStatus = [string]$SessionInventory.parserVmConsumption.status
            m1EligibilityStatus = [string]$SessionInventory.m1Eligibility.status
        }
        planSemanticHash = $planSemanticHash
        uncovered = @(
            'This is a source-only static evidence snapshot, not a runtime DialectPlan or CompatibilityPlan.',
            'Selection source and requested generation are audit metadata and are intentionally excluded from planSemanticHash.',
            'Parser/VM consumption, frozen registry construction, resolver behavior, ownership decisions, aliases, replacements and behavior fixtures remain outside M0-DIA-10.'
        )
    }
}

function New-DialectPlanPreflightReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][object]$RegistrySnapshot,
        [Parameter(Mandatory = $true, Position = 1)][object]$SessionInventory,
        [Parameter(Mandatory = $true, Position = 2)][object]$Catalog,
        [string]$OutputPath = ''
    )

    Assert-DialectPlanPreflightRegistrySnapshot -RegistrySnapshot $RegistrySnapshot
    Assert-DialectPlanPreflightSessionInventory -SessionInventory $SessionInventory
    $catalogContext = Get-DialectPlanPreflightCatalogContext -Catalog $Catalog

    $profiles = New-Object 'System.Collections.Generic.List[object]'
    foreach ($profileId in $catalogContext.profilesById.Keys) {
        $profiles.Add((New-LegacyDialectPlanPreflight -RegistrySnapshot $RegistrySnapshot -SessionInventory $SessionInventory -Catalog $Catalog -ProfileId $profileId -SelectionSource 'StaticContract' -RequestedGeneration 0))
    }
    $profileArray = $profiles.ToArray()
    $unsupportedLegacyCoreProfiles = $catalogContext.canonicalUnsupportedProfiles

    $currentRuntimeIsolation = [ordered]@{
        status = 'Failed'
        reason = 'DIA-02 and M0-SES-01 both show process-wide legacy state/registration; this preflight does not isolate a runtime session.'
    }
    $parserVmConsumption = [ordered]@{
        status = 'NotConsumed'
        reason = 'DIA-10 is emitted by an offline evidence tool and is not read by the legacy Parser or VM.'
    }
    $compatibilityPlanRuntime = [ordered]@{
        status = 'NotImplemented'
        reason = 'No runtime CompatibilityPlan, resolver, feature flag, session facade or frozen registry is created by M0-DIA-10.'
    }
    $m1Eligibility = [ordered]@{
        status = 'Blocked'
        reason = 'The M0-SES-01 source inventory remains blocked; this static preflight is input evidence, not M1 session isolation.'
    }
    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-10'
        sourceRegistrySnapshotHash = [string]$RegistrySnapshot.snapshotSetHash
        sourceRegistryInventoryHash = [string]$RegistrySnapshot.sourceInventoryHash
        sourceSessionInventoryHash = [string]$SessionInventory.inventorySetHash
        catalogId = $catalogContext.catalogId
        catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash
        profiles = $profileArray
        unsupportedLegacyCoreProfiles = $unsupportedLegacyCoreProfiles
        currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime
        m1Eligibility = $m1Eligibility
    }
    $preflightSetHash = Get-DialectPlanPreflightCanonicalHash -Value $setPayload -Depth 40

    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-10'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourceRegistrySnapshotHash = [string]$RegistrySnapshot.snapshotSetHash
        sourceRegistryInventoryHash = [string]$RegistrySnapshot.sourceInventoryHash
        sourceSessionInventoryHash = [string]$SessionInventory.inventorySetHash
        catalogId = $catalogContext.catalogId
        catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash
        preflightSetHash = $preflightSetHash
        profiles = $profileArray
        unsupportedLegacyCoreProfiles = $unsupportedLegacyCoreProfiles
        currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime
        m1Eligibility = $m1Eligibility
        uncovered = @(
            'DIA-10 is a reproducible source-only preflight snapshot, not a runtime CompatibilityPlan or DialectPlan.',
            'Only v24pure and snake have evidence-backed DIA-02 test projections; SnakeModernMobile remains explicitly Uncovered.',
            'The old runtime remains globally coupled: currentRuntimeIsolation=Failed and parserVmConsumption=NotConsumed.',
            'No LegacySessionFacade, feature flag, resolver, Parser/VM switch, D2 frozen registry, game execution, APK or device evidence is introduced.'
        )
    }

    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, (($report | ConvertTo-Json -Depth 40) + "`n"), $script:DialectPlanPreflightUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-LegacyDialectPlanPreflight, New-DialectPlanPreflightReport
