Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:ModuleVisibilityUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-ModuleVisibilitySha256Hex {
    param([byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Assert-ModuleVisibilityHash {
    param([object]$Value, [string]$Name)
    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid DIA-09 hash: $Name" }
}

function New-ModuleVisibilityMap {
    param([object[]]$Items, [string]$Kind, [string]$DuplicatePrefix)
    $map = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($item in @($Items)) {
        $key = [string]$item.publicKey
        if ([string]::IsNullOrWhiteSpace($key)) { throw "$Kind contains an empty public key." }
        if ($map.ContainsKey($key)) { throw "Duplicate $DuplicatePrefix key: $key" }
        $map.Add($key, $item)
    }
    return $map
}

function Assert-ModuleVisibilityKeySet {
    param([object]$Expected, [object]$Actual, [string]$Context)
    if ($Expected.Count -ne $Actual.Count) { throw "$Context key set count mismatch." }
    foreach ($key in $Expected.Keys) {
        if (-not $Actual.ContainsKey($key)) { throw "$Context key set mismatch: $key" }
    }
}

function Assert-ModuleVisibilityCatalog {
    param([object]$Catalog)
    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or
        [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-09' -or
        [string]$Catalog.sourceRegistrySnapshotWorkPackage -cne 'M0-DIA-02' -or
        [string]$Catalog.sourceOwnershipEvidenceWorkPackage -cne 'M0-DIA-07' -or
        [string]$Catalog.sourceNameLookupContractWorkPackage -cne 'M0-DIA-08' -or
        [string]$Catalog.catalogVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
        [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported DIA-09 module visibility catalog.'
    }
    $expectedNames = @(
        'unresolvedOwnershipCount', 'unresolvedInstructionCount', 'unresolvedExpressionFunctionCount',
        'v24VisibleCandidateCount', 'snakeOnlyCandidateCount', 'missingSnakeProjectionCount',
        'v24VisibleInstructionCount', 'snakeOnlyInstructionCount',
        'v24VisibleExpressionFunctionCount', 'snakeOnlyExpressionFunctionCount'
    )
    foreach ($name in $expectedNames) {
        if ($null -eq $Catalog.expected.$name -or [int]$Catalog.expected.$name -lt 0) {
            throw "Invalid DIA-09 catalog expected count: $name"
        }
    }
}

function New-DialectModuleVisibilityReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true, Position=0)][object]$RegistrySnapshot,
        [Parameter(Mandatory=$true, Position=1)][object]$OwnershipEvidence,
        [Parameter(Mandatory=$true, Position=2)][object]$NameLookupContract,
        [Parameter(Mandatory=$true, Position=3)][object]$Catalog,
        [string]$OutputPath = ''
    )

    if ([string]$RegistrySnapshot.workPackage -cne 'M0-DIA-02') { throw 'DIA-09 requires M0-DIA-02.' }
    Assert-ModuleVisibilityHash $RegistrySnapshot.snapshotSetHash 'DIA-02 snapshotSetHash'
    if ([string]$RegistrySnapshot.testProjectionInvariant.status -cne 'Passed') {
        throw 'DIA-02 test projection invariant must remain Passed.'
    }
    if ([string]$RegistrySnapshot.currentRuntimeIsolation.status -cne 'Failed') {
        throw 'DIA-02 current runtime isolation must remain Failed.'
    }
    if ([string]$OwnershipEvidence.workPackage -cne 'M0-DIA-07' -or
        [string]$OwnershipEvidence.sourceRegistrySnapshotHash -cne [string]$RegistrySnapshot.snapshotSetHash) {
        throw 'DIA-07 does not match DIA-02.'
    }
    Assert-ModuleVisibilityHash $OwnershipEvidence.evidenceSetHash 'DIA-07 evidenceSetHash'
    if ([string]$OwnershipEvidence.currentRuntimeIsolation.status -cne 'Failed') {
        throw 'DIA-07 current runtime isolation must remain Failed.'
    }
    if ([string]$NameLookupContract.workPackage -cne 'M0-DIA-08' -or
        [string]$NameLookupContract.sourceOwnershipEvidenceHash -cne [string]$OwnershipEvidence.evidenceSetHash) {
        throw 'DIA-08 does not match DIA-07.'
    }
    Assert-ModuleVisibilityHash $NameLookupContract.contractSetHash 'DIA-08 contractSetHash'
    if ([string]$NameLookupContract.currentRuntimeIsolation.status -cne 'Failed') {
        throw 'DIA-08 current runtime isolation must remain Failed.'
    }
    if ([string]$NameLookupContract.parserVmConsumption.status -cne 'NotConsumed') {
        throw 'DIA-08 parser/VM consumption must remain NotConsumed.'
    }
    Assert-ModuleVisibilityCatalog $Catalog

    $v24Instructions = New-ModuleVisibilityMap @($RegistrySnapshot.profiles.v24.instructions) 'v24 instruction projection' 'v24 instruction projection'
    $v24Functions = New-ModuleVisibilityMap @($RegistrySnapshot.profiles.v24.expressionFunctions) 'v24 expression projection' 'v24 expression projection'
    $snakeInstructions = New-ModuleVisibilityMap @($RegistrySnapshot.profiles.snake.instructions) 'Snake instruction projection' 'Snake instruction projection'
    $snakeFunctions = New-ModuleVisibilityMap @($RegistrySnapshot.profiles.snake.expressionFunctions) 'Snake expression projection' 'Snake expression projection'
    $ownershipInstructions = New-ModuleVisibilityMap @($OwnershipEvidence.instructions) 'DIA-07 instruction evidence' 'DIA-07 instruction evidence'
    $ownershipFunctions = New-ModuleVisibilityMap @($OwnershipEvidence.expressionFunctions) 'DIA-07 expression evidence' 'DIA-07 expression evidence'
    $lookupInstructions = New-ModuleVisibilityMap @($NameLookupContract.entries | Where-Object { $_.registryKind -eq 'Instruction' }) 'DIA-08 instruction lookup entry' 'DIA-08 instruction lookup entry'
    $lookupFunctions = New-ModuleVisibilityMap @($NameLookupContract.entries | Where-Object { $_.registryKind -eq 'ExpressionFunction' }) 'DIA-08 expression lookup entry' 'DIA-08 expression lookup entry'

    Assert-ModuleVisibilityKeySet $ownershipInstructions $snakeInstructions 'DIA-07/Snake instruction projection'
    Assert-ModuleVisibilityKeySet $ownershipFunctions $snakeFunctions 'DIA-07/Snake expression projection'
    Assert-ModuleVisibilityKeySet $ownershipInstructions $lookupInstructions 'DIA-07/DIA-08 instruction lookup'
    Assert-ModuleVisibilityKeySet $ownershipFunctions $lookupFunctions 'DIA-07/DIA-08 expression lookup'
    foreach ($key in $v24Instructions.Keys) {
        if (-not $snakeInstructions.ContainsKey($key)) { throw "v24 instruction projection is not a Snake projection subset: $key" }
    }
    foreach ($key in $v24Functions.Keys) {
        if (-not $snakeFunctions.ContainsKey($key)) { throw "v24 expression projection is not a Snake projection subset: $key" }
    }

    $entriesByCompositeKey = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $allOwnership = @($OwnershipEvidence.instructions) + @($OwnershipEvidence.expressionFunctions)
    foreach ($ownershipEntry in $allOwnership) {
        if ([string]$ownershipEntry.ownershipEvidenceStatus -cne 'Unresolved') { continue }
        $registryKind = [string]$ownershipEntry.registryKind
        $publicKey = [string]$ownershipEntry.publicKey
        $v24Map = if ($registryKind -eq 'Instruction') { $v24Instructions } elseif ($registryKind -eq 'ExpressionFunction') { $v24Functions } else { throw "Unsupported DIA-07 registry kind: $registryKind" }
        $snakeMap = if ($registryKind -eq 'Instruction') { $snakeInstructions } else { $snakeFunctions }
        $lookupMap = if ($registryKind -eq 'Instruction') { $lookupInstructions } else { $lookupFunctions }
        if (-not $snakeMap.ContainsKey($publicKey)) { throw "Unresolved ownership key is missing from Snake projection: $registryKind/$publicKey" }
        if (-not $lookupMap.ContainsKey($publicKey)) { throw "Unresolved ownership key is missing from DIA-08 lookup contract: $registryKind/$publicKey" }

        $v24Present = $v24Map.ContainsKey($publicKey)
        $snakeEntry = $snakeMap[$publicKey]
        $v24Entry = if ($v24Present) { $v24Map[$publicKey] } else { $null }
        $candidate = if ($v24Present) { 'V24VisibleCandidate' } else { 'SnakeOnlyCandidate' }
        $reason = if ($v24Present) {
            'The public key is present in both v24 and Snake static registry projections; this is visibility evidence only.'
        } else {
            'The public key is absent from the v24 static registry projection and present in the Snake projection; this is visibility evidence only.'
        }
        $entry = [pscustomobject][ordered]@{
            registryKind = $registryKind
            publicKey = $publicKey
            currentTargetModule = [string]$ownershipEntry.currentTargetModule
            currentHandler = [string]$ownershipEntry.currentHandler
            ownershipEvidenceStatus = 'Unresolved'
            visibilityEvidenceStatus = 'StaticProjectionMembership'
            visibilityCandidate = $candidate
            candidateReason = $reason
            v24Projection = [ordered]@{
                presence = if ($v24Present) { 'Present' } else { 'Absent' }
                moduleId = if ($v24Present) { [string]$v24Entry.moduleId } else { '' }
                currentContribution = if ($v24Present) { [string]$v24Entry.currentContribution } else { '' }
            }
            snakeProjection = [ordered]@{
                presence = 'Present'
                moduleId = [string]$snakeEntry.moduleId
                currentContribution = [string]$snakeEntry.currentContribution
            }
            lookupContract = [string]$lookupMap[$publicKey].lookupContract
            instructionProjection = [string]$lookupMap[$publicKey].instructionProjection
            aliasStatus = 'Unresolved'
            replacementStatus = 'Unresolved'
            behaviorCompatibilityStatus = 'Uncovered'
            completionEffectStatus = 'Uncovered'
        }
        $compositeKey = $registryKind + [char]0 + $publicKey
        if ($entriesByCompositeKey.ContainsKey($compositeKey)) { throw "Duplicate DIA-09 visibility entry: $registryKind/$publicKey" }
        $entriesByCompositeKey.Add($compositeKey, $entry)
    }
    $entries = @($entriesByCompositeKey.Values)

    $unresolvedInstructions = @($entries | Where-Object registryKind -eq 'Instruction')
    $unresolvedFunctions = @($entries | Where-Object registryKind -eq 'ExpressionFunction')
    $v24VisibleInstructions = @($unresolvedInstructions | Where-Object visibilityCandidate -eq 'V24VisibleCandidate')
    $snakeOnlyInstructions = @($unresolvedInstructions | Where-Object visibilityCandidate -eq 'SnakeOnlyCandidate')
    $v24VisibleFunctions = @($unresolvedFunctions | Where-Object visibilityCandidate -eq 'V24VisibleCandidate')
    $snakeOnlyFunctions = @($unresolvedFunctions | Where-Object visibilityCandidate -eq 'SnakeOnlyCandidate')
    $missingSnakeProjection = @($entries | Where-Object { $_.snakeProjection.presence -ne 'Present' })
    $coverage = [ordered]@{
        unresolvedOwnershipInputCount = $entries.Count
        unresolvedInstructionCount = $unresolvedInstructions.Count
        unresolvedExpressionFunctionCount = $unresolvedFunctions.Count
        v24VisibleCandidateCount = $v24VisibleInstructions.Count + $v24VisibleFunctions.Count
        snakeOnlyCandidateCount = $snakeOnlyInstructions.Count + $snakeOnlyFunctions.Count
        missingSnakeProjectionCount = $missingSnakeProjection.Count
        v24VisibleInstructionCount = $v24VisibleInstructions.Count
        snakeOnlyInstructionCount = $snakeOnlyInstructions.Count
        v24VisibleExpressionFunctionCount = $v24VisibleFunctions.Count
        snakeOnlyExpressionFunctionCount = $snakeOnlyFunctions.Count
    }
    $catalogExpectedNameByCoverageName = [ordered]@{
        unresolvedOwnershipInputCount = 'unresolvedOwnershipCount'
        unresolvedInstructionCount = 'unresolvedInstructionCount'
        unresolvedExpressionFunctionCount = 'unresolvedExpressionFunctionCount'
        v24VisibleCandidateCount = 'v24VisibleCandidateCount'
        snakeOnlyCandidateCount = 'snakeOnlyCandidateCount'
        missingSnakeProjectionCount = 'missingSnakeProjectionCount'
        v24VisibleInstructionCount = 'v24VisibleInstructionCount'
        snakeOnlyInstructionCount = 'snakeOnlyInstructionCount'
        v24VisibleExpressionFunctionCount = 'v24VisibleExpressionFunctionCount'
        snakeOnlyExpressionFunctionCount = 'snakeOnlyExpressionFunctionCount'
    }
    foreach ($name in $coverage.Keys) {
        $catalogExpectedName = [string]$catalogExpectedNameByCoverageName[$name]
        if ([int]$coverage[$name] -ne [int]$Catalog.expected.$catalogExpectedName) {
            $label = switch ($name) {
                'v24VisibleCandidateCount' { 'v24-visible candidate count' }
                'snakeOnlyCandidateCount' { 'Snake-only candidate count' }
                default { $name }
            }
            throw "DIA-09 $label drifted: expected $($Catalog.expected.$catalogExpectedName), actual $($coverage[$name])."
        }
    }
    if ($coverage.v24VisibleCandidateCount + $coverage.snakeOnlyCandidateCount -ne $coverage.unresolvedOwnershipInputCount) {
        throw 'DIA-09 unresolved visibility partition is incomplete.'
    }

    $expectedCanonical = [ordered]@{
        unresolvedOwnershipCount = [int]$Catalog.expected.unresolvedOwnershipCount
        unresolvedInstructionCount = [int]$Catalog.expected.unresolvedInstructionCount
        unresolvedExpressionFunctionCount = [int]$Catalog.expected.unresolvedExpressionFunctionCount
        v24VisibleCandidateCount = [int]$Catalog.expected.v24VisibleCandidateCount
        snakeOnlyCandidateCount = [int]$Catalog.expected.snakeOnlyCandidateCount
        missingSnakeProjectionCount = [int]$Catalog.expected.missingSnakeProjectionCount
        v24VisibleInstructionCount = [int]$Catalog.expected.v24VisibleInstructionCount
        snakeOnlyInstructionCount = [int]$Catalog.expected.snakeOnlyInstructionCount
        v24VisibleExpressionFunctionCount = [int]$Catalog.expected.v24VisibleExpressionFunctionCount
        snakeOnlyExpressionFunctionCount = [int]$Catalog.expected.snakeOnlyExpressionFunctionCount
    }
    $catalogCanonical = [ordered]@{
        schemaVersion = '1.0.0'
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        sourceWorkPackage = 'M0-DIA-09'
        sourceRegistrySnapshotWorkPackage = 'M0-DIA-02'
        sourceOwnershipEvidenceWorkPackage = 'M0-DIA-07'
        sourceNameLookupContractWorkPackage = 'M0-DIA-08'
        expected = $expectedCanonical
    }
    $catalogHash = Get-ModuleVisibilitySha256Hex $script:ModuleVisibilityUtf8NoBom.GetBytes(($catalogCanonical | ConvertTo-Json -Depth 20 -Compress))
    $canonical = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-09'
        sourceRegistrySnapshotHash = [string]$RegistrySnapshot.snapshotSetHash
        sourceOwnershipEvidenceHash = [string]$OwnershipEvidence.evidenceSetHash
        sourceNameLookupContractHash = [string]$NameLookupContract.contractSetHash
        catalogHash = $catalogHash
        coverage = $coverage
        entries = $entries
    }
    $visibilitySetHash = Get-ModuleVisibilitySha256Hex $script:ModuleVisibilityUtf8NoBom.GetBytes(($canonical | ConvertTo-Json -Depth 40 -Compress))
    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-09'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourceRegistrySnapshotHash = [string]$RegistrySnapshot.snapshotSetHash
        sourceOwnershipEvidenceHash = [string]$OwnershipEvidence.evidenceSetHash
        sourceNameLookupContractHash = [string]$NameLookupContract.contractSetHash
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = $catalogHash
        visibilitySetHash = $visibilitySetHash
        coverage = $coverage
        currentRuntimeIsolation = [ordered]@{
            status = 'Failed'
            reason = 'Legacy static registration still mixes profile contributions; DIA-09 only reads test projections.'
        }
        parserVmConsumption = [ordered]@{
            status = 'NotConsumed'
            reason = 'DIA-09 is a static projection-membership report and is not wired into legacy Parser/VM.'
        }
        ownershipResolution = [ordered]@{
            status = 'Unresolved'
            reason = 'Projection visibility does not prove module ownership, behavior compatibility, AliasOf, or ReplacementDeclaration.'
        }
        uncovered = @(
            'V24VisibleCandidate means only that a public key appears in the v24 test projection; it is not an ownership decision.',
            'SnakeOnlyCandidate means only that a public key is absent from the v24 test projection and present in the Snake projection; it is not a replacement decision.',
            'All alias/replacement, behavior, completion/effect, comparer policy, and two-sided fixture evidence remain unresolved or uncovered.',
            'Current runtime isolation remains Failed and the legacy Parser/VM does not consume this report; D1/D2 are not implemented.'
        )
        entries = $entries
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, ($report | ConvertTo-Json -Depth 40), $script:ModuleVisibilityUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectModuleVisibilityReport
