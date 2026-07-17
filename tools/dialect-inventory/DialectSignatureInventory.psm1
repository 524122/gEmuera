Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:SignatureUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-SignatureSha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-SignatureRelativePath {
    param([string]$Root, [string]$Path)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) { throw "Source path escapes project root: $fullPath" }
    return $fullPath.Substring($rootPath.Length).Replace('\', '/')
}

function Sort-SignatureOrdinal {
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

function Normalize-CSharpEvidence {
    param([AllowEmptyString()][string]$Value)
    if ($null -eq $Value) { return '' }
    return ([regex]::Replace($Value, '\s+', ' ')).Trim()
}

function Get-CSharpBlockEndIndex {
    param([string]$Text, [int]$OpenBraceIndex)
    if ($OpenBraceIndex -lt 0 -or $OpenBraceIndex -ge $Text.Length -or $Text[$OpenBraceIndex] -ne '{') { throw 'Invalid C# block start.' }
    $depth = 0
    $state = 'Normal'
    :scanLoop for ($i = $OpenBraceIndex; $i -lt $Text.Length; $i++) {
        $c = $Text[$i]
        $next = if ($i + 1 -lt $Text.Length) { $Text[$i + 1] } else { [char]0 }
        switch ($state) {
            'LineComment' {
                if ($c -eq "`n") { $state = 'Normal' }
                continue scanLoop
            }
            'BlockComment' {
                if ($c -eq '*' -and $next -eq '/') { $state = 'Normal'; $i++ }
                continue scanLoop
            }
            'String' {
                if ($c -eq '\') { $i++; continue scanLoop }
                if ($c -eq '"') { $state = 'Normal' }
                continue scanLoop
            }
            'VerbatimString' {
                if ($c -eq '"' -and $next -eq '"') { $i++; continue scanLoop }
                if ($c -eq '"') { $state = 'Normal' }
                continue scanLoop
            }
            'Char' {
                if ($c -eq '\') { $i++; continue scanLoop }
                if ($c -eq "'") { $state = 'Normal' }
                continue scanLoop
            }
        }
        if ($c -eq '/' -and $next -eq '/') { $state = 'LineComment'; $i++; continue scanLoop }
        if ($c -eq '/' -and $next -eq '*') { $state = 'BlockComment'; $i++; continue scanLoop }
        if ($c -eq '"') {
            $state = if ($i -gt 0 -and $Text[$i - 1] -eq '@') { 'VerbatimString' } else { 'String' }
            continue scanLoop
        }
        if ($c -eq "'") { $state = 'Char'; continue scanLoop }
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') {
            $depth--
            if ($depth -eq 0) { return $i }
            if ($depth -lt 0) { break }
        }
    }
    throw 'Unclosed C# class block.'
}

function Get-CSharpClassIndex {
    param([string]$ProjectRoot, [string[]]$Paths, [ValidateSet('AbstractInstruction','FunctionMethod')][string]$BaseType)
    $index = @{}
    foreach ($path in $Paths) {
        $fullPath = [IO.Path]::GetFullPath($path)
        $relativePath = Get-SignatureRelativePath -Root $ProjectRoot -Path $fullPath
        $text = [IO.File]::ReadAllText($fullPath, [Text.Encoding]::UTF8)
        $pattern = '(?m)\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*' + [regex]::Escape($BaseType) + '\b'
        foreach ($match in [regex]::Matches($text, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            $open = $text.IndexOf('{', $match.Index + $match.Length)
            if ($open -lt 0) { throw "Class body start not found: $relativePath $($match.Groups['name'].Value)" }
            $end = Get-CSharpBlockEndIndex -Text $text -OpenBraceIndex $open
            $block = $text.Substring($match.Index, $end - $match.Index + 1)
            $line = [regex]::Matches($text.Substring(0, $match.Index), "`n").Count + 1
            $entry = [pscustomobject][ordered]@{
                className = $match.Groups['name'].Value
                sourceFile = $relativePath
                sourceLine = $line
                blockText = $block
            }
            if (-not $index.ContainsKey($entry.className)) { $index[$entry.className] = New-Object System.Collections.Generic.List[object] }
            $index[$entry.className].Add($entry)
        }
    }
    return $index
}

function Get-UniqueEvidenceCandidates {
    param([string]$BlockText, [string]$Pattern)
    if ([string]::IsNullOrEmpty($BlockText)) { return @() }
    $set = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($BlockText, $Pattern, [Text.RegularExpressions.RegexOptions]::Singleline -bor [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        $value = Normalize-CSharpEvidence $match.Groups['expr'].Value
        if (-not [string]::IsNullOrWhiteSpace($value)) { [void]$set.Add($value) }
    }
    return Sort-SignatureOrdinal -Items @($set) -KeySelector { param($item) [string]$item }
}

function Get-EvidenceStatus {
    param([AllowNull()][AllowEmptyCollection()][object[]]$Candidates)
    $count = 0
    foreach ($candidate in $Candidates) { $count++ }
    if ($count -eq 0) { return 'Unresolved' }
    if ($count -eq 1) { return 'Resolved' }
    return 'Conditional'
}

function Split-CSharpCallArguments {
    param([string]$Value)
    $result = New-Object System.Collections.Generic.List[string]
    $start = 0; $paren = 0; $bracket = 0; $brace = 0; $state = 'Normal'
    for ($i = 0; $i -lt $Value.Length; $i++) {
        $c = $Value[$i]
        $next = if ($i + 1 -lt $Value.Length) { $Value[$i + 1] } else { [char]0 }
        if ($state -eq 'String') { if ($c -eq '\') { $i++ } elseif ($c -eq '"') { $state='Normal' }; continue }
        if ($state -eq 'VerbatimString') { if ($c -eq '"' -and $next -eq '"') { $i++ } elseif ($c -eq '"') { $state='Normal' }; continue }
        if ($state -eq 'Char') { if ($c -eq '\') { $i++ } elseif ($c -eq "'") { $state='Normal' }; continue }
        if ($c -eq '"') { $state = if ($i -gt 0 -and $Value[$i-1] -eq '@') {'VerbatimString'} else {'String'}; continue }
        if ($c -eq "'") { $state='Char'; continue }
        if ($c -eq '(') { $paren++ } elseif ($c -eq ')') { $paren-- }
        elseif ($c -eq '[') { $bracket++ } elseif ($c -eq ']') { $bracket-- }
        elseif ($c -eq '{') { $brace++ } elseif ($c -eq '}') { $brace-- }
        elseif ($c -eq ',' -and $paren -eq 0 -and $bracket -eq 0 -and $brace -eq 0) {
            $result.Add((Normalize-CSharpEvidence $Value.Substring($start, $i - $start))); $start = $i + 1
        }
    }
    $result.Add((Normalize-CSharpEvidence $Value.Substring($start)))
    return $result.ToArray()
}

function Get-ClassResolution {
    param([hashtable]$Index, [string]$ClassName)
    if ([string]::IsNullOrWhiteSpace($ClassName) -or -not $Index.ContainsKey($ClassName)) {
        return [pscustomobject]@{ status='Unresolved'; entry=$null }
    }
    $entries = $Index[$ClassName].ToArray()
    if ($entries.Count -ne 1) { return [pscustomobject]@{ status='Ambiguous'; entry=$null } }
    return [pscustomobject]@{ status='Resolved'; entry=$entries[0] }
}

function Get-EffectCandidates {
    param([AllowEmptyString()][string]$BlockText)
    $list = New-Object System.Collections.Generic.List[string]
    if ($BlockText -match '(?:exm|GlobalStatic)\.Console|WaitInput\s*\(|ReadAnyKey\s*\(') { $list.Add('ConsoleOrInputCandidate') }
    if ($BlockText -match '\b(?:File|Directory|EraStreamReader|EraDataReader|EraDataWriter)\b') { $list.Add('FileIoCandidate') }
    if ($BlockText -match '\b(?:Sql|SQLite|RuntimeDataStore|DataTable|XmlDocument)\b') { $list.Add('DataCapabilityCandidate') }
    if ($BlockText -match '\b(?:Graphics|Sprite|CBG|ImageLayer|PlaySound|PlayBGM|StopSound|StopBGM)\b') { $list.Add('MediaOrDisplayCandidate') }
    if ($BlockText -match '\b(?:VEvaluator|VariableData|GlobalStatic|ProcessState|state\.)\b') { $list.Add('SessionStateCandidate') }
    if ($list.Count -eq 0) { $list.Add('ValueOrUnclassifiedCandidate') }
    return Sort-SignatureOrdinal -Items $list.ToArray() -KeySelector { param($item) [string]$item }
}

function New-DialectSignatureInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][object]$Inventory,
        [string]$OutputPath = ''
    )
    $resolvedRoot = [IO.Path]::GetFullPath($ProjectRoot)
    if ([string]$Inventory.canonicalHash -notmatch '^[0-9a-f]{64}$') { throw 'Signature inventory requires a valid M0-DIA source inventory hash.' }
    $instructionFiles = @(Get-ChildItem (Join-Path $resolvedRoot 'Scripts\Emuera\GameProc\Function') -File -Filter '*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    $methodFiles = @(Get-ChildItem (Join-Path $resolvedRoot 'Scripts\Emuera\GameData\Function') -File -Filter 'Creator.Method*.cs' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName)
    $instructionClassIndex = Get-CSharpClassIndex -ProjectRoot $resolvedRoot -Paths $instructionFiles -BaseType AbstractInstruction
    $methodClassIndex = Get-CSharpClassIndex -ProjectRoot $resolvedRoot -Paths $methodFiles -BaseType FunctionMethod

    $sourcePaths = @($instructionFiles + $methodFiles)
    $sourceIdentities = New-Object System.Collections.Generic.List[object]
    foreach ($path in $sourcePaths) {
        $bytes = [IO.File]::ReadAllBytes($path)
        $sourceIdentities.Add([pscustomobject][ordered]@{ sourceFile=(Get-SignatureRelativePath $resolvedRoot $path); sha256=(Get-SignatureSha256Hex $bytes); bytes=$bytes.LongLength })
    }
    $sortedSources = Sort-SignatureOrdinal -Items $sourceIdentities.ToArray() -KeySelector { param($item) $item.sourceFile }

    $instructions = New-Object System.Collections.Generic.List[object]
    foreach ($registration in @($Inventory.instructionRegistrations)) {
        $sourcePath = Join-Path $resolvedRoot ([string]$registration.sourceFile).Replace('/', '\')
        $lineText = ''
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            $lines = [IO.File]::ReadAllLines($sourcePath, [Text.Encoding]::UTF8)
            if ([int]$registration.sourceLine -ge 1 -and [int]$registration.sourceLine -le $lines.Count) { $lineText = $lines[[int]$registration.sourceLine - 1].Trim() }
        }
        $callMatch = [regex]::Match($lineText, '^(?<api>addFunction|addPrintFunction|addPrintDataFunction)\s*\((?<args>.*)\)\s*;')
        $registrationStatus = if ($callMatch.Success) { 'Resolved' } else { 'Unresolved' }
        $api = if ($callMatch.Success) { $callMatch.Groups['api'].Value } else { '' }
        $args = if ($callMatch.Success) { @(Split-CSharpCallArguments $callMatch.Groups['args'].Value) } else { @() }
        $bindingKind = 'Unknown'; $handlerType = ''; $argumentCandidates = @(); $registrationFlag = ''
        if ($api -eq 'addPrintFunction') { $bindingKind='GeneratedInstructionHandler'; $handlerType='PRINT_Instruction'; $registrationFlag='0' }
        elseif ($api -eq 'addPrintDataFunction') { $bindingKind='GeneratedInstructionHandler'; $handlerType='PRINT_DATA_Instruction'; $registrationFlag='0' }
        elseif ($api -eq 'addFunction' -and $args.Count -ge 2) {
            $binder = $args[1]
            $registrationFlag = if ($args.Count -ge 3) { $args[2] } else { '0' }
            if ($binder -match '^argb\[(?<arg>FunctionArgType\.[A-Za-z0-9_]+)\]$') { $bindingKind='ArgumentBuilderTable'; $argumentCandidates=@($Matches['arg']) }
            elseif ($binder -match '^ArgumentParser\.GetArgumentBuilder\((?<arg>FunctionArgType\.[A-Za-z0-9_]+)\)$') { $bindingKind='ArgumentBuilderFactory'; $argumentCandidates=@($Matches['arg']) }
            elseif ($binder -match '^new\s+(?<handler>[A-Za-z_][A-Za-z0-9_]*)') { $bindingKind='InstructionHandler'; $handlerType=$Matches['handler'] }
        }
        $classResolution = Get-ClassResolution -Index $instructionClassIndex -ClassName $handlerType
        $blockText = if ($classResolution.status -eq 'Resolved') { [string]$classResolution.entry.blockText } else { '' }
        if ($bindingKind -in @('InstructionHandler','GeneratedInstructionHandler') -and $classResolution.status -eq 'Resolved') {
            $argumentCandidates = @(Get-UniqueEvidenceCandidates $blockText '\bArgBuilder\s*=(?!=)\s*(?<expr>[^;]+);' | ForEach-Object {
                if ($_ -match '^ArgumentParser\.GetArgumentBuilder\((?<arg>FunctionArgType\.[A-Za-z0-9_]+)\)$') { $Matches['arg'] } else { $_ }
            })
        }
        $argumentStatus = Get-EvidenceStatus $argumentCandidates
        $handlerFlags = if ($classResolution.status -eq 'Resolved') { @(Get-UniqueEvidenceCandidates $blockText '\bflag\s*(?:\|=|=(?!=))\s*(?<expr>[^;]+);') } else { @() }
        $completion = 'Unresolved'
        if ($classResolution.status -eq 'Resolved') {
            if ($blockText -match 'WaitInput\s*\(|ReadAnyKey\s*\(') { $completion='InputWaitCandidate' }
            elseif ($blockText -match '\b(?:Thread\.)?Sleep\s*\(|\bAWAIT\b') { $completion='TimedYieldCandidate' }
            elseif ($blockText -match '\b(?:IntoFunction|JumpTo|ReturnF?|SetBegin)\s*\(') { $completion='ControlFlowCandidate' }
            else { $completion='SynchronousCandidate' }
        }
        $instructions.Add([pscustomobject][ordered]@{
            publicKey=[string]$registration.publicKey; targetModule=[string]$registration.targetModule
            registrationSourceStatus=$registrationStatus; registrationApi=$api; registrationExpression=$lineText
            bindingKind=$bindingKind; handlerType=$handlerType; handlerSourceStatus=$classResolution.status
            handlerSourceFile=if($classResolution.status -eq 'Resolved'){$classResolution.entry.sourceFile}else{''}
            handlerSourceLine=if($classResolution.status -eq 'Resolved'){$classResolution.entry.sourceLine}else{0}
            argumentSchemaStatus=$argumentStatus; argumentSchemaCandidates=@($argumentCandidates)
            registrationFlagExpression=$registrationFlag; handlerFlagCandidates=@($handlerFlags)
            completionModeCandidate=$completion; effectCandidates=@(Get-EffectCandidates $blockText)
            evidenceStatus='StaticCandidate'; behaviorFixtureStatus='Uncovered'
        })
    }
    $sortedInstructions = Sort-SignatureOrdinal -Items $instructions.ToArray() -KeySelector { param($item) $item.publicKey }

    $functions = New-Object System.Collections.Generic.List[object]
    foreach ($registration in @($Inventory.expressionRegistrations)) {
        $resolution = Get-ClassResolution -Index $methodClassIndex -ClassName ([string]$registration.handler)
        $blockText = if ($resolution.status -eq 'Resolved') { [string]$resolution.entry.blockText } else { '' }
        $returns = if ($resolution.status -eq 'Resolved') { @(Get-UniqueEvidenceCandidates $blockText '\bReturnType\s*=(?!=)\s*(?<expr>[^;]+);') } else { @() }
        $arguments = if ($resolution.status -eq 'Resolved') { @(Get-UniqueEvidenceCandidates $blockText '\bargumentTypeArray\s*=(?!=)\s*(?<expr>[^;]+);') } else { @() }
        $returnStatus = Get-EvidenceStatus $returns; $argumentStatus = Get-EvidenceStatus $arguments
        $signatureStatus = if ($returnStatus -eq 'Resolved' -and $argumentStatus -eq 'Resolved') { 'CompleteStatic' } elseif ($returnStatus -eq 'Conditional' -or $argumentStatus -eq 'Conditional') { 'ConditionalStatic' } else { 'Unresolved' }
        $functions.Add([pscustomobject][ordered]@{
            publicKey=[string]$registration.publicKey; targetModule=[string]$registration.targetModule; handlerType=[string]$registration.handler
            handlerSourceStatus=$resolution.status; handlerSourceFile=if($resolution.status -eq 'Resolved'){$resolution.entry.sourceFile}else{''}
            handlerSourceLine=if($resolution.status -eq 'Resolved'){$resolution.entry.sourceLine}else{0}
            returnTypeStatus=$returnStatus; returnTypeCandidates=@($returns)
            argumentSchemaStatus=$argumentStatus; argumentSchemaCandidates=@($arguments)
            signatureStatus=$signatureStatus
            overridesArgumentCheck=($blockText -match '\boverride\s+string\s+CheckArgumentType\s*\(')
            restructureCandidates=if($resolution.status -eq 'Resolved'){@(Get-UniqueEvidenceCandidates $blockText '\bCanRestructure\s*=(?!=)\s*(?<expr>[^;]+);')}else{@()}
            completionModeCandidate=if($resolution.status -eq 'Resolved'){'SynchronousBodyCandidate'}else{'Unresolved'}
            effectCandidates=@(Get-EffectCandidates $blockText); evidenceStatus='StaticCandidate'; behaviorFixtureStatus='Uncovered'
        })
    }
    $sortedFunctions = Sort-SignatureOrdinal -Items $functions.ToArray() -KeySelector { param($item) $item.publicKey }

    $coverage = [ordered]@{
        instructionRegistrationSourceResolvedCount=@($sortedInstructions | Where-Object registrationSourceStatus -eq 'Resolved').Count
        instructionBindingUnknownCount=@($sortedInstructions | Where-Object bindingKind -eq 'Unknown').Count
        instructionArgumentResolvedCount=@($sortedInstructions | Where-Object argumentSchemaStatus -eq 'Resolved').Count
        instructionArgumentConditionalCount=@($sortedInstructions | Where-Object argumentSchemaStatus -eq 'Conditional').Count
        instructionArgumentUnresolvedCount=@($sortedInstructions | Where-Object argumentSchemaStatus -eq 'Unresolved').Count
        instructionInputWaitCandidateCount=@($sortedInstructions | Where-Object completionModeCandidate -eq 'InputWaitCandidate').Count
        expressionHandlerSourceResolvedCount=@($sortedFunctions | Where-Object handlerSourceStatus -eq 'Resolved').Count
        expressionReturnResolvedCount=@($sortedFunctions | Where-Object returnTypeStatus -eq 'Resolved').Count
        expressionReturnConditionalCount=@($sortedFunctions | Where-Object returnTypeStatus -eq 'Conditional').Count
        expressionReturnUnresolvedCount=@($sortedFunctions | Where-Object returnTypeStatus -eq 'Unresolved').Count
        expressionArgumentResolvedCount=@($sortedFunctions | Where-Object argumentSchemaStatus -eq 'Resolved').Count
        expressionArgumentConditionalCount=@($sortedFunctions | Where-Object argumentSchemaStatus -eq 'Conditional').Count
        expressionArgumentUnresolvedCount=@($sortedFunctions | Where-Object argumentSchemaStatus -eq 'Unresolved').Count
        expressionCompleteStaticCount=@($sortedFunctions | Where-Object signatureStatus -eq 'CompleteStatic').Count
    }
    $canonicalPayload = [ordered]@{ schemaVersion='1.0.0'; workPackage='M0-DIA-03'; sourceInventoryHash=[string]$Inventory.canonicalHash; sourceFiles=@($sortedSources); instructions=@($sortedInstructions); expressionFunctions=@($sortedFunctions) }
    $canonicalJson = $canonicalPayload | ConvertTo-Json -Depth 30 -Compress
    $descriptorHash = Get-SignatureSha256Hex $script:SignatureUtf8NoBom.GetBytes($canonicalJson)
    $report = [ordered]@{
        schemaVersion='1.0.0'; workPackage='M0-DIA-03'; generatedAtUtc=[DateTime]::UtcNow.ToString('o',[Globalization.CultureInfo]::InvariantCulture)
        executionStatus='InProgress'; gateStatus='Blocked'; blockerCode='EvidenceMissing'; result='Partial'
        sourceInventoryHash=[string]$Inventory.canonicalHash; descriptorSetHash=$descriptorHash
        instructionCount=@($sortedInstructions).Count; expressionFunctionCount=@($sortedFunctions).Count; coverage=$coverage
        uncovered=@(
            'Static assignment and body scans do not evaluate constructor arguments, branches, virtual dispatch, aliases, or runtime configuration.',
            'Completion modes and effect categories are candidates only until two-sided behavior fixtures and trace ordering pass.',
            'Effective name comparer, formal alias/replacement declarations, nullability/default rules, and stable module ownership remain Uncovered.',
            'This report is not consumed by the legacy Parser/VM and does not change M0-DIA-02 snapshot hashes.',
            'D1/D2 runtime, upstream/target/APK/device evidence, and gate signatures remain Uncovered.'
        )
        sourceFiles=@($sortedSources); instructions=@($sortedInstructions); expressionFunctions=@($sortedFunctions)
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutput=[IO.Path]::GetFullPath($OutputPath); [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null
        [IO.File]::WriteAllText($resolvedOutput,(($report | ConvertTo-Json -Depth 30)+"`n"),$script:SignatureUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectSignatureInventory
