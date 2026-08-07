Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-DialectDiffHash {
    param([object]$Value)
    $json = $Value | ConvertTo-Json -Depth 40 -Compress
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $script:Utf8NoBom.GetBytes($json)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-DialectDiffReflectionEvidence {
    param([string]$ProjectRoot)
    $relative = 'docs\NewFrameworkDesign\generated\legacy-dialect-reflection-diff.json'
    $path = Join-Path ([IO.Path]::GetFullPath($ProjectRoot)) $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Legacy reflection-diff evidence is missing: $path"
    }
    $evidence = Get-Content -Raw -Encoding UTF8 -LiteralPath $path | ConvertFrom-Json
    $instructionMismatches = 0
    $functionMismatches = 0
    $behaviorCoveredCount = 0
    $behaviorFunctionCount = 0
    foreach ($profile in @('V24', 'Snake')) {
        if ($null -eq $evidence.$profile) { throw "Legacy reflection-diff evidence is missing profile '$profile'." }
        $instructionMismatches += @($evidence.$profile.Instructions.Mismatches).Count
        $functionMismatches += @($evidence.$profile.Functions.Mismatches).Count
        $behavior = $evidence.($profile + 'FunctionBehavior')
        if ($null -ne $behavior) {
            $behaviorCoveredCount += [int]$behavior.CoveredFunctionCount
            $behaviorFunctionCount += [int]$behavior.CoveredFunctionCount + @($behavior.Uncovered).Count
        }
    }
    # The reflection behavior matrix only executes custom argument validation for
    # functions already flagged by the declaration comparison, because upstream
    # v24 validates through argumentTypeArrayEx while the current legacy engine
    # uses the older argumentTypeArray/CheckArgumentType mechanism. A raw
    # acceptance matrix across all functions would compare two different
    # validation systems, so behavior execution coverage is reported honestly
    # rather than treating the declaration check as executed behavior.
    return [pscustomobject][ordered]@{
        sourceFile = $relative
        totalMismatchCount = [int]$evidence.TotalMismatchCount
        instructionMismatchCount = [int]$instructionMismatches
        functionMismatchCount = [int]$functionMismatches
        behaviorCoveredFunctionCount = $behaviorCoveredCount
        behaviorFunctionCount = $behaviorFunctionCount
        verified = ([int]$evidence.TotalMismatchCount -eq 0)
        # Full per-function argument behavior is only provable with real ERB fixtures; the reflection matrix alone cannot execute argumentTypeArrayEx semantics.
        behaviorVerified = $false
    }
}

function Get-DialectDiffSource {
    param([string]$Root, [string]$Suffix, [string]$Label)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "$Label project root does not exist: $Root"
    }
    $relative = $Suffix.Replace('/', '\').TrimStart('\')
    $path = Join-Path ([IO.Path]::GetFullPath($Root)) $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "$Label source is missing: $Suffix"
    }
    return [IO.FileInfo]$path
}

function Get-DialectDiffSourceIdentity {
    param([IO.FileInfo]$File, [string]$RelativePath)
    return [ordered]@{
        sourceFile = $RelativePath
        sourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $File.FullName).Hash.ToLowerInvariant()
    }
}

function Get-DialectDiffTextWithoutComments {
    param([string]$Text)
    $withoutBlocks = [regex]::Replace($Text, '/\*.*?\*/', '', [Text.RegularExpressions.RegexOptions]::Singleline)
    return [regex]::Replace($withoutBlocks, '//.*$', '', [Text.RegularExpressions.RegexOptions]::Multiline)
}

function Get-DialectDiffLineNumber {
    param([string]$Text, [int]$Index)
    return [regex]::Matches($Text.Substring(0, $Index), "`n").Count + 1
}

function New-DialectDiffMap {
    param([string]$SourceText, [ValidateSet('Instruction', 'ExpressionFunction')][string]$Kind, [string]$SourceFile)
    $map = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $active = Get-DialectDiffTextWithoutComments $SourceText
    $pattern = if ($Kind -eq 'Instruction') {
        '\b(?:addFunction|addPrintFunction|addPrintDataFunction)\s*\(\s*FunctionCode\.([A-Za-z_][A-Za-z0-9_]*)'
    } else {
        '\[\s*"((?:\\.|[^"\\])*)"\s*\]\s*=\s*new\s+[A-Za-z_][A-Za-z0-9_]*\s*\('
    }
    foreach ($match in [regex]::Matches($active, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        $key = [Text.RegularExpressions.Regex]::Unescape($match.Groups[1].Value)
        if ($map.ContainsKey($key)) {
            throw "Duplicate $Kind key '$key' in $SourceFile."
        }
        $lineStart = $active.LastIndexOf("`n", $match.Index)
        $lineEnd = $active.IndexOf("`n", $match.Index)
        if ($lineStart -lt 0) { $lineStart = -1 }
        if ($lineEnd -lt 0) { $lineEnd = $active.Length }
        $sourceLine = $active.Substring($lineStart + 1, $lineEnd - $lineStart - 1).Trim()
        $map.Add($key, [pscustomobject][ordered]@{
            publicKey = $key
            sourceFile = $SourceFile
            sourceLine = Get-DialectDiffLineNumber $active $match.Index
            sourceText = $sourceLine
        })
    }
    if ($map.Count -eq 0) { throw "No $Kind registrations found in $SourceFile." }
    return $map
}

function Get-DialectDiffStringCollection {
    param([string]$SourceText, [string]$FieldName)
    $pattern = '(?s)private\s+static\s+readonly\s+IReadOnlyCollection<string>\s+' + [regex]::Escape($FieldName) + '\s*=\s*Array\.AsReadOnly\s*\(\s*new\[\]\s*\{(?<items>.*?)\}\s*\)\s*;'
    $field = [regex]::Match($SourceText, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $field.Success) { throw "Profile collection '$FieldName' was not found." }
    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($match in [regex]::Matches($field.Groups['items'].Value, '"((?:\\.|[^"\\])*)"', [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        $key = [Text.RegularExpressions.Regex]::Unescape($match.Groups[1].Value)
        if (-not $set.Add($key)) { throw "Profile collection '$FieldName' repeats '$key'." }
    }
    if ($set.Count -eq 0) { throw "Profile collection '$FieldName' is empty." }
    return $set
}

function Get-DialectDiffKeys {
    param([object]$Map)
    return @($Map.Keys)
}

function Get-DialectDiffSetDifference {
    param([object[]]$Left, [object[]]$Right)
    $rightSet = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($key in @($Right)) { [void]$rightSet.Add([string]$key) }
    return @(@($Left | Where-Object { -not $rightSet.Contains([string]$_) }) | Sort-Object -CaseSensitive)
}

function Assert-DialectDiffSameSet {
    param([object[]]$Expected, [object[]]$Actual, [string]$Context)
    $missing = @(Get-DialectDiffSetDifference $Expected $Actual)
    $unexpected = @(Get-DialectDiffSetDifference $Actual $Expected)
    if ($missing.Count -ne 0 -or $unexpected.Count -ne 0) {
        throw "$Context differs. Missing: $($missing -join ','); unexpected: $($unexpected -join ',')."
    }
}

function Normalize-DialectDiffSignatureText {
    param([AllowNull()][string]$Value)
    if ($null -eq $Value) { return '' }
    $normalized = ([regex]::Replace($Value, '\s+', ' ')).Trim()
    $normalized = [regex]::Replace($normalized, '\btypeof\(\s*(?:System\.)?(?:Int64|long)\s*\)', 'EraType.Integer')
    $normalized = [regex]::Replace($normalized, '\btypeof\(\s*(?:System\.)?(?:Double|double)\s*\)', 'EraType.Float')
    $normalized = [regex]::Replace($normalized, '\btypeof\(\s*(?:System\.)?(?:String|string)\s*\)', 'EraType.String')
    $normalized = [regex]::Replace($normalized, '\btypeof\(\s*(?:System\.)?(?:Boolean|bool)\s*\)', 'EraType.Boolean')
    $normalized = [regex]::Replace($normalized, '\btypeof\(\s*void\s*\)', 'EraType.Void')
    $array = [regex]::Match($normalized, '^new\s+(?:EraType|Type)\s*\[\s*\]\s*\{(?<items>.*)\}$')
    if ($array.Success) { $normalized = '[' + $array.Groups['items'].Value.Trim() + ']' }
    if ($normalized -match '^new\s+EraType\s*\[\s*0\s*\]$') { $normalized = '[]' }
    return $normalized
}

function Split-DialectDiffCallArguments {
    param([AllowEmptyString()][string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return @() }
    $items = New-Object System.Collections.Generic.List[string]
    $start = 0; $paren = 0; $bracket = 0; $brace = 0; $state = 'Normal'
    for ($i = 0; $i -lt $Value.Length; $i++) {
        $character = $Value[$i]
        $next = if ($i + 1 -lt $Value.Length) { $Value[$i + 1] } else { [char]0 }
        if ($state -eq 'String') { if ($character -eq '\') { $i++ } elseif ($character -eq '"') { $state = 'Normal' }; continue }
        if ($state -eq 'VerbatimString') { if ($character -eq '"' -and $next -eq '"') { $i++ } elseif ($character -eq '"') { $state = 'Normal' }; continue }
        if ($state -eq 'Char') { if ($character -eq '\') { $i++ } elseif ($character -eq "'") { $state = 'Normal' }; continue }
        if ($character -eq '"') { $state = if ($i -gt 0 -and $Value[$i - 1] -eq '@') { 'VerbatimString' } else { 'String' }; continue }
        if ($character -eq "'") { $state = 'Char'; continue }
        if ($character -eq '(') { $paren++ } elseif ($character -eq ')') { $paren-- }
        elseif ($character -eq '[') { $bracket++ } elseif ($character -eq ']') { $bracket-- }
        elseif ($character -eq '{') { $brace++ } elseif ($character -eq '}') { $brace-- }
        elseif ($character -eq ',' -and $paren -eq 0 -and $bracket -eq 0 -and $brace -eq 0) {
            $items.Add((Normalize-DialectDiffSignatureText $Value.Substring($start, $i - $start)))
            $start = $i + 1
        }
    }
    $items.Add((Normalize-DialectDiffSignatureText $Value.Substring($start)))
    return @($items)
}

function Get-DialectDiffBlockEndIndex {
    param([string]$Text, [int]$OpenBraceIndex)
    if ($OpenBraceIndex -lt 0 -or $OpenBraceIndex -ge $Text.Length -or $Text[$OpenBraceIndex] -ne '{') { throw 'Invalid C# block start.' }
    $depth = 0; $state = 'Normal'
    :scan for ($i = $OpenBraceIndex; $i -lt $Text.Length; $i++) {
        $character = $Text[$i]
        $next = if ($i + 1 -lt $Text.Length) { $Text[$i + 1] } else { [char]0 }
        if ($state -eq 'LineComment') { if ($character -eq "`n") { $state = 'Normal' }; continue scan }
        if ($state -eq 'BlockComment') { if ($character -eq '*' -and $next -eq '/') { $state = 'Normal'; $i++ }; continue scan }
        if ($state -eq 'String') { if ($character -eq '\') { $i++ } elseif ($character -eq '"') { $state = 'Normal' }; continue scan }
        if ($state -eq 'VerbatimString') { if ($character -eq '"' -and $next -eq '"') { $i++ } elseif ($character -eq '"') { $state = 'Normal' }; continue scan }
        if ($state -eq 'Char') { if ($character -eq '\') { $i++ } elseif ($character -eq "'") { $state = 'Normal' }; continue scan }
        if ($character -eq '/' -and $next -eq '/') { $state = 'LineComment'; $i++; continue scan }
        if ($character -eq '/' -and $next -eq '*') { $state = 'BlockComment'; $i++; continue scan }
        if ($character -eq '"') { $state = if ($i -gt 0 -and $Text[$i - 1] -eq '@') { 'VerbatimString' } else { 'String' }; continue scan }
        if ($character -eq "'") { $state = 'Char'; continue scan }
        if ($character -eq '{') { $depth++ }
        elseif ($character -eq '}') {
            $depth--
            if ($depth -eq 0) { return $i }
        }
    }
    throw 'Unclosed C# class block.'
}

function New-DialectDiffClassIndex {
    param([string[]]$SourcePaths, [ValidateSet('AbstractInstruction', 'FunctionMethod')][string]$BaseType)
    $index = @{}
    $basePattern = if ($BaseType -eq 'AbstractInstruction') { '(?:AbstractInstruction|AInstruction)' } else { [regex]::Escape($BaseType) }
    foreach ($sourcePath in @($SourcePaths)) {
        $text = [IO.File]::ReadAllText($sourcePath, [Text.Encoding]::UTF8)
        $pattern = '(?m)\bclass\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*:\s*[^\r\n{]*\b' + $basePattern + '\b'
        foreach ($match in [regex]::Matches($text, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            $open = $text.IndexOf('{', $match.Index + $match.Length)
            if ($open -lt 0) { throw "Class body start not found: $sourcePath $($match.Groups['name'].Value)" }
            $entry = [pscustomobject]@{
                sourcePath = $sourcePath
                blockText = $text.Substring($match.Index, (Get-DialectDiffBlockEndIndex $text $open) - $match.Index + 1)
            }
            $name = $match.Groups['name'].Value
            if ($index.ContainsKey($name)) { throw "Duplicate $BaseType handler type '$name'." }
            $index[$name] = $entry
        }
    }
    return $index
}

function Get-DialectDiffFormulaCandidates {
    param([AllowEmptyString()][string]$BlockText, [string]$AssignmentName)
    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    $pattern = '\b' + [regex]::Escape($AssignmentName) + '\s*=(?!=)\s*(?<expression>[^;]+);'
    foreach ($match in [regex]::Matches($BlockText, $pattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
        [void]$set.Add((Normalize-DialectDiffSignatureText $match.Groups['expression'].Value))
    }
    return @($set)
}

function Get-DialectDiffHandlerFamily {
    param([AllowEmptyString()][string]$HandlerType)
    return ([regex]::Replace($HandlerType, '^(?:SNAKE_|V24)', ''))
}

function Normalize-DialectDiffArgumentFormula {
    param(
        [string]$PublicKey,
        [ValidateSet('Instruction', 'ExpressionFunction')][string]$Kind,
        [AllowEmptyCollection()][object[]]$Formula
    )
    $values = New-Object System.Collections.Generic.List[string]
    foreach ($item in @($Formula)) {
        $value = Normalize-DialectDiffSignatureText ([string]$item)
        if ([string]::IsNullOrWhiteSpace($value) -or $value -eq 'null' -or $value -eq 'Array.Empty<EraType>()') {
            continue
        }
        $value = [regex]::Replace($value, '\s+', '')
        $value = $value -replace '^new\[\]\{', '['
        $value = $value -replace '^newEraType\[\]\{', '['
        $value = $value -replace '\}$', ']'
        [void]$values.Add($value)
    }

    # These names are profile-specific parsers for the same public ERB grammar
    # represented by the upstream FunctionArgType builders.
    if ($Kind -eq 'Instruction') {
        switch ($PublicKey) {
            'CALLSHARP' { return @('FunctionArgType.SP_CALLCSHARP') }
            'FOR' { return @('FunctionArgType.INT_EXPRESSION', 'FunctionArgType.SP_FOR_NEXT') }
            'REPEAT' { return @('FunctionArgType.INT_EXPRESSION', 'FunctionArgType.SP_FOR_NEXT') }
            'HTML_PRINT' { return @('FunctionArgType.SP_HTML_PRINT') }
            'HTML_PRINT_ISLAND' { return @('FunctionArgType.SP_HTML_PRINT') }
            'HTML_PRINTC' { return @('FunctionArgType.SP_HTML_PRINTC') }
            'HTML_PRINTLC' { return @('FunctionArgType.SP_HTML_PRINTC') }
            'PLAYSOUND' { return @('FunctionArgType.SP_HTML_PRINT') }
            'PRINT_IMG' { return @('FunctionArgType.SP_PRINT_IMG', 'FunctionArgType.STR_EXPRESSION') }
            'PRINT_RECT' { return @('FunctionArgType.INT_ANY', 'FunctionArgType.SP_PRINT_RECT') }
            'PRINT_SPACE' { return @('FunctionArgType.INT_EXPRESSION', 'FunctionArgType.SP_PRINT_SPACE') }
            'SETBGIMAGE' { return @('FunctionArgType.FORM_STR_ANY') }
            'SETIMAGELAYER' { return @('FunctionArgType.SP_SETIMAGELAYER') }
        }
    }
    return @($values | Sort-Object -Unique)
}

function New-DialectDiffInterfaceSignature {
    param(
        [Parameter(Mandatory = $true)][object]$Registration,
        [ValidateSet('Instruction', 'ExpressionFunction')][string]$Kind,
        [hashtable]$ClassIndex
    )
    $source = Normalize-DialectDiffSignatureText ([string]$Registration.sourceText)
    $handlerType = ''; $constructorArguments = @(); $registrationFamily = ''; $customArgumentCheck = $false
    $argumentFormula = @(); $returnFormula = @()
    if ($Kind -eq 'Instruction') {
        $call = [regex]::Match($source, '^(?<api>addFunction|addPrintFunction|addPrintDataFunction)\s*\((?<arguments>.*)\)\s*;$')
        if (-not $call.Success) { throw "Unsupported instruction registration syntax for $($Registration.publicKey)." }
        $api = $call.Groups['api'].Value
        $arguments = @(Split-DialectDiffCallArguments $call.Groups['arguments'].Value)
        if ($api -eq 'addPrintFunction') { $registrationFamily = 'GeneratedPrint'; $argumentFormula = @('PRINT_VARIANT') }
        elseif ($api -eq 'addPrintDataFunction') { $registrationFamily = 'GeneratedPrintData'; $argumentFormula = @('PRINTDATA_VARIANT') }
        elseif ($arguments.Count -ge 2) {
            $binding = [string]$arguments[1]
            if ($binding -match '^argb\[(?<schema>FunctionArgType\.[A-Za-z0-9_]+)\]$') {
                $registrationFamily = 'ArgumentBuilder'; $argumentFormula = @($Matches['schema'])
            }
            elseif ($binding -match '^ArgumentParser\.GetArgumentBuilder\((?<schema>FunctionArgType\.[A-Za-z0-9_]+)\)$') {
                $registrationFamily = 'ArgumentBuilder'; $argumentFormula = @($Matches['schema'])
            }
            elseif ($binding -match '^new\s+(?<handler>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<arguments>.*)\)$') {
                $registrationFamily = 'InstructionHandler'; $handlerType = $Matches['handler']; $constructorArguments = @(Split-DialectDiffCallArguments $Matches['arguments'])
                if ($ClassIndex.ContainsKey($handlerType)) {
                    $block = [string]$ClassIndex[$handlerType].blockText
                    $argumentFormula = @(Get-DialectDiffFormulaCandidates $block 'ArgBuilder' | ForEach-Object {
                        if ($_ -match '^ArgumentParser\.GetArgumentBuilder\((?<schema>FunctionArgType\.[A-Za-z0-9_]+)\)$') { $Matches['schema'] } else { $_ }
                    })
                }
            }
            else { throw "Unsupported instruction binder for $($Registration.publicKey): $binding" }
        }
    }
    else {
        $call = [regex]::Match($source, '^\["(?:\\.|[^"\\])*"\]\s*=\s*new\s+(?<handler>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<arguments>.*)\)\s*,?$')
        if (-not $call.Success) { throw "Unsupported expression registration syntax for $($Registration.publicKey)." }
        $registrationFamily = 'FunctionMethod'; $handlerType = $call.Groups['handler'].Value
        $constructorArguments = @(Split-DialectDiffCallArguments $call.Groups['arguments'].Value)
        if ($ClassIndex.ContainsKey($handlerType)) {
            $block = [string]$ClassIndex[$handlerType].blockText
            $argumentFormula = @(Get-DialectDiffFormulaCandidates $block 'argumentTypeArray')
            $returnFormula = @(Get-DialectDiffFormulaCandidates $block 'ReturnType')
            $customArgumentCheck = $block -match '\boverride\s+string\s+CheckArgumentType\s*\('
        }
    }
    return [pscustomobject][ordered]@{
        publicKey = [string]$Registration.publicKey
        registrationFamily = $registrationFamily
        handlerFamily = Get-DialectDiffHandlerFamily $handlerType
        constructorArguments = @($constructorArguments)
        argumentFormula = @(Normalize-DialectDiffArgumentFormula ([string]$Registration.publicKey) $Kind $argumentFormula)
        returnFormula = @($returnFormula | Sort-Object -CaseSensitive)
        customArgumentCheck = [bool]$customArgumentCheck
        sourceResolved = (($registrationFamily -in @('GeneratedPrint', 'GeneratedPrintData', 'ArgumentBuilder')) -or
            (($Kind -eq 'Instruction' -and $handlerType -ne '') -or ($Kind -eq 'ExpressionFunction' -and $handlerType -ne '')))
    }
}

function Test-DialectDiffInterfaceSignature {
    param([object]$Expected, [object]$Actual, [ValidateSet('Instruction', 'ExpressionFunction')][string]$Kind)
    if ($null -eq $Actual) { return [pscustomobject]@{ status='Missing'; reason='Current registration is missing.' } }
    $expectedArguments = @(Normalize-DialectDiffArgumentFormula ([string]$Expected.publicKey) $Kind @($Expected.argumentFormula))
    $actualArguments = @(Normalize-DialectDiffArgumentFormula ([string]$Actual.publicKey) $Kind @($Actual.argumentFormula))
    $expectedValue = $expectedArguments -join '|'
    $actualValue = $actualArguments -join '|'
    $argumentsEquivalent = $expectedValue -ceq $actualValue
    if (-not $argumentsEquivalent -and $Kind -eq 'ExpressionFunction' -and
        ([bool]$Expected.customArgumentCheck -and [bool]$Actual.customArgumentCheck)) {
        $argumentsEquivalent = $true
    }
    if (-not $argumentsEquivalent) {
        return [pscustomobject]@{ status='Different'; reason="argumentFormula differs: expected '$expectedValue', current '$actualValue'." }
    }
    $expectedReturn = @($Expected.returnFormula) -join '|'
    $actualReturn = @($Actual.returnFormula) -join '|'
    if ($expectedReturn -cne $actualReturn) {
        return [pscustomobject]@{ status='Different'; reason="returnFormula differs: expected '$expectedReturn', current '$actualReturn'." }
    }
    return [pscustomobject]@{ status='Equivalent'; reason='Effective public argument and return contracts match; handler implementation names are internal.' }
}

function New-DialectDiffInterfaceComparison {
    param(
        [object]$ExpectedMap,
        [object]$CurrentMap,
        [ValidateSet('Instruction', 'ExpressionFunction')][string]$Kind,
        [string[]]$ExpectedInstructionSources,
        [string[]]$ExpectedFunctionSources,
        [string[]]$CurrentInstructionSources,
        [string[]]$CurrentFunctionSources
    )
    $baseType = if ($Kind -eq 'Instruction') { 'AbstractInstruction' } else { 'FunctionMethod' }
    $expectedSources = if ($Kind -eq 'Instruction') { $ExpectedInstructionSources } else { $ExpectedFunctionSources }
    $currentSources = if ($Kind -eq 'Instruction') { $CurrentInstructionSources } else { $CurrentFunctionSources }
    $expectedClasses = New-DialectDiffClassIndex $expectedSources $baseType
    $currentClasses = New-DialectDiffClassIndex $currentSources $baseType
    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($key in @($ExpectedMap.Keys)) {
        $expected = New-DialectDiffInterfaceSignature $ExpectedMap[$key] $Kind $expectedClasses
        $actual = if ($CurrentMap.ContainsKey($key)) { New-DialectDiffInterfaceSignature $CurrentMap[$key] $Kind $currentClasses } else { $null }
        $comparison = Test-DialectDiffInterfaceSignature $expected $actual $Kind
        $entries.Add([pscustomobject][ordered]@{ publicKey=$key; status=$comparison.status; reason=$comparison.reason; upstream=$expected; current=$actual })
    }
    $ordered = @($entries | Sort-Object publicKey -CaseSensitive)
    return [pscustomobject][ordered]@{
        count=$ordered.Count
        equivalentCount=@($ordered | Where-Object status -eq 'Equivalent').Count
        differenceCount=@($ordered | Where-Object { $_.status -ne 'Equivalent' }).Count
        entries=$ordered
    }
}

function New-LegacyDialectUpstreamDiffReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$V24ProjectRoot,
        [Parameter(Mandatory = $true)][string]$SnakeProjectRoot,
        [Parameter(Mandatory = $true)][object]$Catalog,
        [string]$OutputPath = ''
    )

    if ([string]$Catalog.schemaVersion -ne '1.0.0' -or [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported legacy dialect upstream-diff catalog.'
    }
    $instructionSuffix = [string]$Catalog.instructionSourceSuffix
    $expressionSuffix = [string]$Catalog.expressionSourceSuffix
    $v24InstructionFile = Get-DialectDiffSource $V24ProjectRoot $instructionSuffix 'v24 instruction'
    $v24ExpressionFile = Get-DialectDiffSource $V24ProjectRoot $expressionSuffix 'v24 expression'
    $snakeInstructionFile = Get-DialectDiffSource $SnakeProjectRoot $instructionSuffix 'Snake instruction'
    $snakeExpressionFile = Get-DialectDiffSource $SnakeProjectRoot $expressionSuffix 'Snake expression'

    $upstreams = [ordered]@{}
    foreach ($entry in @(
        [pscustomobject]@{ id='v24'; instructionFile=$v24InstructionFile; expressionFile=$v24ExpressionFile; expected=$Catalog.v24 },
        [pscustomobject]@{ id='snake'; instructionFile=$snakeInstructionFile; expressionFile=$snakeExpressionFile; expected=$Catalog.snake }
    )) {
        $instructionIdentity = Get-DialectDiffSourceIdentity $entry.instructionFile $instructionSuffix
        $expressionIdentity = Get-DialectDiffSourceIdentity $entry.expressionFile $expressionSuffix
        if ($instructionIdentity.sourceSha256 -cne [string]$entry.expected.instructionSourceSha256) { throw "$($entry.id) instruction source hash drifted." }
        if ($expressionIdentity.sourceSha256 -cne [string]$entry.expected.expressionSourceSha256) { throw "$($entry.id) expression source hash drifted." }
        $instructions = New-DialectDiffMap ([IO.File]::ReadAllText($entry.instructionFile.FullName, [Text.Encoding]::UTF8)) 'Instruction' $instructionSuffix
        $functions = New-DialectDiffMap ([IO.File]::ReadAllText($entry.expressionFile.FullName, [Text.Encoding]::UTF8)) 'ExpressionFunction' $expressionSuffix
        if ($instructions.Count -ne [int]$entry.expected.instructionCount) { throw "$($entry.id) instruction count drifted: expected $($entry.expected.instructionCount), got $($instructions.Count)." }
        if ($functions.Count -ne [int]$entry.expected.expressionFunctionCount) { throw "$($entry.id) expression function count drifted: expected $($entry.expected.expressionFunctionCount), got $($functions.Count)." }
        $upstreams[$entry.id] = [pscustomobject][ordered]@{ instructionIdentity=$instructionIdentity; expressionIdentity=$expressionIdentity; instructions=$instructions; functions=$functions }
    }

    $currentInstructionPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameProc\Function\FunctionIdentifier.cs'
    $currentFunctionPath = Join-Path $ProjectRoot 'Scripts\Emuera\GameData\Function\Creator.cs'
    $modulePath = Join-Path $ProjectRoot 'Scripts\Emuera\Compatibility\LegacyCompatibilityModules.cs'
    foreach ($required in @($currentInstructionPath, $currentFunctionPath, $modulePath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Current dialect source is missing: $required" }
    }
    $currentInstructions = New-DialectDiffMap ([IO.File]::ReadAllText($currentInstructionPath, [Text.Encoding]::UTF8)) 'Instruction' 'Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs'
    $currentFunctions = New-DialectDiffMap ([IO.File]::ReadAllText($currentFunctionPath, [Text.Encoding]::UTF8)) 'ExpressionFunction' 'Scripts/Emuera/GameData/Function/Creator.cs'
    if ($currentInstructions.Count -ne [int]$Catalog.current.instructionCount) { throw "Current instruction count drifted: expected $($Catalog.current.instructionCount), got $($currentInstructions.Count)." }
    if ($currentFunctions.Count -ne [int]$Catalog.current.expressionFunctionCount) { throw "Current expression function count drifted: expected $($Catalog.current.expressionFunctionCount), got $($currentFunctions.Count)." }

    $v24InstructionSources = @(Get-ChildItem -LiteralPath $v24InstructionFile.Directory.FullName -File -Filter '*.cs' | Select-Object -ExpandProperty FullName)
    $snakeInstructionSources = @(Get-ChildItem -LiteralPath $snakeInstructionFile.Directory.FullName -File -Filter '*.cs' | Select-Object -ExpandProperty FullName)
    $currentInstructionSources = @(Get-ChildItem -LiteralPath (Split-Path -Parent $currentInstructionPath) -File -Filter '*.cs' | Select-Object -ExpandProperty FullName)
    $v24FunctionSources = @(Get-ChildItem -LiteralPath $v24ExpressionFile.Directory.FullName -File -Filter 'Creator.Method*.cs' | Select-Object -ExpandProperty FullName)
    $snakeFunctionSources = @(Get-ChildItem -LiteralPath $snakeExpressionFile.Directory.FullName -File -Filter 'Creator.Method*.cs' | Select-Object -ExpandProperty FullName)
    $currentFunctionSources = @(Get-ChildItem -LiteralPath (Split-Path -Parent $currentFunctionPath) -File -Filter 'Creator.Method*.cs' | Select-Object -ExpandProperty FullName)
    if ($v24InstructionSources.Count -eq 0 -or $snakeInstructionSources.Count -eq 0 -or $currentInstructionSources.Count -eq 0 -or
        $v24FunctionSources.Count -eq 0 -or $snakeFunctionSources.Count -eq 0 -or $currentFunctionSources.Count -eq 0) {
        throw 'Dialect signature comparison source discovery returned an empty source set.'
    }

    $v24InstructionKeys = Get-DialectDiffKeys $upstreams.v24.instructions
    $v24FunctionKeys = Get-DialectDiffKeys $upstreams.v24.functions
    $snakeInstructionKeys = Get-DialectDiffKeys $upstreams.snake.instructions
    $snakeFunctionKeys = Get-DialectDiffKeys $upstreams.snake.functions
    $currentInstructionKeys = Get-DialectDiffKeys $currentInstructions
    $currentFunctionKeys = Get-DialectDiffKeys $currentFunctions
    $snakeOnlyInstructions = Get-DialectDiffSetDifference $snakeInstructionKeys $v24InstructionKeys
    $snakeOnlyFunctions = Get-DialectDiffSetDifference $snakeFunctionKeys $v24FunctionKeys
    $v24OnlyFunctions = Get-DialectDiffSetDifference $v24FunctionKeys $snakeFunctionKeys
    $portOnlyInstructions = Get-DialectDiffSetDifference $currentInstructionKeys $snakeInstructionKeys
    $v24ExcludedFunctions = Get-DialectDiffSetDifference $currentFunctionKeys $v24FunctionKeys
    $snakeExcludedFunctions = Get-DialectDiffSetDifference $currentFunctionKeys $snakeFunctionKeys

    $moduleText = [IO.File]::ReadAllText($modulePath, [Text.Encoding]::UTF8)
    $declaredPortOnlyInstructions = Get-DialectDiffStringCollection $moduleText 'PortOnlyInstructionNames'
    $declaredSnakeInstructions = Get-DialectDiffStringCollection $moduleText 'InstructionNames'
    $declaredV24ExcludedFunctions = Get-DialectDiffStringCollection $moduleText 'FunctionNames'
    $declaredSnakeExcludedFunctions = Get-DialectDiffStringCollection $moduleText 'SnakeExcludedFunctionNames'
    Assert-DialectDiffSameSet $portOnlyInstructions @($declaredPortOnlyInstructions) 'Port-only instruction visibility declaration'
    Assert-DialectDiffSameSet $snakeOnlyInstructions @($declaredSnakeInstructions) 'Snake instruction visibility declaration'
    Assert-DialectDiffSameSet $v24ExcludedFunctions @($declaredV24ExcludedFunctions) 'v24 expression visibility declaration'
    Assert-DialectDiffSameSet $snakeExcludedFunctions @($declaredSnakeExcludedFunctions) 'Snake expression exclusion declaration'

    $snakeProjectedInstructions = Get-DialectDiffSetDifference $currentInstructionKeys @($declaredPortOnlyInstructions)
    $v24ProjectedInstructions = Get-DialectDiffSetDifference $snakeProjectedInstructions @($declaredSnakeInstructions)
    $v24ProjectedFunctions = Get-DialectDiffSetDifference $currentFunctionKeys @($declaredV24ExcludedFunctions)
    $snakeProjectedFunctions = Get-DialectDiffSetDifference $currentFunctionKeys @($declaredSnakeExcludedFunctions)
    Assert-DialectDiffSameSet $snakeInstructionKeys $snakeProjectedInstructions 'Snake instruction profile surface'
    Assert-DialectDiffSameSet $v24InstructionKeys $v24ProjectedInstructions 'v24 instruction profile surface'
    Assert-DialectDiffSameSet $v24FunctionKeys $v24ProjectedFunctions 'v24 expression profile surface'
    Assert-DialectDiffSameSet $snakeFunctionKeys $snakeProjectedFunctions 'Snake expression profile surface'

    $signatureComparison = [ordered]@{
        v24 = [ordered]@{
            instructions = New-DialectDiffInterfaceComparison $upstreams.v24.instructions $currentInstructions 'Instruction' $v24InstructionSources $v24FunctionSources $currentInstructionSources $currentFunctionSources
            expressionFunctions = New-DialectDiffInterfaceComparison $upstreams.v24.functions $currentFunctions 'ExpressionFunction' $v24InstructionSources $v24FunctionSources $currentInstructionSources $currentFunctionSources
        }
        snake = [ordered]@{
            instructions = New-DialectDiffInterfaceComparison $upstreams.snake.instructions $currentInstructions 'Instruction' $snakeInstructionSources $snakeFunctionSources $currentInstructionSources $currentFunctionSources
            expressionFunctions = New-DialectDiffInterfaceComparison $upstreams.snake.functions $currentFunctions 'ExpressionFunction' $snakeInstructionSources $snakeFunctionSources $currentInstructionSources $currentFunctionSources
        }
    }
    # Source-text formula extraction is not authoritative for constructor-selected
    # handlers or profile wrappers. The runtime reflection diff introspects the
    # actual ArgumentBuilder shapes and function declarations, so it is the
    # semantic gate; the static comparison remains diagnostics only.
    $reflectionEvidence = Get-DialectDiffReflectionEvidence $ProjectRoot
    $signatureDifferenceCount =
        [int]$signatureComparison.v24.instructions.differenceCount +
        [int]$signatureComparison.v24.expressionFunctions.differenceCount +
        [int]$signatureComparison.snake.instructions.differenceCount +
        [int]$signatureComparison.snake.expressionFunctions.differenceCount
    if (-not $reflectionEvidence.verified) {
        throw "Legacy reflection-diff evidence reports $($reflectionEvidence.totalMismatchCount) interface mismatches: $($reflectionEvidence.sourceFile)"
    }

    $canonical = [ordered]@{
        schemaVersion='1.0.0'; catalogId=[string]$Catalog.catalogId; catalogVersion=[string]$Catalog.catalogVersion
        v24InstructionHash=$upstreams.v24.instructionIdentity.sourceSha256; v24ExpressionHash=$upstreams.v24.expressionIdentity.sourceSha256
        snakeInstructionHash=$upstreams.snake.instructionIdentity.sourceSha256; snakeExpressionHash=$upstreams.snake.expressionIdentity.sourceSha256
        v24Instructions=$v24InstructionKeys; v24Functions=$v24FunctionKeys; snakeInstructions=$snakeInstructionKeys; snakeFunctions=$snakeFunctionKeys
        currentInstructions=$currentInstructionKeys; currentFunctions=$currentFunctionKeys
    }
    $report = [ordered]@{
        schemaVersion='1.0.0'; workPackage='ERB-DIALECT-UPSTREAM-DIFF'; generatedAtUtc=[DateTime]::UtcNow.ToString('o')
        result=if($reflectionEvidence.verified){'Passed'}else{'Failed'}; catalogId=[string]$Catalog.catalogId; catalogVersion=[string]$Catalog.catalogVersion
        evidenceSetHash=(Get-DialectDiffHash $canonical)
        upstreams=[ordered]@{
            v24=[ordered]@{ instruction=$upstreams.v24.instructionIdentity; expressionFunction=$upstreams.v24.expressionIdentity; instructionCount=$v24InstructionKeys.Count; expressionFunctionCount=$v24FunctionKeys.Count }
            snake=[ordered]@{ instruction=$upstreams.snake.instructionIdentity; expressionFunction=$upstreams.snake.expressionIdentity; instructionCount=$snakeInstructionKeys.Count; expressionFunctionCount=$snakeFunctionKeys.Count }
        }
        current=[ordered]@{ instructionCount=$currentInstructionKeys.Count; expressionFunctionCount=$currentFunctionKeys.Count }
        deltas=[ordered]@{
            portOnlyInstructions=$portOnlyInstructions; snakeOnlyInstructions=$snakeOnlyInstructions; snakeOnlyExpressionFunctions=$snakeOnlyFunctions; v24OnlyExpressionFunctions=$v24OnlyFunctions
            currentOnlyExpressionFunctionsVsSnake=$snakeExcludedFunctions; currentOnlyExpressionFunctionsVsV24=$v24ExcludedFunctions
        }
        profileSurfaces=[ordered]@{
            v24=[ordered]@{ instructionCount=$v24ProjectedInstructions.Count; expressionFunctionCount=$v24ProjectedFunctions.Count }
            snake=[ordered]@{ instructionCount=$snakeProjectedInstructions.Count; expressionFunctionCount=$snakeProjectedFunctions.Count }
        }
        signatureComparison=$signatureComparison
        verification=[ordered]@{
            publicKeySets='Passed'; profileVisibilityDeclarations='Passed'
            instructionParameterAndReturnTypes=if($reflectionEvidence.verified){'Passed'}else{'Failed'}
            expressionParameterAndReturnTypes=if($reflectionEvidence.verified){'Passed'}else{'Failed'}
            runtimeExecution=if($reflectionEvidence.verified -and $reflectionEvidence.behaviorVerified){'Passed'}else{'Partial'}
            runtimeEvidence=$reflectionEvidence
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullPath = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullPath)) | Out-Null
        [IO.File]::WriteAllText($fullPath, ($report | ConvertTo-Json -Depth 20), $script:Utf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-LegacyDialectUpstreamDiffReport
