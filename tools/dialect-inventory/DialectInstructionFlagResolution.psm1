Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:FlagUtf8NoBom=New-Object Text.UTF8Encoding($false)

function Get-FlagSha256Hex {
    param([byte[]]$Bytes)
    $sha=[Security.Cryptography.SHA256]::Create();try{return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant()}finally{$sha.Dispose()}
}
function Sort-FlagOrdinal {
    param([object[]]$Items,[scriptblock]$KeySelector)
    $map=New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal);$n=0
    foreach($item in @($Items)){$key=[string](& $KeySelector $item);$map.Add($key+[char]0+$n.ToString('D10',[Globalization.CultureInfo]::InvariantCulture),$item);$n++}
    return @($map.Values)
}
function Assert-FlagText {param([object]$Value,[string]$Name) if([string]::IsNullOrWhiteSpace([string]$Value)){throw "Instruction flag field is required: $Name"}}
function Get-FlagTokens {
    param([string]$Expression,[object]$KnownSet,[string]$Context,[switch]$IgnoreUnknown)
    $set=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach($raw in @($Expression -split '\|')){
        $token=$raw.Trim();if([string]::IsNullOrEmpty($token)-or $token -eq '0'){continue}
        if(-not $KnownSet.Contains($token)){if($IgnoreUnknown){continue};throw "Unknown instruction flag token '$token' in $Context"}
        [void]$set.Add($token)
    }
    return Sort-FlagOrdinal @($set) {param($x)[string]$x}
}
function Test-FlagRuleMatch {
    param([object]$Rule,[object]$Instruction)
    if([string]$Rule.handlerType -cne [string]$Instruction.handlerType){return $false}
    if([string]$Rule.matchKind -eq 'Exact'){return [string]$Rule.pattern -ceq [string]$Instruction.publicKey}
    return $Rule.compiledRegex.IsMatch([string]$Instruction.publicKey)
}

function New-DialectInstructionFlagResolutionReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true,Position=0)][object]$SignatureInventory,
        [Parameter(Mandatory=$true,Position=1)][object]$InstructionSignatureResolution,
        [Parameter(Mandatory=$true,Position=2)][object]$Catalog,
        [string]$OutputPath=''
    )
    if([string]$SignatureInventory.workPackage -ne 'M0-DIA-03'-or [string]$SignatureInventory.descriptorSetHash -notmatch '^[0-9a-f]{64}$'){throw 'Flag resolution requires a valid M0-DIA-03 inventory.'}
    if([string]$InstructionSignatureResolution.workPackage -ne 'M0-DIA-04'-or [string]$InstructionSignatureResolution.resolutionSetHash -notmatch '^[0-9a-f]{64}$'){throw 'Flag resolution requires a valid M0-DIA-04 instruction signature resolution.'}
    if([string]$InstructionSignatureResolution.sourceDescriptorSetHash -cne [string]$SignatureInventory.descriptorSetHash){throw 'DIA-04 sourceDescriptorSetHash does not match DIA-03.'}
    if([string]$Catalog.schemaVersion -ne '1.0.0'){throw "Unsupported instruction flag catalog schemaVersion: $($Catalog.schemaVersion)"}
    Assert-FlagText $Catalog.catalogId 'catalogId';Assert-FlagText $Catalog.catalogVersion 'catalogVersion'
    if([string]$Catalog.sourceWorkPackage -ne 'M0-DIA-03'){throw 'Instruction flag catalog sourceWorkPackage must be M0-DIA-03.'}
    if([string]$Catalog.sourceDescriptorSetHash -cne [string]$SignatureInventory.descriptorSetHash){throw 'Instruction flag catalog sourceDescriptorSetHash does not match DIA-03.'}
    if([string]$Catalog.sourceInstructionSignatureResolutionHash -cne [string]$InstructionSignatureResolution.resolutionSetHash){throw 'Instruction flag catalog sourceInstructionSignatureResolutionHash does not match DIA-04.'}

    $knownSet=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach($flag in @($Catalog.knownFlags)){Assert-FlagText $flag 'knownFlags[]';if(-not $knownSet.Add([string]$flag)){throw "Duplicate known flag: $flag"}}
    if($knownSet.Count -eq 0){throw 'Instruction flag catalog has no knownFlags.'}
    $knownFlags=Sort-FlagOrdinal @($knownSet) {param($x)[string]$x}

    $source=@($SignatureInventory.instructions);$args=@($InstructionSignatureResolution.instructions)
    if($source.Count -eq 0-or $source.Count -ne $args.Count){throw 'DIA-03/DIA-04 instruction key set count mismatch.'}
    $sourceMap=@{};$argMap=@{}
    foreach($d in $source){Assert-FlagText $d.publicKey 'DIA-03 publicKey';if($sourceMap.ContainsKey([string]$d.publicKey)){throw "Duplicate DIA-03 instruction key: $($d.publicKey)"};$sourceMap[[string]$d.publicKey]=$d}
    foreach($d in $args){Assert-FlagText $d.publicKey 'DIA-04 publicKey';if($argMap.ContainsKey([string]$d.publicKey)){throw "Duplicate DIA-04 instruction key: $($d.publicKey)"};$argMap[[string]$d.publicKey]=$d}
    foreach($key in $sourceMap.Keys){
        if(-not $argMap.ContainsKey($key)){throw "DIA-03/DIA-04 instruction key set mismatch: $key"}
        $s=$sourceMap[$key];$a=$argMap[$key]
        if([string]$s.targetModule -cne [string]$a.targetModule-or [string]$s.handlerType -cne [string]$a.handlerType){throw "DIA-03/DIA-04 descriptor identity mismatch: $key"}
        if([string]::IsNullOrEmpty([string]$a.resolvedArgumentSchema)){throw "DIA-04 argument is unresolved: $key"}
    }

    $ids=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal);$selectors=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    $normalized=New-Object System.Collections.Generic.List[object]
    foreach($rule in @($Catalog.rules)){
        Assert-FlagText $rule.ruleId 'rules[].ruleId';Assert-FlagText $rule.handlerType 'rules[].handlerType';Assert-FlagText $rule.pattern 'rules[].pattern';Assert-FlagText $rule.rationale 'rules[].rationale'
        if(-not $ids.Add([string]$rule.ruleId)){throw "Duplicate ruleId in instruction flag catalog: $($rule.ruleId)"}
        if([string]$rule.matchKind -notin @('Exact','PublicKeyRegex')){throw "Unsupported instruction flag matchKind: $($rule.matchKind)"}
        $selector=[string]$rule.handlerType+[char]0+[string]$rule.matchKind+[char]0+[string]$rule.pattern
        if(-not $selectors.Add($selector)){throw "Duplicate rule selector in instruction flag catalog: $($rule.ruleId)"}
        $selectedSet=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
        foreach($flag in @($rule.selectedHandlerFlags)){if(-not $knownSet.Contains([string]$flag)){throw "Rule $($rule.ruleId) selects unknown flag: $flag"};if(-not $selectedSet.Add([string]$flag)){throw "Rule $($rule.ruleId) contains duplicate selected flag: $flag"}}
        if($selectedSet.Count -eq 0){throw "Rule $($rule.ruleId) selects no handler flags."}
        $expected=[int]$rule.expectedMatchCount;if($expected -lt 1){throw "Rule expectedMatchCount must be positive: $($rule.ruleId)"}
        $regex=$null
        if([string]$rule.matchKind -eq 'PublicKeyRegex'){
            if(-not([string]$rule.pattern).StartsWith('^',[StringComparison]::Ordinal)-or-not([string]$rule.pattern).EndsWith('$',[StringComparison]::Ordinal)){throw "PublicKeyRegex must be anchored for rule $($rule.ruleId)"}
            try{$regex=New-Object Text.RegularExpressions.Regex([string]$rule.pattern,([Text.RegularExpressions.RegexOptions]::CultureInvariant-bor[Text.RegularExpressions.RegexOptions]::ExplicitCapture),[TimeSpan]::FromMilliseconds(250))}catch{throw "Invalid flag regex for rule $($rule.ruleId): $($_.Exception.Message)"}
        }
        $normalized.Add([pscustomobject][ordered]@{ruleId=[string]$rule.ruleId;handlerType=[string]$rule.handlerType;matchKind=[string]$rule.matchKind;pattern=[string]$rule.pattern;selectedHandlerFlags=@(Sort-FlagOrdinal @($selectedSet){param($x)[string]$x});expectedMatchCount=$expected;rationale=[string]$rule.rationale;compiledRegex=$regex})
    }
    if($normalized.Count -eq 0){throw 'Instruction flag catalog has no rules.'}
    $rules=Sort-FlagOrdinal $normalized.ToArray(){param($x)$x.ruleId};$ruleReports=New-Object System.Collections.Generic.List[object]
    foreach($rule in $rules){
        $matched=New-Object System.Collections.Generic.List[object]
        foreach($d in $source){if(Test-FlagRuleMatch $rule $d){$matched.Add($d)}}
        foreach($d in $matched){
            if(@($d.handlerFlagCandidates).Count -le 1){throw "Instruction flag rule $($rule.ruleId) targets a non-multi-candidate descriptor: $($d.publicKey)"}
            $candidateSet=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
            foreach($expr in @($d.handlerFlagCandidates)){foreach($flag in @(Get-FlagTokens ([string]$expr) $knownSet "handler candidates $($d.publicKey)" -IgnoreUnknown)){[void]$candidateSet.Add($flag)}}
            foreach($flag in @($rule.selectedHandlerFlags)){if(-not $candidateSet.Contains($flag)){throw "Rule $($rule.ruleId) flag '$flag' is not present in DIA-03 handler candidates for $($d.publicKey)"}}
        }
        if($matched.Count -ne [int]$rule.expectedMatchCount){throw "Instruction flag rule $($rule.ruleId) expectedMatchCount=$($rule.expectedMatchCount), actual=$($matched.Count)."}
        $keys=Sort-FlagOrdinal @($matched|Select-Object -ExpandProperty publicKey){param($x)[string]$x}
        $ruleReports.Add([pscustomobject][ordered]@{ruleId=$rule.ruleId;matchedCount=$matched.Count;matchedPublicKeys=@($keys)})
    }

    $result=New-Object System.Collections.Generic.List[object]
    foreach($d in $source){
        $key=[string]$d.publicKey;$effective=New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
        foreach($flag in @(Get-FlagTokens ([string]$d.registrationFlagExpression) $knownSet "registration $key")){[void]$effective.Add($flag)}
        $candidateCount=@($d.handlerFlagCandidates).Count;$status='';$ruleIds=@()
        if($candidateCount -eq 0){if(-not[string]::IsNullOrEmpty([string]$d.handlerType)){throw "Handler descriptor has no flag candidate: $key"};$status='DirectRegistrationStatic'}
        elseif($candidateCount -eq 1){foreach($flag in @(Get-FlagTokens ([string]$d.handlerFlagCandidates[0]) $knownSet "single handler $key")){[void]$effective.Add($flag)};$status='HandlerSingleStatic'}
        else{
            $matches=New-Object System.Collections.Generic.List[object];foreach($rule in $rules){if(Test-FlagRuleMatch $rule $d){$matches.Add($rule)}}
            if($matches.Count -lt 1){throw "Multi-candidate instruction must match at least one rule: $key"}
            foreach($rule in $matches){foreach($flag in @($rule.selectedHandlerFlags)){[void]$effective.Add([string]$flag)}}
            $ruleIds=Sort-FlagOrdinal @($matches|Select-Object -ExpandProperty ruleId){param($x)[string]$x};$status='HandlerRulesStatic'
        }
        $a=$argMap[$key]
        $result.Add([pscustomobject][ordered]@{
            publicKey=$key;targetModule=[string]$d.targetModule;handlerType=[string]$d.handlerType
            resolvedArgumentSchema=[string]$a.resolvedArgumentSchema;argumentResolutionStatus=[string]$a.resolutionStatus
            registrationFlagExpression=[string]$d.registrationFlagExpression;handlerFlagCandidates=@($d.handlerFlagCandidates)
            effectiveFlags=@(Sort-FlagOrdinal @($effective){param($x)[string]$x});flagResolutionStatus=$status;flagRuleIds=@($ruleIds)
            completionModeCandidate=[string]$d.completionModeCandidate;effectCandidates=@($d.effectCandidates)
            evidenceStatus='StaticCandidate';behaviorFixtureStatus='Uncovered'
        })
    }
    $sorted=Sort-FlagOrdinal $result.ToArray(){param($x)$x.publicKey}
    $canonicalRules=@($rules|%{[ordered]@{ruleId=$_.ruleId;handlerType=$_.handlerType;matchKind=$_.matchKind;pattern=$_.pattern;selectedHandlerFlags=@($_.selectedHandlerFlags);expectedMatchCount=$_.expectedMatchCount;rationale=$_.rationale}})
    $catalogCanonical=[ordered]@{schemaVersion='1.0.0';catalogId=[string]$Catalog.catalogId;catalogVersion=[string]$Catalog.catalogVersion;sourceWorkPackage='M0-DIA-03';sourceDescriptorSetHash=[string]$Catalog.sourceDescriptorSetHash;sourceInstructionSignatureResolutionHash=[string]$Catalog.sourceInstructionSignatureResolutionHash;knownFlags=@($knownFlags);rules=$canonicalRules}
    $catalogHash=Get-FlagSha256Hex $script:FlagUtf8NoBom.GetBytes(($catalogCanonical|ConvertTo-Json -Depth 30 -Compress))
    $canonical=[ordered]@{schemaVersion='1.0.0';workPackage='M0-DIA-06';sourceDescriptorSetHash=[string]$SignatureInventory.descriptorSetHash;sourceInstructionSignatureResolutionHash=[string]$InstructionSignatureResolution.resolutionSetHash;catalogHash=$catalogHash;instructions=@($sorted)}
    $resolutionHash=Get-FlagSha256Hex $script:FlagUtf8NoBom.GetBytes(($canonical|ConvertTo-Json -Depth 40 -Compress))
    $coverage=[ordered]@{directRegistrationStaticCount=@($sorted|? flagResolutionStatus -eq 'DirectRegistrationStatic').Count;handlerSingleStaticCount=@($sorted|? flagResolutionStatus -eq 'HandlerSingleStatic').Count;handlerRulesStaticCount=@($sorted|? flagResolutionStatus -eq 'HandlerRulesStatic').Count;unresolvedCount=0;knownFlagCount=$knownFlags.Count;catalogRuleCount=$rules.Count}
    $report=[ordered]@{schemaVersion='1.0.0';workPackage='M0-DIA-06';generatedAtUtc=[DateTime]::UtcNow.ToString('o');executionStatus='InProgress';gateStatus='Blocked';blockerCode='EvidenceMissing';result='Partial';sourceDescriptorSetHash=[string]$SignatureInventory.descriptorSetHash;sourceInstructionSignatureResolutionHash=[string]$InstructionSignatureResolution.resolutionSetHash;catalogId=[string]$Catalog.catalogId;catalogVersion=[string]$Catalog.catalogVersion;catalogHash=$catalogHash;resolutionSetHash=$resolutionHash;instructionCount=$sorted.Count;coverage=$coverage;knownFlags=@($knownFlags);ruleMatches=@($ruleReports.ToArray());uncovered=@('Effective flags are static constructor results; Parser/VM flag behavior still requires two-sided fixtures.','Completion/effect fields remain StaticCandidate and behaviorFixtureStatus remains Uncovered.','Module ownership, replacement/comparer, D1 plan and D2 runtime isolation remain unresolved.','This report is not consumed by the legacy Parser/VM and does not change M0-DIA-02 through M0-DIA-05 hashes.');instructions=@($sorted)}
    if(-not[string]::IsNullOrWhiteSpace($OutputPath)){$full=[IO.Path]::GetFullPath($OutputPath);[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full))|Out-Null;[IO.File]::WriteAllText($full,($report|ConvertTo-Json -Depth 50),$script:FlagUtf8NoBom)}
    return [pscustomobject]$report
}
Export-ModuleMember -Function New-DialectInstructionFlagResolutionReport
