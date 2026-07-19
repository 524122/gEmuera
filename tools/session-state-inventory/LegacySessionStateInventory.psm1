Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-SessionInventorySha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha256.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha256.Dispose() }
}

function Get-SessionInventoryRelativePath {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$Path)

    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $fullPath = [IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Source path escapes project root: $fullPath"
    }
    return $fullPath.Substring($rootPath.Length).Replace('\', '/')
}

function Sort-SessionInventoryOrdinal {
    param([object[]]$Items, [Parameter(Mandatory = $true)][scriptblock]$KeySelector)

    $ordered = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $index = 0
    foreach ($item in @($Items)) {
        $key = [string](& $KeySelector $item)
        $ordered.Add(($key + [char]0 + $index.ToString('D10', [Globalization.CultureInfo]::InvariantCulture)), $item)
        $index++
    }
    return @($ordered.Values)
}

function ConvertTo-SessionInventoryJson {
    param([Parameter(Mandatory = $true)][object]$Value, [switch]$Compress)

    if ($Compress) { return ($Value | ConvertTo-Json -Depth 100 -Compress) }
    return ($Value | ConvertTo-Json -Depth 100)
}

function Get-SessionInventoryClassBlock {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][AllowEmptyString()][string[]]$Lines,
        [Parameter(Mandatory = $true)][string]$ClassName,
        [Parameter(Mandatory = $true)][string]$SourceFile
    )

    $declarationPattern = '\b(?:public|internal|private)\s+static\s+class\s+' + [regex]::Escape($ClassName) + '\b'
    $declarations = New-Object System.Collections.Generic.List[int]
    for ($lineIndex = 0; $lineIndex -lt $Lines.Count; $lineIndex++) {
        if ([regex]::IsMatch($Lines[$lineIndex], $declarationPattern, [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
            $declarations.Add($lineIndex)
        }
    }
    if ($declarations.Count -ne 1) {
        throw "Expected exactly one static class $ClassName in $SourceFile; found $($declarations.Count)."
    }

    $startLine = $declarations[0]
    $depth = 0
    $opened = $false
    $openingLine = -1
    for ($lineIndex = $startLine; $lineIndex -lt $Lines.Count; $lineIndex++) {
        foreach ($character in $Lines[$lineIndex].ToCharArray()) {
            if ($character -eq '{') {
                if (-not $opened) { $openingLine = $lineIndex }
                $depth++; $opened = $true
            }
            elseif ($character -eq '}') { $depth-- }
        }
        if ($opened -and $depth -eq 0) {
            return [pscustomobject][ordered]@{
                declarationLine = $startLine
                bodyStartLine = $openingLine
                endLine = $lineIndex
            }
        }
        if ($opened -and $depth -lt 0) { break }
    }
    throw "Could not locate closing brace for static class $ClassName in $SourceFile."
}

function Get-SessionInventoryLineWithoutComment {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Line)

    $commentIndex = $Line.IndexOf('//', [StringComparison]::Ordinal)
    if ($commentIndex -ge 0) { return $Line.Substring(0, $commentIndex) }
    return $Line
}

function Get-SessionInventoryConditionalLabel {
    param([System.Collections.Generic.List[string]]$Stack)

    if ($null -eq $Stack -or $Stack.Count -eq 0) { return '' }
    return (@($Stack.ToArray()) -join ' && ')
}

function Get-SessionInventoryRootStateDeclarations {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$ClassName,
        [Parameter(Mandatory = $true)][ValidateSet('GlobalStatic', 'Program')][string]$Scope
    )

    $path = Join-Path $ProjectRoot ($RelativePath.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing source file: $RelativePath" }
    $lines = [IO.File]::ReadAllLines([IO.Path]::GetFullPath($path), [Text.Encoding]::UTF8)
    $block = Get-SessionInventoryClassBlock -Lines $lines -ClassName $ClassName -SourceFile $RelativePath
    $entries = New-Object System.Collections.Generic.List[object]
    $conditionalStack = New-Object 'System.Collections.Generic.List[string]'
    $depth = 0

    for ($lineIndex = $block.bodyStartLine + 1; $lineIndex -lt $block.endLine; $lineIndex++) {
        $rawLine = $lines[$lineIndex]
        $line = Get-SessionInventoryLineWithoutComment -Line $rawLine
        $trimmed = $line.Trim()

        if ($trimmed -match '^#if\s+(?<condition>.+)$') {
            $conditionalStack.Add($matches.condition.Trim())
            continue
        }
        if ($trimmed -match '^#else\b') {
            if ($conditionalStack.Count -gt 0) { $conditionalStack[$conditionalStack.Count - 1] = 'ELSE(' + $conditionalStack[$conditionalStack.Count - 1] + ')' }
            continue
        }
        if ($trimmed -match '^#elif\s+(?<condition>.+)$') {
            if ($conditionalStack.Count -gt 0) { $conditionalStack[$conditionalStack.Count - 1] = 'ELIF(' + $matches.condition.Trim() + ')' }
            continue
        }
        if ($trimmed -match '^#endif\b') {
            if ($conditionalStack.Count -gt 0) { $conditionalStack.RemoveAt($conditionalStack.Count - 1) }
            continue
        }

        if ($depth -eq 0) {
            $fieldMatch = [regex]::Match($line, '^\s*(?:public|private|internal|protected)\s+static\s+(?<readonly>readonly\s+)?(?<type>[^;=(){}]+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:=\s*.*)?;\s*$', [Text.RegularExpressions.RegexOptions]::CultureInvariant)
            $propertyMatch = [regex]::Match($line, '^\s*(?:public|private|internal|protected)\s+static\s+(?<type>[^{}();=]+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*\{\s*get\s*;\s*private\s+set\s*;\s*\}(?:\s*=\s*.*)?\s*;?\s*$', [Text.RegularExpressions.RegexOptions]::CultureInvariant)

            if ($fieldMatch.Success) {
                $entries.Add([pscustomobject][ordered]@{
                    scope = $Scope
                    symbol = $fieldMatch.Groups['name'].Value
                    declaredType = $fieldMatch.Groups['type'].Value.Trim()
                    declarationKind = 'Field'
                    mutability = if ($fieldMatch.Groups['readonly'].Success) { 'ReadonlyReferenceOrValue' } else { 'ReplaceableField' }
                    sourceFile = $RelativePath
                    sourceLine = $lineIndex + 1
                    declaration = $rawLine.Trim()
                    conditionalCompilation = Get-SessionInventoryConditionalLabel -Stack $conditionalStack
                })
            }
            elseif ($propertyMatch.Success) {
                $entries.Add([pscustomobject][ordered]@{
                    scope = $Scope
                    symbol = $propertyMatch.Groups['name'].Value
                    declaredType = $propertyMatch.Groups['type'].Value.Trim()
                    declarationKind = 'AutoProperty'
                    mutability = 'PrivateSetterProperty'
                    sourceFile = $RelativePath
                    sourceLine = $lineIndex + 1
                    declaration = $rawLine.Trim()
                    conditionalCompilation = Get-SessionInventoryConditionalLabel -Stack $conditionalStack
                })
            }
        }

        foreach ($character in $line.ToCharArray()) {
            if ($character -eq '{') { $depth++ }
            elseif ($character -eq '}') { $depth-- }
        }
        if ($depth -lt 0) { throw "Unexpected negative member depth while scanning $RelativePath line $($lineIndex + 1)." }
    }

    return @(Sort-SessionInventoryOrdinal -Items $entries.ToArray() -KeySelector { param($entry) $entry.symbol })
}

function Get-SessionInventoryResetSites {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][object[]]$GlobalEntries
    )

    $relativePath = 'Scripts/Emuera/GlobalStatic.cs'
    $path = Join-Path $ProjectRoot ($relativePath.Replace('/', '\'))
    $lines = [IO.File]::ReadAllLines([IO.Path]::GetFullPath($path), [Text.Encoding]::UTF8)
    $resetMethods = @(
        [pscustomobject]@{ name = 'Reset'; pattern = '^\s*public\s+static\s+void\s+Reset\s*\('; prefix = 'Baseline:' },
        [pscustomobject]@{ name = 'ResetCanarySessionState'; pattern = '^\s*internal\s+static\s+void\s+ResetCanarySessionState\s*\('; prefix = 'Canary:' }
    )
    $resetLines = New-Object System.Collections.Generic.List[object]
    foreach ($method in $resetMethods) {
        $resetStart = -1
        for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
            if ($lines[$lineIndex] -match $method.pattern) { $resetStart = $lineIndex; break }
        }
        if ($resetStart -lt 0) {
            if ($method.name -eq 'Reset') { throw 'GlobalStatic.Reset was not found.' }
            continue
        }

        $depth = 0
        $opened = $false
        for ($lineIndex = $resetStart; $lineIndex -lt $lines.Count; $lineIndex++) {
            $line = Get-SessionInventoryLineWithoutComment -Line $lines[$lineIndex]
            $resetLines.Add([pscustomobject]@{ index = $lineIndex; text = $line; prefix = $method.prefix })
            foreach ($character in $line.ToCharArray()) {
                if ($character -eq '{') { $depth++; $opened = $true }
                elseif ($character -eq '}') { $depth-- }
            }
            if ($opened -and $depth -eq 0) { break }
        }
    }

    $result = @{}
    foreach ($entry in $GlobalEntries) {
        $escaped = [regex]::Escape([string]$entry.symbol)
        $matches = New-Object System.Collections.Generic.List[object]
        foreach ($line in $resetLines) {
            $operation = ''
            if ([regex]::IsMatch($line.text, '(?<![A-Za-z0-9_\.])' + $escaped + '\s*=(?!=)', [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                $operation = 'Assign'
            }
            elseif ([regex]::IsMatch($line.text, '(?<![A-Za-z0-9_\.])' + $escaped + '\s*\.\s*(?:Clear|Dispose|Close|CloseAll|Reset(?:SessionState|CanarySessionState)?)\s*\(', [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                $operation = 'ClearOrClose'
            }
            if ($operation.Length -gt 0) {
                $matches.Add([pscustomobject][ordered]@{ sourceFile = $relativePath; sourceLine = $line.index + 1; operation = $line.prefix + $operation })
            }
        }
        $result[$entry.symbol] = @(Sort-SessionInventoryOrdinal -Items $matches.ToArray() -KeySelector { param($site) $site.sourceLine.ToString('D10') + [char]0 + $site.operation })
    }
    return $result
}

function Get-SessionInventoryCatalogMap {
    param([Parameter(Mandatory = $true)][object]$Catalog)

    if ($Catalog.schemaVersion -ne '1.0.0' -or $Catalog.workPackage -ne 'M0-SES-01') { throw 'Unsupported M0-SES-01 session-state catalog.' }
    if ([string]::IsNullOrWhiteSpace([string]$Catalog.catalogId) -or [string]::IsNullOrWhiteSpace([string]$Catalog.catalogVersion)) {
        throw 'Session-state catalog identity is required.'
    }
    $map = @{}
    foreach ($entry in @($Catalog.entries)) {
        foreach ($required in @('scope', 'symbol', 'legacyOwner', 'candidateM1Owner', 'crossSessionExpectation', 'expectedReset', 'fixtureIds', 'migrationStage', 'notes')) {
            if ($null -eq $entry.$required) { throw "Catalog entry is missing required property: $required" }
        }
        if ($entry.scope -notin @('GlobalStatic', 'Program')) { throw "Unsupported catalog scope: $($entry.scope)" }
        if ([string]::IsNullOrWhiteSpace([string]$entry.symbol)) { throw 'Catalog entry symbol cannot be empty.' }
        if (@($entry.fixtureIds).Count -eq 0) { throw "Catalog entry must name at least one fixture: $($entry.scope).$($entry.symbol)" }
        $key = [string]$entry.scope + [char]0 + [string]$entry.symbol
        if ($map.ContainsKey($key)) { throw "Duplicate catalog mapping: $($entry.scope).$($entry.symbol)" }
        $map[$key] = $entry
    }
    return $map
}

function Get-SessionInventoryCatalogHash {
    param([Parameter(Mandatory = $true)][object]$Catalog)

    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($entry in @($Catalog.entries)) {
        $fixtures = @(Sort-SessionInventoryOrdinal -Items @($entry.fixtureIds) -KeySelector { param($fixture) [string]$fixture })
        $entries.Add([pscustomobject][ordered]@{
            scope = [string]$entry.scope
            symbol = [string]$entry.symbol
            legacyOwner = [string]$entry.legacyOwner
            candidateM1Owner = [string]$entry.candidateM1Owner
            crossSessionExpectation = [string]$entry.crossSessionExpectation
            expectedReset = [string]$entry.expectedReset
            fixtureIds = $fixtures
            migrationStage = [string]$entry.migrationStage
            notes = [string]$entry.notes
        })
    }
    $normalized = [pscustomobject][ordered]@{
        schemaVersion = [string]$Catalog.schemaVersion
        workPackage = [string]$Catalog.workPackage
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        entries = @(Sort-SessionInventoryOrdinal -Items $entries.ToArray() -KeySelector { param($entry) $entry.scope + [char]0 + $entry.symbol })
    }
    return Get-SessionInventorySha256Hex -Bytes $script:Utf8NoBom.GetBytes((ConvertTo-SessionInventoryJson -Value $normalized -Compress))
}

function Get-SessionInventorySourceFiles {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot)

    $scriptsRoot = Join-Path $ProjectRoot 'Scripts'
    if (-not (Test-Path -LiteralPath $scriptsRoot -PathType Container)) { throw "Scripts root does not exist: $scriptsRoot" }
    return @(Sort-SessionInventoryOrdinal -Items @([IO.Directory]::EnumerateFiles($scriptsRoot, '*.cs', [IO.SearchOption]::AllDirectories)) -KeySelector {
        param($path) Get-SessionInventoryRelativePath -Root $ProjectRoot -Path $path
    })
}

function Get-SessionInventoryAccesses {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][object[]]$Entries
    )

    $sourceFiles = @(Get-SessionInventorySourceFiles -ProjectRoot $ProjectRoot)
    $accessByKey = @{}
    $referencedSourcePaths = @{}
    $symbolsByScope = @{
        GlobalStatic = New-Object System.Collections.Generic.List[string]
        Program = New-Object System.Collections.Generic.List[string]
    }
    foreach ($entry in $Entries) {
        $key = [string]$entry.scope + [char]0 + [string]$entry.symbol
        $accessByKey[$key] = New-Object System.Collections.Generic.List[object]
        $symbolsByScope[[string]$entry.scope].Add([string]$entry.symbol)
    }

    $patterns = @{}
    foreach ($scope in @('GlobalStatic', 'Program')) {
        $symbols = @(Sort-SessionInventoryOrdinal -Items $symbolsByScope[$scope].ToArray() -KeySelector {
            param($symbol)
            (100000 - ([string]$symbol).Length).ToString('D6', [Globalization.CultureInfo]::InvariantCulture) + [char]0 + [string]$symbol
        })
        $alternatives = @($symbols | ForEach-Object { [regex]::Escape([string]$_) }) -join '|'
        $prefix = if ($scope -eq 'GlobalStatic') { '\bGlobalStatic\.' } else { '\bProgram\.' }
        $patterns[$scope] = [Text.RegularExpressions.Regex]::new($prefix + '(?<symbol>' + $alternatives + ')\b', [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    }

    foreach ($sourcePath in $sourceFiles) {
        $relativePath = Get-SessionInventoryRelativePath -Root $ProjectRoot -Path $sourcePath
        $lines = [IO.File]::ReadAllLines($sourcePath, [Text.Encoding]::UTF8)
        for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
            $line = Get-SessionInventoryLineWithoutComment -Line $lines[$lineIndex]
            foreach ($scope in @('GlobalStatic', 'Program')) {
                foreach ($match in $patterns[$scope].Matches($line)) {
                    $symbol = $match.Groups['symbol'].Value
                    $key = $scope + [char]0 + $symbol
                    $tail = $line.Substring($match.Index + $match.Length)
                    $kind = if ([regex]::IsMatch($tail, '^\s*(?:=(?!=)|\+=|-=|\+\+|--)', [Text.RegularExpressions.RegexOptions]::CultureInvariant) -or
                        [regex]::IsMatch($tail, '^\s*\.\s*(?:Add|Clear|Remove|Enqueue|Dequeue|Push|Pop|Reset|Dispose|Close|CloseAll)\s*\(', [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                        'WriteOrMutation'
                    }
                    else {
                        'Reference'
                    }
                    $accessByKey[$key].Add([pscustomobject][ordered]@{ sourceFile = $relativePath; sourceLine = $lineIndex + 1; accessKind = $kind })
                    $referencedSourcePaths[$relativePath] = $sourcePath
                }
            }
        }
    }

    return [pscustomobject]@{ accessByKey = $accessByKey; referencedSourcePaths = $referencedSourcePaths }
}

function Get-SessionInventoryProgramWriteSites {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][object[]]$ProgramEntries
    )

    $relativePath = 'Scripts/Emuera/Program.cs'
    $path = Join-Path $ProjectRoot ($relativePath.Replace('/', '\'))
    $lines = [IO.File]::ReadAllLines($path, [Text.Encoding]::UTF8)
    $result = @{}
    foreach ($entry in $ProgramEntries) {
        $symbol = [regex]::Escape([string]$entry.symbol)
        $sites = New-Object System.Collections.Generic.List[object]
        for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
            $line = Get-SessionInventoryLineWithoutComment -Line $lines[$lineIndex]
            if ([regex]::IsMatch($line, '(?<![A-Za-z0-9_\.])' + $symbol + '\s*=(?!=)', [Text.RegularExpressions.RegexOptions]::CultureInvariant)) {
                $sites.Add([pscustomobject][ordered]@{ sourceFile = $relativePath; sourceLine = $lineIndex + 1; operation = 'Assign' })
            }
        }
        $result[$entry.symbol] = @(Sort-SessionInventoryOrdinal -Items $sites.ToArray() -KeySelector { param($site) $site.sourceLine.ToString('D10') })
    }
    return $result
}

function New-LegacySessionRootInventory {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][string]$ProjectRoot,
        [Parameter(Mandatory = $true, Position = 1)][object]$Catalog,
        [string]$OutputPath = ''
    )

    $resolvedRoot = [IO.Path]::GetFullPath($ProjectRoot)
    $catalogMap = Get-SessionInventoryCatalogMap -Catalog $Catalog
    $catalogHash = Get-SessionInventoryCatalogHash -Catalog $Catalog
    $globalEntries = @(Get-SessionInventoryRootStateDeclarations -ProjectRoot $resolvedRoot -RelativePath 'Scripts/Emuera/GlobalStatic.cs' -ClassName 'GlobalStatic' -Scope 'GlobalStatic')
    $programEntries = @(Get-SessionInventoryRootStateDeclarations -ProjectRoot $resolvedRoot -RelativePath 'Scripts/Emuera/Program.cs' -ClassName 'Program' -Scope 'Program')
    $observedEntries = @($globalEntries + $programEntries)

    foreach ($entry in $observedEntries) {
        $key = [string]$entry.scope + [char]0 + [string]$entry.symbol
        if (-not $catalogMap.ContainsKey($key)) { throw "Catalog does not classify observed root state: $($entry.scope).$($entry.symbol)" }
    }
    foreach ($catalogKey in $catalogMap.Keys) {
        if (@($observedEntries | Where-Object { (([string]$_.scope) + [char]0 + ([string]$_.symbol)) -eq $catalogKey }).Count -ne 1) {
            throw "Catalog maps unknown root state: $($catalogKey.Replace([string][char]0, '.'))"
        }
    }

    $resetSites = Get-SessionInventoryResetSites -ProjectRoot $resolvedRoot -GlobalEntries $globalEntries
    $accessData = Get-SessionInventoryAccesses -ProjectRoot $resolvedRoot -Entries $observedEntries
    $programWriteSites = Get-SessionInventoryProgramWriteSites -ProjectRoot $resolvedRoot -ProgramEntries $programEntries
    $sourcePaths = @{}
    foreach ($relativePath in @('Scripts/Emuera/GlobalStatic.cs', 'Scripts/Emuera/Program.cs')) {
        $sourcePaths[$relativePath] = Join-Path $resolvedRoot ($relativePath.Replace('/', '\'))
    }
    foreach ($key in $accessData.referencedSourcePaths.Keys) { $sourcePaths[$key] = $accessData.referencedSourcePaths[$key] }

    $entries = New-Object System.Collections.Generic.List[object]
    foreach ($state in @(Sort-SessionInventoryOrdinal -Items $observedEntries -KeySelector { param($entry) $entry.scope + [char]0 + $entry.symbol })) {
        $key = [string]$state.scope + [char]0 + [string]$state.symbol
        $catalogEntry = $catalogMap[$key]
        $references = @(Sort-SessionInventoryOrdinal -Items $accessData.accessByKey[$key].ToArray() -KeySelector { param($access) $access.sourceFile + [char]0 + $access.sourceLine.ToString('D10') + [char]0 + $access.accessKind })
        $writeSites = New-Object System.Collections.Generic.List[object]
        foreach ($access in @($references | Where-Object { $_.accessKind -eq 'WriteOrMutation' })) {
            $writeSites.Add([pscustomobject][ordered]@{ sourceFile = $access.sourceFile; sourceLine = $access.sourceLine; operation = 'WriteOrMutation' })
        }

        if ($state.scope -eq 'GlobalStatic') {
            $currentResetSites = @($resetSites[$state.symbol])
            foreach ($reset in $currentResetSites) { $writeSites.Add($reset) }
            $reset = if ($currentResetSites.Count -gt 0) {
                [pscustomobject][ordered]@{ status = 'ExplicitlyCleared'; reason = 'GlobalStatic.Reset contains an explicit assignment or clear/close call.'; sites = $currentResetSites }
            }
            else {
                [pscustomobject][ordered]@{ status = 'NotObserved'; reason = 'No explicit reset operation was found in GlobalStatic.Reset.'; sites = @() }
            }
        }
        else {
            foreach ($write in @($programWriteSites[$state.symbol])) { $writeSites.Add($write) }
            $reset = [pscustomobject][ordered]@{ status = 'NoLegacyProgramReset'; reason = 'Program has no per-session reset method; M1 must replace this with candidate/commit ownership.'; sites = @() }
        }

        $orderedWriteSites = @(Sort-SessionInventoryOrdinal -Items $writeSites.ToArray() -KeySelector { param($site) $site.sourceFile + [char]0 + $site.sourceLine.ToString('D10') + [char]0 + $site.operation })
        $entries.Add([pscustomobject][ordered]@{
            scope = $state.scope
            symbol = $state.symbol
            declaredType = $state.declaredType
            declarationKind = $state.declarationKind
            mutability = $state.mutability
            sourceFile = $state.sourceFile
            sourceLine = $state.sourceLine
            declaration = $state.declaration
            conditionalCompilation = $state.conditionalCompilation
            legacyOwner = [string]$catalogEntry.legacyOwner
            candidateM1Owner = [string]$catalogEntry.candidateM1Owner
            crossSessionExpectation = [string]$catalogEntry.crossSessionExpectation
            expectedReset = [string]$catalogEntry.expectedReset
            fixtureIds = @(Sort-SessionInventoryOrdinal -Items @($catalogEntry.fixtureIds) -KeySelector { param($fixture) [string]$fixture })
            migrationStage = [string]$catalogEntry.migrationStage
            notes = [string]$catalogEntry.notes
            reset = $reset
            referenceCount = $references.Count
            writeSites = $orderedWriteSites
            references = $references
        })
    }

    $sourceIdentities = New-Object System.Collections.Generic.List[object]
    foreach ($relativePath in @(Sort-SessionInventoryOrdinal -Items @($sourcePaths.Keys) -KeySelector { param($path) [string]$path })) {
        $path = $sourcePaths[$relativePath]
        $sourceIdentities.Add([pscustomobject][ordered]@{
            sourceFile = [string]$relativePath
            sha256 = Get-SessionInventorySha256Hex -Bytes ([IO.File]::ReadAllBytes($path))
        })
    }

    $globalNotReset = @($entries | Where-Object { $_.scope -eq 'GlobalStatic' -and $_.reset.status -ne 'ExplicitlyCleared' })
    $hashPayload = [pscustomobject][ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-SES-01'
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = $catalogHash
        sourceIdentities = $sourceIdentities.ToArray()
        entries = $entries.ToArray()
        runtimeIsolation = 'Failed'
        parserVmConsumption = 'NotConsumed'
    }
    $inventorySetHash = Get-SessionInventorySha256Hex -Bytes $script:Utf8NoBom.GetBytes((ConvertTo-SessionInventoryJson -Value $hashPayload -Compress))

    $report = [pscustomobject][ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-SES-01'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = $catalogHash
        inventorySetHash = $inventorySetHash
        scope = [pscustomobject][ordered]@{
            included = @('MinorShift.Emuera.GlobalStatic direct root fields', 'MinorShift.Emuera.Program mutable static fields and private-set auto-properties')
            excluded = @('Other static caches, Godot view singletons, platform bridges, diagnostics and third-party state remain outside this root-only inventory.')
        }
        sourceIdentities = $sourceIdentities.ToArray()
        coverage = [pscustomobject][ordered]@{
            globalStaticStateCount = $globalEntries.Count
            programStateCount = $programEntries.Count
            observedStateCount = $observedEntries.Count
            catalogMappedCount = $entries.Count
            unmappedCount = 0
            explicitlyResetGlobalStateCount = @($entries | Where-Object { $_.scope -eq 'GlobalStatic' -and $_.reset.status -eq 'ExplicitlyCleared' }).Count
            globalStateWithoutObservedResetCount = $globalNotReset.Count
            directReferenceCount = @($entries | ForEach-Object { $_.referenceCount } | Measure-Object -Sum).Sum
        }
        currentRuntimeIsolation = [pscustomobject][ordered]@{ status = 'Failed'; reason = 'Program.CoreProfile and GlobalStatic remain process-wide mutable state in the legacy runtime.' }
        parserVmConsumption = [pscustomobject][ordered]@{ status = 'NotConsumed'; reason = 'This M0 inventory is source evidence only and is not read by the legacy Parser or VM.' }
        m1Eligibility = [pscustomobject][ordered]@{ status = 'Blocked'; reason = 'Only the central GlobalStatic/Program root is inventoried; M0 approval, remaining static state inventory, generation guards and rollback evidence are still required.' }
        uncovered = @(
            'The inventory does not prove runtime reset ordering, thread safety or A-to-B-to-A isolation.',
            'Other mutable static caches and Godot/platform objects are intentionally outside this root-only M0 scope.',
            'No LegacySessionFacade, feature flag, CompatibilityPlan runtime object or frozen registry is implemented or consumed.',
            'No Era game was started by this static source analysis.'
        )
        entries = $entries.ToArray()
    }

    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        $directory = Split-Path -Parent $fullOutput
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
        [IO.File]::WriteAllText($fullOutput, (ConvertTo-SessionInventoryJson -Value $report), $script:Utf8NoBom)
    }

    return $report
}

Export-ModuleMember -Function New-LegacySessionRootInventory
