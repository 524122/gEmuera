Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:OwnershipUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-OwnershipSha256Hex {
    param([byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Assert-OwnershipHash {
    param([object]$Value, [string]$Name)
    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid ownership source hash: $Name" }
}

function Get-OwnershipSourceFile {
    param([string]$Root, [string]$Suffix, [string]$Kind)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { throw "UpstreamProjectRoot does not exist: $Root" }
    $normalizedSuffix = $Suffix.Replace('\', '/').TrimStart('/')
    $matches = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter ([IO.Path]::GetFileName($Suffix)) |
        Where-Object { $_.FullName.Replace('\', '/').EndsWith($normalizedSuffix, [StringComparison]::OrdinalIgnoreCase) })
    if ($matches.Count -ne 1) { throw "Expected exactly one upstream $Kind source ending '$Suffix', found $($matches.Count)." }
    return $matches[0]
}

function Get-OwnershipUpstreamKeys {
    param([IO.FileInfo]$File, [ValidateSet('Instruction','ExpressionFunction')][string]$Kind)
    $map = New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::Ordinal)
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($File.FullName)) {
        $lineNumber++
        $active = [regex]::Replace($line, '//.*$', '')
        $pattern = if ($Kind -eq 'Instruction') {
            '\b(?:addFunction|addPrintFunction|addPrintDataFunction)\s*\(\s*FunctionCode\.([A-Za-z_][A-Za-z0-9_]*)'
        } else {
            '\[\s*"([^"]+)"\s*\]\s*=\s*new\s+'
        }
        foreach ($match in [regex]::Matches($active, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            $key = [string]$match.Groups[1].Value
            if ($map.ContainsKey($key)) { throw "Duplicate upstream $($Kind.ToLowerInvariant()) key: $key" }
            $map.Add($key, [pscustomobject][ordered]@{ sourceLine=$lineNumber; sourceText=$active.Trim() })
        }
    }
    return $map
}

function Assert-OwnershipKeySet {
    param([object[]]$Expected, [object[]]$Actual, [string]$Context)
    if (@($Expected).Count -ne @($Actual).Count) { throw "$Context key set count mismatch." }
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($item in @($Expected)) {
        $key=[string]$item.publicKey
        if (-not $set.Add($key)) { throw "Duplicate $Context source key: $key" }
    }
    foreach ($item in @($Actual)) { if (-not $set.Contains([string]$item.publicKey)) { throw "$Context key set mismatch: $($item.publicKey)" } }
}

function Sort-OwnershipEntries {
    param([object[]]$Items)
    $map=New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach($item in @($Items)){$map.Add([string]$item.publicKey,$item)}
    return @($map.Values)
}

function New-OwnershipEntries {
    param([object[]]$Current, [object]$UpstreamMap, [object]$StableModuleSet, [string]$RegistryKind, [string]$UpstreamSourceFile)
    $result = New-Object System.Collections.Generic.List[object]
    foreach ($entry in @($Current)) {
        $key=[string]$entry.publicKey
        $target=[string]$entry.targetModule
        $present=$UpstreamMap.ContainsKey($key)
        $candidate='Unresolved';$status='Unresolved';$conflict='None';$upstreamLine=$null;$upstreamText=''
        if ($present) {
            $candidate='emuera.upstream';$status='UpstreamNameMatch'
            $upstreamLine=[int]$UpstreamMap[$key].sourceLine;$upstreamText=[string]$UpstreamMap[$key].sourceText
            if ($StableModuleSet.Contains($target) -and $target -cne 'emuera.upstream') { $conflict='CurrentTargetDiffersFromUpstreamCandidate' }
        } elseif ($StableModuleSet.Contains($target)) {
            $candidate=$target;$status='ExplicitCurrentModuleCandidate'
        }
        $result.Add([pscustomobject][ordered]@{
            publicKey=$key;registryKind=$RegistryKind;currentTargetModule=$target;currentHandler=[string]$entry.handler
            currentSourceFile=[string]$entry.sourceFile;currentSourceLine=[int]$entry.sourceLine
            upstreamPresence=$(if($present){'Present'}else{'Absent'});upstreamSourceFile=$(if($present){$UpstreamSourceFile}else{''});upstreamSourceLine=$upstreamLine;upstreamSourceText=$upstreamText
            ownershipEvidenceStatus=$status;stableModuleCandidate=$candidate;ownershipConflictStatus=$conflict
            nameComparerCandidate='LegacyConfig.ICFunctionCandidate';nameComparerStatus='Unresolved'
            aliasStatus='Unresolved';replacementStatus='Unresolved';behaviorCompatibilityStatus='Uncovered';completionEffectStatus='Uncovered'
        })
    }
    return Sort-OwnershipEntries $result.ToArray()
}

function New-DialectOwnershipEvidenceReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true,Position=0)][object]$Inventory,
        [Parameter(Mandatory=$true,Position=1)][object]$RegistrySnapshot,
        [Parameter(Mandatory=$true,Position=2)][object]$SignatureInventory,
        [Parameter(Mandatory=$true,Position=3)][object]$InstructionSignatureResolution,
        [Parameter(Mandatory=$true,Position=4)][object]$FunctionSignatureResolution,
        [Parameter(Mandatory=$true,Position=5)][object]$InstructionFlagResolution,
        [Parameter(Mandatory=$true,Position=6)][object]$Catalog,
        [Parameter(Mandatory=$true,Position=7)][string]$UpstreamProjectRoot,
        [string]$OutputPath=''
    )
    if ([string]$Inventory.workPackage -ne 'M0-DIA-01') { throw 'Ownership evidence requires M0-DIA-01.' }
    Assert-OwnershipHash $Inventory.canonicalHash 'DIA-01 canonicalHash'
    if ([string]$RegistrySnapshot.workPackage -ne 'M0-DIA-02' -or [string]$RegistrySnapshot.sourceInventoryHash -cne [string]$Inventory.canonicalHash) { throw 'DIA-02 does not match DIA-01.' }
    Assert-OwnershipHash $RegistrySnapshot.snapshotSetHash 'DIA-02 snapshotSetHash'
    if ([string]$RegistrySnapshot.currentRuntimeIsolation.status -cne 'Passed' -and [string]$RegistrySnapshot.currentRuntimeIsolation.status -cne 'Failed') { throw 'DIA-02 current runtime isolation has an unsupported status.' }
    if ([string]$SignatureInventory.workPackage -ne 'M0-DIA-03' -or [string]$SignatureInventory.sourceInventoryHash -cne [string]$Inventory.canonicalHash) { throw 'DIA-03 does not match DIA-01.' }
    Assert-OwnershipHash $SignatureInventory.descriptorSetHash 'DIA-03 descriptorSetHash'
    if ([string]$InstructionSignatureResolution.workPackage -ne 'M0-DIA-04' -or [string]$InstructionSignatureResolution.sourceDescriptorSetHash -cne [string]$SignatureInventory.descriptorSetHash) { throw 'DIA-04 does not match DIA-03.' }
    Assert-OwnershipHash $InstructionSignatureResolution.resolutionSetHash 'DIA-04 resolutionSetHash'
    if ([string]$FunctionSignatureResolution.workPackage -ne 'M0-DIA-05' -or [string]$FunctionSignatureResolution.sourceDescriptorSetHash -cne [string]$SignatureInventory.descriptorSetHash) { throw 'DIA-05 does not match DIA-03.' }
    Assert-OwnershipHash $FunctionSignatureResolution.resolutionSetHash 'DIA-05 resolutionSetHash'
    if ([string]$InstructionFlagResolution.workPackage -ne 'M0-DIA-06' -or [string]$InstructionFlagResolution.sourceDescriptorSetHash -cne [string]$SignatureInventory.descriptorSetHash -or [string]$InstructionFlagResolution.sourceInstructionSignatureResolutionHash -cne [string]$InstructionSignatureResolution.resolutionSetHash) { throw 'DIA-06 does not match DIA-03/DIA-04.' }
    Assert-OwnershipHash $InstructionFlagResolution.resolutionSetHash 'DIA-06 resolutionSetHash'
    Assert-OwnershipKeySet @($Inventory.instructionRegistrations) @($SignatureInventory.instructions) 'DIA-01/DIA-03 instruction'
    Assert-OwnershipKeySet @($Inventory.instructionRegistrations) @($InstructionSignatureResolution.instructions) 'DIA-01/DIA-04 instruction'
    Assert-OwnershipKeySet @($Inventory.instructionRegistrations) @($InstructionFlagResolution.instructions) 'DIA-01/DIA-06 instruction'
    Assert-OwnershipKeySet @($Inventory.expressionRegistrations) @($SignatureInventory.expressionFunctions) 'DIA-01/DIA-03 expression'
    Assert-OwnershipKeySet @($Inventory.expressionRegistrations) @($FunctionSignatureResolution.expressionFunctions) 'DIA-01/DIA-05 expression'
    if ([string]$Catalog.schemaVersion -ne '1.0.0' -or [string]$Catalog.sourceWorkPackage -ne 'M0-DIA-07' -or [string]$Catalog.catalogVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) { throw 'Unsupported ownership catalog.' }

    $stableModules = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach($module in @($Catalog.stableCurrentModuleCandidates)){ if(-not $stableModules.Add([string]$module)){throw "Duplicate stable module candidate: $module"} }
    if ($stableModules.Contains('game.snake.candidate') -or $stableModules.Contains('legacy.common.unresolved') -or $stableModules.Contains('legacy.expression.unresolved')) { throw 'Unresolved/candidate buckets cannot be stable ownership modules.' }
    $instructionFile=Get-OwnershipSourceFile $UpstreamProjectRoot ([string]$Catalog.upstream.instructionSourceSuffix) 'instruction'
    $functionFile=Get-OwnershipSourceFile $UpstreamProjectRoot ([string]$Catalog.upstream.expressionSourceSuffix) 'expression'
    $instructionHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $instructionFile.FullName).Hash.ToLowerInvariant()
    $functionHash=(Get-FileHash -Algorithm SHA256 -LiteralPath $functionFile.FullName).Hash.ToLowerInvariant()
    if ($instructionHash -cne [string]$Catalog.upstream.instructionSourceSha256) { throw 'Upstream instruction source hash drifted.' }
    if ($functionHash -cne [string]$Catalog.upstream.expressionSourceSha256) { throw 'Upstream expression source hash drifted.' }
    $upInstructions=Get-OwnershipUpstreamKeys $instructionFile 'Instruction';$upFunctions=Get-OwnershipUpstreamKeys $functionFile 'ExpressionFunction'
    if ($upInstructions.Count -ne [int]$Catalog.upstream.expectedInstructionCount) { throw 'Upstream instruction key count drifted.' }
    if ($upFunctions.Count -ne [int]$Catalog.upstream.expectedExpressionFunctionCount) { throw 'Upstream expression key count drifted.' }
    $instructionSuffix=[string]$Catalog.upstream.instructionSourceSuffix;$functionSuffix=[string]$Catalog.upstream.expressionSourceSuffix
    $instructions=New-OwnershipEntries @($Inventory.instructionRegistrations) $upInstructions $stableModules 'Instruction' $instructionSuffix
    $functions=New-OwnershipEntries @($Inventory.expressionRegistrations) $upFunctions $stableModules 'ExpressionFunction' $functionSuffix

    $catalogCanonical=[ordered]@{schemaVersion='1.0.0';catalogId=[string]$Catalog.catalogId;catalogVersion=[string]$Catalog.catalogVersion;sourceWorkPackage='M0-DIA-07';stableCurrentModuleCandidates=@($Catalog.stableCurrentModuleCandidates|Sort-Object);upstream=[ordered]@{instructionSourceSuffix=$instructionSuffix;expressionSourceSuffix=$functionSuffix;instructionSourceSha256=$instructionHash;expressionSourceSha256=$functionHash;expectedInstructionCount=$upInstructions.Count;expectedExpressionFunctionCount=$upFunctions.Count}}
    $catalogHash=Get-OwnershipSha256Hex $script:OwnershipUtf8NoBom.GetBytes(($catalogCanonical|ConvertTo-Json -Depth 10 -Compress))
    $canonical=[ordered]@{schemaVersion='1.0.0';workPackage='M0-DIA-07';sourceInventoryHash=[string]$Inventory.canonicalHash;sourceRegistrySnapshotHash=[string]$RegistrySnapshot.snapshotSetHash;sourceDescriptorSetHash=[string]$SignatureInventory.descriptorSetHash;sourceInstructionSignatureResolutionHash=[string]$InstructionSignatureResolution.resolutionSetHash;sourceFunctionSignatureResolutionHash=[string]$FunctionSignatureResolution.resolutionSetHash;sourceInstructionFlagResolutionHash=[string]$InstructionFlagResolution.resolutionSetHash;upstreamInstructionSourceSha256=$instructionHash;upstreamExpressionSourceSha256=$functionHash;catalogHash=$catalogHash;instructions=@($instructions);expressionFunctions=@($functions)}
    $evidenceHash=Get-OwnershipSha256Hex $script:OwnershipUtf8NoBom.GetBytes(($canonical|ConvertTo-Json -Depth 30 -Compress))
    $all=@($instructions)+@($functions)
    $coverage=[ordered]@{upstreamInstructionNameMatchCount=@($instructions|Where-Object upstreamPresence -eq 'Present').Count;upstreamExpressionNameMatchCount=@($functions|Where-Object upstreamPresence -eq 'Present').Count;explicitCurrentModuleCandidateCount=@($all|Where-Object ownershipEvidenceStatus -eq 'ExplicitCurrentModuleCandidate').Count;unresolvedOwnershipCount=@($all|Where-Object ownershipEvidenceStatus -eq 'Unresolved').Count;nameComparerUnresolvedCount=@($all).Count;aliasUnresolvedCount=@($all).Count;replacementUnresolvedCount=@($all).Count;behaviorUncoveredCount=@($all).Count;completionEffectUncoveredCount=@($all).Count}
    $report=[ordered]@{schemaVersion='1.0.0';workPackage='M0-DIA-07';generatedAtUtc=[DateTime]::UtcNow.ToString('o');executionStatus='InProgress';gateStatus='Blocked';blockerCode='EvidenceMissing';result='Partial';sourceInventoryHash=[string]$Inventory.canonicalHash;sourceRegistrySnapshotHash=[string]$RegistrySnapshot.snapshotSetHash;sourceDescriptorSetHash=[string]$SignatureInventory.descriptorSetHash;sourceInstructionSignatureResolutionHash=[string]$InstructionSignatureResolution.resolutionSetHash;sourceFunctionSignatureResolutionHash=[string]$FunctionSignatureResolution.resolutionSetHash;sourceInstructionFlagResolutionHash=[string]$InstructionFlagResolution.resolutionSetHash;catalogId=[string]$Catalog.catalogId;catalogVersion=[string]$Catalog.catalogVersion;catalogHash=$catalogHash;evidenceSetHash=$evidenceHash;upstreamIdentity=[ordered]@{instructionSourceFile=$instructionSuffix;instructionSourceSha256=$instructionHash;expressionSourceFile=$functionSuffix;expressionSourceSha256=$functionHash};instructionCount=$instructions.Count;expressionFunctionCount=$functions.Count;coverage=$coverage;currentRuntimeIsolation=[ordered]@{status='Failed';reason='Legacy static registration still mixes profile contributions.'};uncovered=@('Upstream public-key presence is provenance only and does not prove behavioral compatibility.','Name comparer, alias and replacement contracts remain unresolved.','Completion/effect and all behavior fixtures remain Uncovered.','This report is not consumed by the legacy Parser/VM and does not implement D1/D2 runtime selection.');instructions=@($instructions);expressionFunctions=@($functions)}
    if(-not[string]::IsNullOrWhiteSpace($OutputPath)){$full=[IO.Path]::GetFullPath($OutputPath);[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full))|Out-Null;[IO.File]::WriteAllText($full,($report|ConvertTo-Json -Depth 40),$script:OwnershipUtf8NoBom)}
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectOwnershipEvidenceReport
