Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:LookupUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-LookupSha256Hex {
    param([byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Assert-LookupHash {
    param([object]$Value, [string]$Name)
    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid DIA-08 hash: $Name" }
}

function New-LookupOrdinalMap {
    param([object[]]$Items, [string]$Kind)
    $map = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($item in @($Items)) {
        $key = [string]$item.publicKey
        if ([string]::IsNullOrWhiteSpace($key)) { throw "$Kind contains an empty public key." }
        if ($map.ContainsKey($key)) { throw "Duplicate $Kind public key: $key" }
        $map.Add($key, $item)
    }
    return $map
}

function Assert-LookupKeySet {
    param([object]$ExpectedMap, [object[]]$Actual, [string]$Context)
    if ($ExpectedMap.Count -ne @($Actual).Count) { throw "$Context key set count mismatch." }
    $actualMap = New-LookupOrdinalMap @($Actual) $Context
    foreach ($key in $ExpectedMap.Keys) {
        if (-not $actualMap.ContainsKey($key)) { throw "$Context key set mismatch: $key" }
    }
}

function Assert-LookupSourcePattern {
    param([string]$Text, [string]$Pattern, [string]$Evidence)
    $options = [Text.RegularExpressions.RegexOptions]::Singleline -bor [Text.RegularExpressions.RegexOptions]::CultureInvariant
    if (-not [regex]::IsMatch($Text, $Pattern, $options)) { throw "Legacy lookup source contract missing: $Evidence" }
}

function Get-LookupSortedStrings {
    param([object[]]$Values, [string]$DuplicateMessage)
    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($value in @($Values)) {
        $text = [string]$value
        if ([string]::IsNullOrWhiteSpace($text)) { throw "$DuplicateMessage contains an empty value." }
        if (-not $set.Add($text)) { throw "$DuplicateMessage`: $text" }
    }
    return @($set)
}

function Test-LegacyInstructionLookupMatch {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$RegisteredKey,
        [Parameter(Mandatory=$true)][string]$InputName,
        [Parameter(Mandatory=$true)][bool]$IgnoreCaseVariable
    )
    $comparer = if ($IgnoreCaseVariable) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
    $dictionary = New-Object 'System.Collections.Generic.Dictionary[string,bool]' ($comparer)
    $dictionary.Add($RegisteredKey, $true)
    return $dictionary.ContainsKey($InputName)
}

function Get-LegacyExpressionLookupKey {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$InputName,
        [Parameter(Mandatory=$true)][bool]$IgnoreCaseFunction,
        [Parameter(Mandatory=$true)][string]$CultureName
    )
    if (-not $IgnoreCaseFunction) { return $InputName }
    $culture = [Globalization.CultureInfo]::GetCultureInfo($CultureName)
    return $culture.TextInfo.ToUpper($InputName)
}

function New-DialectNameLookupContractReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true,Position=0)][object]$Inventory,
        [Parameter(Mandatory=$true,Position=1)][object]$OwnershipEvidence,
        [Parameter(Mandatory=$true,Position=2)][object]$Catalog,
        [Parameter(Mandatory=$true,Position=3)][string]$ProjectRoot,
        [string]$OutputPath = ''
    )

    if ([string]$Inventory.workPackage -cne 'M0-DIA-01') { throw 'DIA-08 requires M0-DIA-01.' }
    Assert-LookupHash $Inventory.canonicalHash 'DIA-01 canonicalHash'
    if ([string]$OwnershipEvidence.workPackage -cne 'M0-DIA-07' -or
        [string]$OwnershipEvidence.sourceInventoryHash -cne [string]$Inventory.canonicalHash) {
        throw 'DIA-07 does not match DIA-01.'
    }
    Assert-LookupHash $OwnershipEvidence.evidenceSetHash 'DIA-07 evidenceSetHash'
    if ([string]$OwnershipEvidence.currentRuntimeIsolation.status -cne 'Failed') {
        throw 'DIA-07 current runtime isolation must remain Failed.'
    }
    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or
        [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-08' -or
        [string]$Catalog.sourceInventoryWorkPackage -cne 'M0-DIA-01' -or
        [string]$Catalog.sourceOwnershipWorkPackage -cne 'M0-DIA-07' -or
        [string]$Catalog.catalogVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
        [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported DIA-08 lookup catalog.'
    }

    $contract = $Catalog.contracts
    $expectedContract = [ordered]@{
        instructionComparerSelector='Config.ICVariable'
        instructionComparerWhenTrue='OrdinalIgnoreCase'
        instructionComparerWhenFalse='Ordinal'
        instructionNormalizer='Identity'
        expressionComparer='Ordinal'
        expressionNormalizerSelector='Config.ICFunction'
        expressionNormalizerWhenTrue='CurrentCultureToUpper'
        expressionNormalizerWhenFalse='Identity'
        projectionCollisionPolicy='ExistingInstructionWins'
        renameClassification='SourceTextRewrite'
    }
    foreach ($name in $expectedContract.Keys) {
        if ([string]$contract.$name -cne [string]$expectedContract[$name]) { throw "Unsupported DIA-08 contract value: $name" }
    }

    $project = [IO.Path]::GetFullPath($ProjectRoot)
    if (-not (Test-Path -LiteralPath $project -PathType Container)) { throw "ProjectRoot does not exist: $project" }
    $sourceById = @{}
    $sourceIdentityMap = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $sourcePathSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($source in @($Catalog.sourceFiles)) {
        $id = [string]$source.id
        $relativePath = ([string]$source.path).Replace('\','/').TrimStart('/')
        Assert-LookupHash $source.sha256 "catalog source $id"
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($relativePath)) { throw 'DIA-08 source identity is incomplete.' }
        if ($sourceById.ContainsKey($id)) { throw "Duplicate DIA-08 source id: $id" }
        if (-not $sourcePathSet.Add($relativePath)) { throw "Duplicate DIA-08 source path: $relativePath" }
        $fullPath = Join-Path $project $relativePath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "DIA-08 source file is missing: $relativePath" }
        $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $fullPath).Hash.ToLowerInvariant()
        if ($actualHash -cne [string]$source.sha256) { throw "DIA-08 source hash drifted: $id" }
        $text = [IO.File]::ReadAllText($fullPath)
        $sourceById.Add($id, $text)
        $sourceIdentityMap.Add($id, [pscustomobject][ordered]@{ id=$id; path=$relativePath; sha256=$actualHash })
    }
    $requiredSourceIds = @('EraStreamReader','ExpressionParser','FunctionIdentifier','FunctionMethodCreator','IdentifierDictionary','LexicalAnalyzer','LogicalLineParser','ParserMediator')
    foreach ($id in $requiredSourceIds) { if (-not $sourceById.ContainsKey($id)) { throw "Missing DIA-08 source evidence: $id" } }

    Assert-LookupSourcePattern $sourceById.FunctionIdentifier 'new\s+Dictionary<string,\s*FunctionIdentifier>\s*\(\s*Config\.ICVariable\s*\?\s*System\.StringComparer\.OrdinalIgnoreCase\s*:\s*System\.StringComparer\.Ordinal\s*\)' 'instruction comparer conditional on Config.ICVariable'
    Assert-LookupSourcePattern $sourceById.FunctionIdentifier 'GetInstructionNameDic\s*\(\s*\)\s*\{\s*return\s+funcDic\s*;' 'raw instruction dictionary exposure'
    Assert-LookupSourcePattern $sourceById.FunctionIdentifier 'FunctionMethodCreator\.GetMethodList\s*\(\s*\).*?if\s*\(\s*!funcDic\.ContainsKey\s*\(\s*key\s*\)\s*\).*?funcDic\.Add\s*\(\s*key\s*,\s*new\s+FunctionIdentifier\s*\(\s*key\s*,\s*pair\.Value\s*,\s*methodInstruction\s*\)\s*\)' 'expression-to-instruction projection with existing instruction precedence'
    Assert-LookupSourcePattern $sourceById.FunctionMethodCreator 'methodList\s*=\s*new\s+Dictionary<string,\s*FunctionMethod>\s*\{' 'ordinal expression dictionary construction'
    Assert-LookupSourcePattern $sourceById.FunctionMethodCreator 'GetMethodList\s*\(\s*\)\s*\{\s*return\s+methodList\s*;' 'raw expression dictionary exposure'
    Assert-LookupSourcePattern $sourceById.IdentifierDictionary 'GetFunctionIdentifier\s*\(\s*string\s+str\s*\).*?var\s+lookup\s*=\s*compatibilityInstructionDic\s*\?\?\s*instructionDic.*?lookup\.TryGetValue\s*\(\s*str\s*,' 'descriptor-routed instruction lookup without key normalization'
    Assert-LookupSourcePattern $sourceById.IdentifierDictionary 'GetFunctionMethod\s*\(.*?if\s*\(\s*Config\.ICFunction\s*\)\s*codeStr\s*=\s*codeStr\.ToUpper\s*\(\s*\)\s*;.*?var\s+methods\s*=\s*compatibilityMethodDic\s*\?\?\s*methodDic.*?methods\.TryGetValue\s*\(\s*codeStr\s*,' 'descriptor-routed expression normalization followed by ordinal lookup'
    Assert-LookupSourcePattern $sourceById.LogicalLineParser 'ReadFirstIdentifier\s*\(\s*stream\s*\).*?GetFunctionIdentifier\s*\(\s*idCode\s*\)' 'instruction parser passes the lexical key directly to lookup'
    Assert-LookupSourcePattern $sourceById.ExpressionParser 'GetFunctionMethod\s*\(\s*GlobalStatic\.LabelDictionary\s*,\s*idStr\s*,\s*args\s*,\s*false\s*\)' 'expression parser delegates name lookup to IdentifierDictionary'
    Assert-LookupSourcePattern $sourceById.LexicalAnalyzer 'ReadFirstIdentifier\s*\(\s*StringStream\s+st\s*\)\s*\{\s*string\s+str\s*=\s*ReadSingleIdentifier\s*\(\s*st\s*\)' 'lexical instruction key has no casing normalization'
    Assert-LookupSourcePattern $sourceById.ParserMediator 'RenameDic\s*=\s*new\s+Dictionary<string,\s*string>\s*\(\s*\).*?string\.Format\s*\(\s*"\[\[\{0\}\]\]".*?RenameDic\s*\[\s*key\s*\]\s*=\s*value' '_Rename catalog produces bracketed source tokens'
    Assert-LookupSourcePattern $sourceById.EraStreamReader 'foreach\s*\(\s*KeyValuePair<string,\s*string>\s+pair\s+in\s+ParserMediator\.RenameDic\s*\)\s*line\s*=\s*line\.Replace\s*\(\s*pair\.Key\s*,\s*pair\.Value\s*\)' '_Rename is whole-line source text rewrite before lexical analysis'

    $instructionMap = New-LookupOrdinalMap @($Inventory.instructionRegistrations) 'instruction'
    $expressionMap = New-LookupOrdinalMap @($Inventory.expressionRegistrations) 'expression'
    if ($instructionMap.Count -ne [int]$Catalog.expectedInstructionRegistrationCount) { throw 'Instruction registration count does not match DIA-08 catalog.' }
    if ($expressionMap.Count -ne [int]$Catalog.expectedExpressionRegistrationCount) { throw 'Expression registration count does not match DIA-08 catalog.' }
    Assert-LookupKeySet $instructionMap @($OwnershipEvidence.instructions) 'DIA-01/DIA-07 instruction'
    Assert-LookupKeySet $expressionMap @($OwnershipEvidence.expressionFunctions) 'DIA-01/DIA-07 expression'
    $allPublicKeys = @($instructionMap.Keys) + @($expressionMap.Keys)
    $asciiUpperPublicKeyCount = @($allPublicKeys | Where-Object { $_ -cmatch '^[A-Z0-9_]+$' }).Count
    $nonAsciiOrMixedCasePublicKeyCount = $allPublicKeys.Count - $asciiUpperPublicKeyCount

    $collisionSet = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($key in $instructionMap.Keys) { if ($expressionMap.ContainsKey($key)) { [void]$collisionSet.Add($key) } }
    $expectedCollisionKeys = Get-LookupSortedStrings @($Catalog.expectedCollisionKeys) 'Duplicate expected collision key'
    if ($collisionSet.Count -ne $expectedCollisionKeys.Count) { throw 'Cross-registry collision count drifted.' }
    for ($index=0; $index -lt $expectedCollisionKeys.Count; $index++) {
        if ([string]$expectedCollisionKeys[$index] -cne [string]@($collisionSet)[$index]) { throw "Cross-registry collision key drifted: $($expectedCollisionKeys[$index])" }
    }
    $surfaceSet = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($key in $instructionMap.Keys) { [void]$surfaceSet.Add($key) }
    foreach ($key in $expressionMap.Keys) { [void]$surfaceSet.Add($key) }
    if ($surfaceSet.Count -ne [int]$Catalog.expectedInstructionLookupSurfaceCount) { throw 'Instruction lookup surface count drifted.' }
    if ($surfaceSet.Count -ne ($instructionMap.Count + $expressionMap.Count - $collisionSet.Count)) { throw 'Instruction lookup projection set identity failed.' }

    $ownershipInstructionMap = New-LookupOrdinalMap @($OwnershipEvidence.instructions) 'DIA-07 instruction'
    $ownershipExpressionMap = New-LookupOrdinalMap @($OwnershipEvidence.expressionFunctions) 'DIA-07 expression'
    $entryMap = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($key in $instructionMap.Keys) {
        $inventoryEntry = $instructionMap[$key]
        $ownershipEntry = $ownershipInstructionMap[$key]
        $entryMap.Add("Instruction`0$key", [pscustomobject][ordered]@{
            publicKey=$key; registryKind='Instruction'; currentHandler=[string]$inventoryEntry.handler
            lookupContract='InstructionConditionalICVariable'; directLookup='DirectInstruction'; instructionProjection='DirectInstruction'
            ownershipEvidenceStatus=[string]$ownershipEntry.ownershipEvidenceStatus
            lookupAliasMechanism='None'; lookupReplacementMechanism='None'
            semanticAliasStatus='Unresolved'; semanticReplacementStatus='Unresolved'
        })
    }
    foreach ($key in $expressionMap.Keys) {
        $inventoryEntry = $expressionMap[$key]
        $ownershipEntry = $ownershipExpressionMap[$key]
        $projection = if ($collisionSet.Contains($key)) { 'SuppressedByInstructionCollision' } else { 'ProjectedAsMethodInstruction' }
        $entryMap.Add("ExpressionFunction`0$key", [pscustomobject][ordered]@{
            publicKey=$key; registryKind='ExpressionFunction'; currentHandler=[string]$inventoryEntry.handler
            lookupContract='ExpressionOrdinalWithConditionalCurrentCultureUpper'; directLookup='DirectExpressionFunction'; instructionProjection=$projection
            ownershipEvidenceStatus=[string]$ownershipEntry.ownershipEvidenceStatus
            lookupAliasMechanism='None'; lookupReplacementMechanism='None'
            semanticAliasStatus='Unresolved'; semanticReplacementStatus='Unresolved'
        })
    }
    $entries = @($entryMap.Values)
    $collisions = New-Object System.Collections.Generic.List[object]
    foreach ($key in $collisionSet) {
        $collisions.Add([pscustomobject][ordered]@{
            publicKey=$key
            instructionHandler=[string]$instructionMap[$key].handler
            expressionHandler=[string]$expressionMap[$key].handler
            resolution='InstructionPrecedence'
        })
    }
    $collisionArray = $collisions.ToArray()

    $instructionLookup = [ordered]@{
        registryComparer=[ordered]@{ selector='Config.ICVariable'; whenTrue='OrdinalIgnoreCase'; whenFalse='Ordinal'; capture='StaticInitialization' }
        normalizer='Identity'; parserInput='LexicalIdentifierUnchanged'; lookupOperation='Dictionary.TryGetValue'
    }
    $expressionLookup = [ordered]@{
        registryComparer='Ordinal'
        normalizer=[ordered]@{ selector='Config.ICFunction'; whenTrue='CurrentCultureToUpper'; whenFalse='Identity' }
        lookupOperation='Dictionary.TryGetValue'; cultureRisk='CurrentCultureDependent'
    }
    $projection = [ordered]@{
        source='ExpressionFunctionRegistry'; target='InstructionLookup'; projectedCount=($expressionMap.Count-$collisionSet.Count)
        suppressedCollisionCount=$collisionSet.Count; collisionPolicy='ExistingInstructionWins'; implementation='ContainsKeyThenAddMethodInstruction'
    }
    $mutableExposure = [ordered]@{
        instructionRegistry='RawStaticMutableDictionary'; instructionApi='FunctionIdentifier.GetInstructionNameDic'
        expressionRegistry='RawStaticMutableDictionary'; expressionApi='FunctionMethodCreator.GetMethodList'
    }
    $sourceRewrite = [ordered]@{
        classification='SourceTextRewrite'; source='_Rename.csv'; tokenForm='[[name]]'; stage='EraStreamReaderBeforeLexicalAnalysis'
        algorithm='DictionaryEnumerationThenStringReplace'; registryAliasMechanism='None'; registryReplacementMechanism='None'
    }
    $semanticStatus = [ordered]@{ alias='Unresolved'; replacement='Unresolved'; reason='Absence of registry indirection does not prove absence of semantic aliases or intended replacements.' }
    $keyDomain = [ordered]@{
        comparerDomain='OrdinalString'; asciiUpperPublicKeyCount=$asciiUpperPublicKeyCount
        nonAsciiOrMixedCasePublicKeyCount=$nonAsciiOrMixedCasePublicKeyCount
        note='Unicode public keys are preserved as ordinal registry keys; identical handler types do not establish AliasOf.'
    }

    $sourceIdentities = @($sourceIdentityMap.Values)
    $catalogCanonical = [ordered]@{
        schemaVersion='1.0.0'; catalogId=[string]$Catalog.catalogId; catalogVersion=[string]$Catalog.catalogVersion; sourceWorkPackage='M0-DIA-08'
        sourceInventoryWorkPackage='M0-DIA-01'; sourceOwnershipWorkPackage='M0-DIA-07'; sourceFiles=$sourceIdentities
        expectedInstructionRegistrationCount=[int]$Catalog.expectedInstructionRegistrationCount
        expectedExpressionRegistrationCount=[int]$Catalog.expectedExpressionRegistrationCount
        expectedInstructionLookupSurfaceCount=[int]$Catalog.expectedInstructionLookupSurfaceCount
        expectedCollisionKeys=$expectedCollisionKeys; contracts=$expectedContract
    }
    $catalogHash = Get-LookupSha256Hex $script:LookupUtf8NoBom.GetBytes(($catalogCanonical | ConvertTo-Json -Depth 20 -Compress))
    $canonical = [ordered]@{
        schemaVersion='1.0.0'; workPackage='M0-DIA-08'; sourceInventoryHash=[string]$Inventory.canonicalHash
        sourceOwnershipEvidenceHash=[string]$OwnershipEvidence.evidenceSetHash; catalogHash=$catalogHash; sourceIdentity=$sourceIdentities
        instructionLookup=$instructionLookup; expressionLookup=$expressionLookup; projection=$projection
        mutableDictionaryExposure=$mutableExposure; sourceTextRewrite=$sourceRewrite; semanticStatus=$semanticStatus; keyDomain=$keyDomain
        collisions=$collisionArray; entries=$entries
    }
    $contractSetHash = Get-LookupSha256Hex $script:LookupUtf8NoBom.GetBytes(($canonical | ConvertTo-Json -Depth 30 -Compress))
    $report = [ordered]@{
        schemaVersion='1.0.0'; workPackage='M0-DIA-08'; generatedAtUtc=[DateTime]::UtcNow.ToString('o')
        executionStatus='InProgress'; gateStatus='Blocked'; blockerCode='EvidenceMissing'; result='Partial'
        sourceInventoryHash=[string]$Inventory.canonicalHash; sourceOwnershipEvidenceHash=[string]$OwnershipEvidence.evidenceSetHash
        catalogId=[string]$Catalog.catalogId; catalogVersion=[string]$Catalog.catalogVersion; catalogHash=$catalogHash; contractSetHash=$contractSetHash
        sourceIdentity=$sourceIdentities; instructionRegistrationCount=$instructionMap.Count; expressionRegistrationCount=$expressionMap.Count
        crossRegistryCollisionCount=$collisionSet.Count; instructionLookupSurfaceCount=$surfaceSet.Count
        instructionLookup=$instructionLookup; expressionLookup=$expressionLookup; projection=$projection
        mutableDictionaryExposure=$mutableExposure; sourceTextRewrite=$sourceRewrite; semanticStatus=$semanticStatus; keyDomain=$keyDomain
        coverage=[ordered]@{
            lookupContractResolvedCount=$entries.Count; semanticAliasUnresolvedCount=$entries.Count; semanticReplacementUnresolvedCount=$entries.Count
            projectedExpressionInstructionCount=($expressionMap.Count-$collisionSet.Count); suppressedExpressionCollisionCount=$collisionSet.Count
        }
        currentRuntimeIsolation=[ordered]@{ status='Failed'; reason='Legacy static registration still mixes profile contributions and captures comparer state globally.' }
        parserVmConsumption=[ordered]@{ status='NotConsumed'; reason='DIA-08 is a static evidence report and is not wired into legacy Parser/VM lookup.' }
        uncovered=@(
            'Semantic AliasOf relationships remain Unresolved; handler reuse is not alias proof.',
            'Semantic replacement intent remains Unresolved; instruction collision precedence is not a ReplacementDeclaration.',
            'Current-culture ToUpper behavior is recorded but not approved as the future D2 comparer policy.',
            'Behavior, completion and effects remain Uncovered; runtime isolation remains Failed.'
        )
        collisions=$collisionArray; entries=$entries
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, ($report | ConvertTo-Json -Depth 40), $script:LookupUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectNameLookupContractReport,Test-LegacyInstructionLookupMatch,Get-LegacyExpressionLookupKey
