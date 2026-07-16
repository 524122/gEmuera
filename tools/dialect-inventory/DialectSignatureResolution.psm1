Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:ResolutionUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-ResolutionSha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Sort-ResolutionOrdinal {
    param([object[]]$Items, [Parameter(Mandatory = $true)][scriptblock]$KeySelector)
    $map = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $index = 0
    foreach ($item in @($Items)) {
        $key = [string](& $KeySelector $item)
        $map.Add(($key + [char]0 + $index.ToString('D10', [Globalization.CultureInfo]::InvariantCulture)), $item)
        $index++
    }
    return @($map.Values)
}

function Assert-ResolutionText {
    param([AllowNull()][object]$Value, [string]$FieldName)
    if ([string]::IsNullOrWhiteSpace([string]$Value)) { throw "Signature resolution catalog field is required: $FieldName" }
}

function Test-SignatureResolutionRuleMatch {
    param([Parameter(Mandatory = $true)][object]$Rule, [Parameter(Mandatory = $true)][object]$Instruction)
    if ([string]$Rule.handlerType -cne [string]$Instruction.handlerType) { return $false }
    if ([string]$Rule.matchKind -eq 'Exact') { return [string]$Rule.pattern -ceq [string]$Instruction.publicKey }
    return $Rule.compiledRegex.IsMatch([string]$Instruction.publicKey)
}

function New-DialectSignatureResolutionReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$SignatureInventory,
        [Parameter(Mandatory = $true)][object]$Catalog,
        [string]$OutputPath = ''
    )

    if ([string]$SignatureInventory.workPackage -ne 'M0-DIA-03') { throw 'Signature resolution requires an M0-DIA-03 inventory.' }
    if ([string]$SignatureInventory.descriptorSetHash -notmatch '^[0-9a-f]{64}$') { throw 'Signature resolution requires a valid DIA-03 descriptorSetHash.' }
    if ([string]$Catalog.schemaVersion -ne '1.0.0') { throw "Unsupported signature resolution catalog schemaVersion: $($Catalog.schemaVersion)" }
    Assert-ResolutionText $Catalog.catalogId 'catalogId'
    Assert-ResolutionText $Catalog.catalogVersion 'catalogVersion'
    if ([string]$Catalog.sourceWorkPackage -ne 'M0-DIA-03') { throw 'Signature resolution catalog sourceWorkPackage must be M0-DIA-03.' }
    if ([string]$Catalog.sourceDescriptorSetHash -cne [string]$SignatureInventory.descriptorSetHash) {
        throw "Signature resolution catalog sourceDescriptorSetHash does not match DIA-03: catalog=$($Catalog.sourceDescriptorSetHash) actual=$($SignatureInventory.descriptorSetHash)"
    }

    $instructions = @($SignatureInventory.instructions)
    if ($instructions.Count -eq 0) { throw 'Signature resolution requires at least one DIA-03 instruction descriptor.' }
    $publicKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($instruction in $instructions) {
        Assert-ResolutionText $instruction.publicKey 'instructions[].publicKey'
        if ([string]$instruction.argumentSchemaStatus -eq 'Conditional') {
            Assert-ResolutionText $instruction.handlerType 'Conditional instructions[].handlerType'
        }
        if (-not $publicKeys.Add([string]$instruction.publicKey)) { throw "Duplicate DIA-03 instruction publicKey: $($instruction.publicKey)" }
    }

    $ruleIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $selectors = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $normalizedRules = New-Object System.Collections.Generic.List[object]
    foreach ($rule in @($Catalog.rules)) {
        Assert-ResolutionText $rule.ruleId 'rules[].ruleId'
        Assert-ResolutionText $rule.handlerType 'rules[].handlerType'
        Assert-ResolutionText $rule.pattern 'rules[].pattern'
        Assert-ResolutionText $rule.selectedArgumentSchema 'rules[].selectedArgumentSchema'
        Assert-ResolutionText $rule.rationale 'rules[].rationale'
        if (-not $ruleIds.Add([string]$rule.ruleId)) { throw "Duplicate ruleId in signature resolution catalog: $($rule.ruleId)" }
        if ([string]$rule.matchKind -notin @('Exact', 'PublicKeyRegex')) { throw "Unsupported matchKind for rule $($rule.ruleId): $($rule.matchKind)" }
        $selector = ([string]$rule.handlerType) + [char]0 + ([string]$rule.matchKind) + [char]0 + ([string]$rule.pattern)
        if (-not $selectors.Add($selector)) { throw "Duplicate rule selector in signature resolution catalog: $($rule.ruleId)" }
        $expectedMatchCount = [int]$rule.expectedMatchCount
        if ($expectedMatchCount -lt 1) { throw "Rule expectedMatchCount must be positive: $($rule.ruleId)" }

        $compiledRegex = $null
        if ([string]$rule.matchKind -eq 'PublicKeyRegex') {
            if (-not ([string]$rule.pattern).StartsWith('^', [StringComparison]::Ordinal) -or
                -not ([string]$rule.pattern).EndsWith('$', [StringComparison]::Ordinal)) {
                throw "PublicKeyRegex must be anchored for rule $($rule.ruleId): $($rule.pattern)"
            }
            try {
                $compiledRegex = New-Object Text.RegularExpressions.Regex(
                    [string]$rule.pattern,
                    ([Text.RegularExpressions.RegexOptions]::CultureInvariant -bor [Text.RegularExpressions.RegexOptions]::ExplicitCapture),
                    [TimeSpan]::FromMilliseconds(250))
            }
            catch { throw "Invalid PublicKeyRegex for rule $($rule.ruleId): $($_.Exception.Message)" }
        }

        $normalizedRules.Add([pscustomobject][ordered]@{
            ruleId = [string]$rule.ruleId
            handlerType = [string]$rule.handlerType
            matchKind = [string]$rule.matchKind
            pattern = [string]$rule.pattern
            selectedArgumentSchema = [string]$rule.selectedArgumentSchema
            expectedMatchCount = $expectedMatchCount
            rationale = [string]$rule.rationale
            compiledRegex = $compiledRegex
        })
    }
    if ($normalizedRules.Count -eq 0) { throw 'Signature resolution catalog contains no rules.' }
    $sortedRules = Sort-ResolutionOrdinal -Items $normalizedRules.ToArray() -KeySelector { param($item) $item.ruleId }

    $ruleMatchReports = New-Object System.Collections.Generic.List[object]
    foreach ($rule in $sortedRules) {
        $matched = New-Object System.Collections.Generic.List[object]
        foreach ($instruction in $instructions) {
            if (Test-SignatureResolutionRuleMatch -Rule $rule -Instruction $instruction) { $matched.Add($instruction) }
        }
        foreach ($instruction in $matched) {
            if ([string]$instruction.argumentSchemaStatus -ne 'Conditional') {
                throw "Signature resolution rule $($rule.ruleId) targets an already-Resolved or non-Conditional descriptor: $($instruction.publicKey)"
            }
        }
        if ($matched.Count -ne [int]$rule.expectedMatchCount) {
            throw "Signature resolution rule $($rule.ruleId) expectedMatchCount=$($rule.expectedMatchCount), actual=$($matched.Count)."
        }
        $matchedKeys = Sort-ResolutionOrdinal -Items @($matched | Select-Object -ExpandProperty publicKey) -KeySelector { param($item) [string]$item }
        $ruleMatchReports.Add([pscustomobject][ordered]@{
            ruleId = $rule.ruleId
            matchedCount = $matched.Count
            matchedPublicKeys = @($matchedKeys)
        })
    }

    $resolved = New-Object System.Collections.Generic.List[object]
    foreach ($instruction in $instructions) {
        $candidates = @($instruction.argumentSchemaCandidates)
        $status = [string]$instruction.argumentSchemaStatus
        $selected = ''
        $resolutionStatus = 'UnresolvedStatic'
        $ruleId = ''

        if ($status -eq 'Resolved') {
            if ($candidates.Count -ne 1) { throw "Resolved DIA-03 descriptor must have exactly one candidate: $($instruction.publicKey)" }
            $selected = [string]$candidates[0]
            $resolutionStatus = 'PreservedStatic'
        }
        elseif ($status -eq 'Conditional') {
            $matches = New-Object System.Collections.Generic.List[object]
            foreach ($rule in $sortedRules) {
                if (Test-SignatureResolutionRuleMatch -Rule $rule -Instruction $instruction) { $matches.Add($rule) }
            }
            if ($matches.Count -ne 1) {
                throw "Conditional descriptor must match exactly one rule: $($instruction.publicKey) matched=$($matches.Count)"
            }
            $selected = [string]$matches[0].selectedArgumentSchema
            if (-not (@($candidates) -ccontains $selected)) {
                throw "Rule $($matches[0].ruleId) selected '$selected', which is not a DIA-03 candidate for $($instruction.publicKey)."
            }
            $ruleId = [string]$matches[0].ruleId
            $resolutionStatus = 'ResolvedStaticByRule'
        }
        elseif ($status -ne 'Unresolved') {
            throw "Unsupported DIA-03 argumentSchemaStatus for $($instruction.publicKey): $status"
        }

        $resolved.Add([pscustomobject][ordered]@{
            publicKey = [string]$instruction.publicKey
            targetModule = [string]$instruction.targetModule
            handlerType = [string]$instruction.handlerType
            sourceArgumentSchemaStatus = $status
            sourceArgumentSchemaCandidates = @($candidates)
            resolvedArgumentSchema = $selected
            resolutionStatus = $resolutionStatus
            ruleId = $ruleId
            evidenceStatus = 'StaticCandidate'
            behaviorFixtureStatus = 'Uncovered'
        })
    }
    $sortedResolved = Sort-ResolutionOrdinal -Items $resolved.ToArray() -KeySelector { param($item) $item.publicKey }

    $canonicalRules = @($sortedRules | ForEach-Object { [ordered]@{
        ruleId=$_.ruleId; handlerType=$_.handlerType; matchKind=$_.matchKind; pattern=$_.pattern
        selectedArgumentSchema=$_.selectedArgumentSchema; expectedMatchCount=$_.expectedMatchCount; rationale=$_.rationale
    } })
    $catalogCanonical = [ordered]@{
        schemaVersion = [string]$Catalog.schemaVersion
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        sourceWorkPackage = [string]$Catalog.sourceWorkPackage
        sourceDescriptorSetHash = [string]$Catalog.sourceDescriptorSetHash
        rules = $canonicalRules
    }
    $catalogJson = $catalogCanonical | ConvertTo-Json -Depth 20 -Compress
    $catalogHash = Get-ResolutionSha256Hex $script:ResolutionUtf8NoBom.GetBytes($catalogJson)
    $resolutionCanonical = [ordered]@{
        schemaVersion='1.0.0'; workPackage='M0-DIA-04'
        sourceDescriptorSetHash=[string]$SignatureInventory.descriptorSetHash
        catalogHash=$catalogHash; instructions=@($sortedResolved)
    }
    $resolutionJson = $resolutionCanonical | ConvertTo-Json -Depth 30 -Compress
    $resolutionSetHash = Get-ResolutionSha256Hex $script:ResolutionUtf8NoBom.GetBytes($resolutionJson)

    $coverage = [ordered]@{
        sourceResolvedCount = @($instructions | Where-Object argumentSchemaStatus -eq 'Resolved').Count
        sourceConditionalCount = @($instructions | Where-Object argumentSchemaStatus -eq 'Conditional').Count
        sourceUnresolvedCount = @($instructions | Where-Object argumentSchemaStatus -eq 'Unresolved').Count
        preservedStaticCount = @($sortedResolved | Where-Object resolutionStatus -eq 'PreservedStatic').Count
        resolvedStaticByRuleCount = @($sortedResolved | Where-Object resolutionStatus -eq 'ResolvedStaticByRule').Count
        unresolvedCount = @($sortedResolved | Where-Object resolutionStatus -eq 'UnresolvedStatic').Count
        catalogRuleCount = $sortedRules.Count
    }
    $report = [ordered]@{
        schemaVersion='1.0.0'; workPackage='M0-DIA-04'; generatedAtUtc=[DateTime]::UtcNow.ToString('o')
        executionStatus='InProgress'; gateStatus='Blocked'; blockerCode='EvidenceMissing'; result='Partial'
        sourceDescriptorSetHash=[string]$SignatureInventory.descriptorSetHash
        catalogId=[string]$Catalog.catalogId; catalogVersion=[string]$Catalog.catalogVersion
        catalogHash=$catalogHash; resolutionSetHash=$resolutionSetHash; instructionCount=$sortedResolved.Count
        coverage=$coverage; ruleMatches=@($ruleMatchReports.ToArray())
        uncovered=@(
            'Resolution status is static-by-rule only; argument defaults, error behavior, completion and effects require two-sided fixtures.',
            'Expression function Conditional return types remain outside M0-DIA-04.',
            'Module ownership, replacement/comparer policy, D1 plan and D2 runtime registry isolation remain unresolved.',
            'This report is not consumed by the legacy Parser/VM and does not change M0-DIA-02 or M0-DIA-03 hashes.'
        )
        instructions=@($sortedResolved)
    }

    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, ($report | ConvertTo-Json -Depth 40), $script:ResolutionUtf8NoBom)
    }
    return [pscustomobject]$report
}

function New-DialectFunctionSignatureResolutionReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][object]$SignatureInventory,
        [Parameter(Mandatory = $true)][object]$Catalog,
        [string]$OutputPath = ''
    )

    if ([string]$SignatureInventory.workPackage -ne 'M0-DIA-03') { throw 'Function signature resolution requires an M0-DIA-03 inventory.' }
    if ([string]$SignatureInventory.descriptorSetHash -notmatch '^[0-9a-f]{64}$') { throw 'Function signature resolution requires a valid DIA-03 descriptorSetHash.' }
    if ([string]$Catalog.schemaVersion -ne '1.0.0') { throw "Unsupported function return resolution catalog schemaVersion: $($Catalog.schemaVersion)" }
    Assert-ResolutionText $Catalog.catalogId 'catalogId'
    Assert-ResolutionText $Catalog.catalogVersion 'catalogVersion'
    if ([string]$Catalog.sourceWorkPackage -ne 'M0-DIA-03') { throw 'Function return resolution catalog sourceWorkPackage must be M0-DIA-03.' }
    if ([string]$Catalog.sourceDescriptorSetHash -cne [string]$SignatureInventory.descriptorSetHash) {
        throw "Function return resolution catalog sourceDescriptorSetHash does not match DIA-03: catalog=$($Catalog.sourceDescriptorSetHash) actual=$($SignatureInventory.descriptorSetHash)"
    }

    $functions = @($SignatureInventory.expressionFunctions)
    if ($functions.Count -eq 0) { throw 'Function signature resolution requires at least one DIA-03 expression function descriptor.' }
    $publicKeys = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($function in $functions) {
        Assert-ResolutionText $function.publicKey 'expressionFunctions[].publicKey'
        Assert-ResolutionText $function.handlerType 'expressionFunctions[].handlerType'
        if (-not $publicKeys.Add([string]$function.publicKey)) { throw "Duplicate DIA-03 expression function publicKey: $($function.publicKey)" }
    }

    $ruleIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $selectors = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $normalizedRules = New-Object System.Collections.Generic.List[object]
    foreach ($rule in @($Catalog.rules)) {
        Assert-ResolutionText $rule.ruleId 'rules[].ruleId'
        Assert-ResolutionText $rule.handlerType 'rules[].handlerType'
        Assert-ResolutionText $rule.pattern 'rules[].pattern'
        Assert-ResolutionText $rule.selectedReturnType 'rules[].selectedReturnType'
        Assert-ResolutionText $rule.rationale 'rules[].rationale'
        if (-not $ruleIds.Add([string]$rule.ruleId)) { throw "Duplicate ruleId in function return resolution catalog: $($rule.ruleId)" }
        if ([string]$rule.matchKind -notin @('Exact', 'PublicKeyRegex')) { throw "Unsupported matchKind for function rule $($rule.ruleId): $($rule.matchKind)" }
        $selector = ([string]$rule.handlerType) + [char]0 + ([string]$rule.matchKind) + [char]0 + ([string]$rule.pattern)
        if (-not $selectors.Add($selector)) { throw "Duplicate rule selector in function return resolution catalog: $($rule.ruleId)" }
        $expectedMatchCount = [int]$rule.expectedMatchCount
        if ($expectedMatchCount -lt 1) { throw "Function rule expectedMatchCount must be positive: $($rule.ruleId)" }

        $compiledRegex = $null
        if ([string]$rule.matchKind -eq 'PublicKeyRegex') {
            if (-not ([string]$rule.pattern).StartsWith('^', [StringComparison]::Ordinal) -or
                -not ([string]$rule.pattern).EndsWith('$', [StringComparison]::Ordinal)) {
                throw "PublicKeyRegex must be anchored for function rule $($rule.ruleId): $($rule.pattern)"
            }
            try {
                $compiledRegex = New-Object Text.RegularExpressions.Regex(
                    [string]$rule.pattern,
                    ([Text.RegularExpressions.RegexOptions]::CultureInvariant -bor [Text.RegularExpressions.RegexOptions]::ExplicitCapture),
                    [TimeSpan]::FromMilliseconds(250))
            }
            catch { throw "Invalid PublicKeyRegex for function rule $($rule.ruleId): $($_.Exception.Message)" }
        }

        $normalizedRules.Add([pscustomobject][ordered]@{
            ruleId=[string]$rule.ruleId; handlerType=[string]$rule.handlerType
            matchKind=[string]$rule.matchKind; pattern=[string]$rule.pattern
            selectedReturnType=[string]$rule.selectedReturnType
            expectedMatchCount=$expectedMatchCount; rationale=[string]$rule.rationale
            compiledRegex=$compiledRegex
        })
    }
    if ($normalizedRules.Count -eq 0) { throw 'Function return resolution catalog contains no rules.' }
    $sortedRules = Sort-ResolutionOrdinal -Items $normalizedRules.ToArray() -KeySelector { param($item) $item.ruleId }

    $ruleMatchReports = New-Object System.Collections.Generic.List[object]
    foreach ($rule in $sortedRules) {
        $matched = New-Object System.Collections.Generic.List[object]
        foreach ($function in $functions) {
            if (Test-SignatureResolutionRuleMatch -Rule $rule -Instruction $function) { $matched.Add($function) }
        }
        foreach ($function in $matched) {
            if ([string]$function.returnTypeStatus -ne 'Conditional') {
                throw "Function return rule $($rule.ruleId) targets an already-Resolved or non-Conditional descriptor: $($function.publicKey)"
            }
        }
        if ($matched.Count -ne [int]$rule.expectedMatchCount) {
            throw "Function return rule $($rule.ruleId) expectedMatchCount=$($rule.expectedMatchCount), actual=$($matched.Count)."
        }
        $matchedKeys = Sort-ResolutionOrdinal -Items @($matched | Select-Object -ExpandProperty publicKey) -KeySelector { param($item) [string]$item }
        $ruleMatchReports.Add([pscustomobject][ordered]@{ ruleId=$rule.ruleId; matchedCount=$matched.Count; matchedPublicKeys=@($matchedKeys) })
    }

    $resolved = New-Object System.Collections.Generic.List[object]
    foreach ($function in $functions) {
        $returnCandidates = @($function.returnTypeCandidates)
        $returnStatus = [string]$function.returnTypeStatus
        $selectedReturn = ''
        $returnResolutionStatus = 'UnresolvedStatic'
        $ruleId = ''

        if ($returnStatus -eq 'Resolved') {
            if ($returnCandidates.Count -ne 1) { throw "Resolved DIA-03 function must have exactly one return candidate: $($function.publicKey)" }
            $selectedReturn = [string]$returnCandidates[0]
            $returnResolutionStatus = 'PreservedStatic'
        }
        elseif ($returnStatus -eq 'Conditional') {
            $matches = New-Object System.Collections.Generic.List[object]
            foreach ($rule in $sortedRules) {
                if (Test-SignatureResolutionRuleMatch -Rule $rule -Instruction $function) { $matches.Add($rule) }
            }
            if ($matches.Count -ne 1) { throw "Conditional function return must match exactly one rule: $($function.publicKey) matched=$($matches.Count)" }
            $selectedReturn = [string]$matches[0].selectedReturnType
            if (-not (@($returnCandidates) -ccontains $selectedReturn)) {
                throw "Function rule $($matches[0].ruleId) selected '$selectedReturn', which is not a DIA-03 candidate for $($function.publicKey)."
            }
            $ruleId = [string]$matches[0].ruleId
            $returnResolutionStatus = 'ResolvedStaticByRule'
        }
        elseif ($returnStatus -ne 'Unresolved') { throw "Unsupported DIA-03 returnTypeStatus for $($function.publicKey): $returnStatus" }

        $argumentCandidates = @($function.argumentSchemaCandidates)
        $argumentStatus = [string]$function.argumentSchemaStatus
        $resolvedArgument = ''
        if ($argumentStatus -eq 'Resolved') {
            if ($argumentCandidates.Count -ne 1) { throw "Resolved DIA-03 function must have exactly one argument candidate: $($function.publicKey)" }
            $resolvedArgument = [string]$argumentCandidates[0]
        }
        elseif ($argumentStatus -notin @('Conditional', 'Unresolved')) { throw "Unsupported DIA-03 argumentSchemaStatus for $($function.publicKey): $argumentStatus" }
        $staticSignatureStatus = if (-not [string]::IsNullOrEmpty($selectedReturn) -and -not [string]::IsNullOrEmpty($resolvedArgument)) { 'CompleteStatic' } else { 'IncompleteStatic' }

        $resolved.Add([pscustomobject][ordered]@{
            publicKey=[string]$function.publicKey; targetModule=[string]$function.targetModule; handlerType=[string]$function.handlerType
            sourceReturnTypeStatus=$returnStatus; sourceReturnTypeCandidates=@($returnCandidates)
            resolvedReturnType=$selectedReturn; returnResolutionStatus=$returnResolutionStatus; ruleId=$ruleId
            sourceArgumentSchemaStatus=$argumentStatus; sourceArgumentSchemaCandidates=@($argumentCandidates)
            resolvedArgumentSchema=$resolvedArgument; staticSignatureStatus=$staticSignatureStatus
            overridesArgumentCheck=[bool]$function.overridesArgumentCheck; restructureCandidates=@($function.restructureCandidates)
            completionModeCandidate=[string]$function.completionModeCandidate; effectCandidates=@($function.effectCandidates)
            evidenceStatus='StaticCandidate'; behaviorFixtureStatus='Uncovered'
        })
    }
    $sortedResolved = Sort-ResolutionOrdinal -Items $resolved.ToArray() -KeySelector { param($item) $item.publicKey }

    $canonicalRules = @($sortedRules | ForEach-Object { [ordered]@{
        ruleId=$_.ruleId; handlerType=$_.handlerType; matchKind=$_.matchKind; pattern=$_.pattern
        selectedReturnType=$_.selectedReturnType; expectedMatchCount=$_.expectedMatchCount; rationale=$_.rationale
    } })
    $catalogCanonical = [ordered]@{
        schemaVersion=[string]$Catalog.schemaVersion; catalogId=[string]$Catalog.catalogId; catalogVersion=[string]$Catalog.catalogVersion
        sourceWorkPackage=[string]$Catalog.sourceWorkPackage; sourceDescriptorSetHash=[string]$Catalog.sourceDescriptorSetHash; rules=$canonicalRules
    }
    $catalogJson = $catalogCanonical | ConvertTo-Json -Depth 20 -Compress
    $catalogHash = Get-ResolutionSha256Hex $script:ResolutionUtf8NoBom.GetBytes($catalogJson)
    $resolutionCanonical = [ordered]@{
        schemaVersion='1.0.0'; workPackage='M0-DIA-05'; sourceDescriptorSetHash=[string]$SignatureInventory.descriptorSetHash
        catalogHash=$catalogHash; expressionFunctions=@($sortedResolved)
    }
    $resolutionJson = $resolutionCanonical | ConvertTo-Json -Depth 30 -Compress
    $resolutionSetHash = Get-ResolutionSha256Hex $script:ResolutionUtf8NoBom.GetBytes($resolutionJson)

    $coverage = [ordered]@{
        sourceReturnResolvedCount=@($functions | Where-Object returnTypeStatus -eq 'Resolved').Count
        sourceReturnConditionalCount=@($functions | Where-Object returnTypeStatus -eq 'Conditional').Count
        sourceReturnUnresolvedCount=@($functions | Where-Object returnTypeStatus -eq 'Unresolved').Count
        preservedStaticCount=@($sortedResolved | Where-Object returnResolutionStatus -eq 'PreservedStatic').Count
        resolvedStaticByRuleCount=@($sortedResolved | Where-Object returnResolutionStatus -eq 'ResolvedStaticByRule').Count
        unresolvedCount=@($sortedResolved | Where-Object returnResolutionStatus -eq 'UnresolvedStatic').Count
        argumentSchemaResolvedCount=@($sortedResolved | Where-Object sourceArgumentSchemaStatus -eq 'Resolved').Count
        completeStaticSignatureCount=@($sortedResolved | Where-Object staticSignatureStatus -eq 'CompleteStatic').Count
        catalogRuleCount=$sortedRules.Count
    }
    $report = [ordered]@{
        schemaVersion='1.0.0'; workPackage='M0-DIA-05'; generatedAtUtc=[DateTime]::UtcNow.ToString('o')
        executionStatus='InProgress'; gateStatus='Blocked'; blockerCode='EvidenceMissing'; result='Partial'
        sourceDescriptorSetHash=[string]$SignatureInventory.descriptorSetHash
        catalogId=[string]$Catalog.catalogId; catalogVersion=[string]$Catalog.catalogVersion
        catalogHash=$catalogHash; resolutionSetHash=$resolutionSetHash; expressionFunctionCount=$sortedResolved.Count
        coverage=$coverage; ruleMatches=@($ruleMatchReports.ToArray())
        uncovered=@(
            'CompleteStatic covers only return and argument source resolution; defaults, validation errors, restructure and effects require two-sided fixtures.',
            'Completion and effect fields remain static candidates and behaviorFixtureStatus remains Uncovered.',
            'Module ownership, replacement/comparer policy, D1 plan and D2 runtime registry isolation remain unresolved.',
            'This report is not consumed by the legacy Parser/VM and does not change M0-DIA-02, M0-DIA-03 or M0-DIA-04 hashes.'
        )
        expressionFunctions=@($sortedResolved)
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, ($report | ConvertTo-Json -Depth 40), $script:ResolutionUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectSignatureResolutionReport, New-DialectFunctionSignatureResolutionReport
