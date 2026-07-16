Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# M0-SAV-01 captures legacy binary save wire facts as offline source evidence.
# It deliberately does not open a save file, execute a codec, choose a profile,
# mutate the legacy variable store, or become a runtime save resolver.
$script:LegacySaveBaselineUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-LegacySaveBaselineSha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-LegacySaveBaselineCanonicalHash {
    param([Parameter(Mandatory = $true)][object]$Value, [int]$Depth = 100)

    return Get-LegacySaveBaselineSha256Hex -Bytes $script:LegacySaveBaselineUtf8NoBom.GetBytes(($Value | ConvertTo-Json -Depth $Depth -Compress))
}

function Assert-LegacySaveBaselineHash {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid M0-SAV-01 hash: $Name" }
}

function Assert-LegacySaveBaselineSemanticVersion {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') { throw "Invalid M0-SAV-01 semantic version: $Name" }
}

function Assert-LegacySaveBaselineObjectShape {
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
        $allowedProperty = $false
        foreach ($allowedName in $Allowed) {
            if ($property.Name -ceq $allowedName) { $allowedProperty = $true; break }
        }
        if (-not $allowedProperty) { throw "$Context contains unsupported field: $($property.Name)" }
    }
}

function ConvertTo-LegacySaveBaselineHexByte {
    param([Parameter(Mandatory = $true)][object]$Value, [Parameter(Mandatory = $true)][string]$Context)

    $text = [string]$Value
    if ($text -notmatch '^0x[0-9A-Fa-f]{2}$') { throw "Invalid M0-SAV-01 byte code: $Context" }
    $number = [Convert]::ToInt32($text.Substring(2), 16)
    return [pscustomobject][ordered]@{ number = $number; hexCode = ('0x{0:X2}' -f $number) }
}

function Get-LegacySaveBaselineStringSet {
    param([object[]]$Values, [Parameter(Mandatory = $true)][string]$Name, [bool]$AllowEmpty = $false)

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

function Assert-LegacySaveBaselineStringSet {
    param([object[]]$Actual, [object[]]$Expected, [Parameter(Mandatory = $true)][string]$Context, [bool]$AllowEmpty = $false)

    $actualValues = @(Get-LegacySaveBaselineStringSet -Values $Actual -Name "$Context actual values" -AllowEmpty $AllowEmpty)
    $expectedValues = @(Get-LegacySaveBaselineStringSet -Values $Expected -Name "$Context expected values" -AllowEmpty $AllowEmpty)
    if ($actualValues.Count -ne $expectedValues.Count) { throw "$Context count drifted: expected $($expectedValues.Count), actual $($actualValues.Count)." }
    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -cne $expectedValues[$index]) { throw "$Context value drifted: expected $($expectedValues[$index]), actual $($actualValues[$index])." }
    }
}

function Resolve-LegacySaveBaselineProjectFile {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot, [Parameter(Mandatory = $true)][string]$RelativePath)

    if ([string]::IsNullOrWhiteSpace($RelativePath) -or $RelativePath -match '(^[\\/]|^[A-Za-z]:|(^|[\\/])\.\.([\\/]|$))') {
        throw "Invalid M0-SAV-01 relative source path: $RelativePath"
    }
    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $fullPath = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    $rootWithSeparator = $root + [IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($rootWithSeparator, [StringComparison]::OrdinalIgnoreCase)) { throw "M0-SAV-01 source path escaped project root: $RelativePath" }
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "M0-SAV-01 source file is missing: $RelativePath" }
    return $fullPath
}

function Get-LegacySaveBaselineSourceFile {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot, [Parameter(Mandatory = $true)][object]$SourceCatalog)

    $fields = @('relativePath', 'sha256', 'byteLength')
    Assert-LegacySaveBaselineObjectShape -Value $SourceCatalog -Required $fields -Allowed $fields -Context 'M0-SAV-01 source catalog entry'
    $relativePath = ([string]$SourceCatalog.relativePath).Replace('\', '/')
    Assert-LegacySaveBaselineHash $SourceCatalog.sha256 "source $relativePath sha256"
    if ([int64]$SourceCatalog.byteLength -le 0) { throw "Invalid M0-SAV-01 source byte length: $relativePath" }
    $fullPath = Resolve-LegacySaveBaselineProjectFile -ProjectRoot $ProjectRoot -RelativePath $relativePath
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    $actualHash = Get-LegacySaveBaselineSha256Hex -Bytes $bytes
    if ($actualHash -cne [string]$SourceCatalog.sha256) { throw "M0-SAV-01 source hash drifted: $relativePath" }
    if ($bytes.LongLength -ne [int64]$SourceCatalog.byteLength) { throw "M0-SAV-01 source byte length drifted: $relativePath" }
    return [pscustomobject][ordered]@{
        relativePath = $relativePath
        sha256 = $actualHash
        byteLength = [int64]$bytes.LongLength
        text = [Text.Encoding]::UTF8.GetString($bytes)
    }
}

function Get-LegacySaveBaselineEnumEntries {
    param([Parameter(Mandatory = $true)][string]$Text, [Parameter(Mandatory = $true)][string]$EnumName)

    $enumPattern = "public\s+enum\s+$([regex]::Escape($EnumName))\s*:\s*byte\s*\{(?<body>.*?)^\s*\}"
    $enumMatch = [regex]::Match($Text, $enumPattern, [Text.RegularExpressions.RegexOptions]::Singleline -bor [Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $enumMatch.Success) { throw "M0-SAV-01 enum was not found: $EnumName" }
    $entries = New-Object 'System.Collections.Generic.List[object]'
    $entryPattern = '(?m)^\s*(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<hex>0x[0-9A-Fa-f]+)\s*,?'
    foreach ($match in [regex]::Matches($enumMatch.Groups['body'].Value, $entryPattern)) {
        $hex = ConvertTo-LegacySaveBaselineHexByte -Value $match.Groups['hex'].Value -Context "$EnumName.$($match.Groups['id'].Value)"
        $entries.Add([pscustomobject][ordered]@{ id = $match.Groups['id'].Value; hexCode = $hex.hexCode; numericCode = $hex.number })
    }
    if ($entries.Count -eq 0) { throw "M0-SAV-01 enum has no numeric entries: $EnumName" }
    return @($entries | Sort-Object @{ Expression = { $_.numericCode } }, @{ Expression = { $_.id } })
}

function Get-LegacySaveBaselineConstLiteral {
    param([Parameter(Mandatory = $true)][string]$Text, [Parameter(Mandatory = $true)][string]$ConstName)

    $pattern = "public\s+const\s+(?:UInt64|UInt32)\s+$([regex]::Escape($ConstName))\s*=\s*(?<literal>0x[0-9A-Fa-f]+(?:UL)?|[0-9]+)\s*;"
    $match = [regex]::Match($Text, $pattern)
    if (-not $match.Success) { throw "M0-SAV-01 header constant was not found: $ConstName" }
    return $match.Groups['literal'].Value
}

function ConvertTo-LegacySaveBaselineUInt64Hex {
    param([Parameter(Mandatory = $true)][string]$Literal, [Parameter(Mandatory = $true)][string]$Context)

    $trimmed = $Literal.TrimEnd('U', 'L', 'u', 'l')
    if ($trimmed -notmatch '^0x[0-9A-Fa-f]+$') { throw "Invalid M0-SAV-01 UInt64 literal: $Context" }
    $value = [Convert]::ToUInt64($trimmed.Substring(2), 16)
    return ('0x{0:X16}' -f $value)
}

function Get-LegacySaveBaselineExpectedEntries {
    param([object[]]$Entries, [Parameter(Mandatory = $true)][string]$Context, [string[]]$AdditionalFields = @())

    $required = @('id', 'hexCode') + $AdditionalFields
    $allowed = @('id', 'hexCode') + $AdditionalFields
    $byId = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    $byCode = New-Object 'System.Collections.Generic.SortedDictionary[string,string]' ([StringComparer]::Ordinal)
    foreach ($entry in @($Entries)) {
        Assert-LegacySaveBaselineObjectShape -Value $entry -Required $required -Allowed $allowed -Context $Context
        $id = [string]$entry.id
        if ($id -notmatch '^[A-Za-z][A-Za-z0-9]*$') { throw "Invalid M0-SAV-01 $Context id: $id" }
        $hex = ConvertTo-LegacySaveBaselineHexByte -Value $entry.hexCode -Context "$Context.$id"
        if ($byId.ContainsKey($id)) { throw "Duplicate M0-SAV-01 $Context id: $id" }
        if ($byCode.ContainsKey($hex.hexCode)) { throw "Duplicate M0-SAV-01 $Context code: $($hex.hexCode)" }
        $copy = [ordered]@{ id = $id; hexCode = $hex.hexCode; numericCode = $hex.number }
        foreach ($field in $AdditionalFields) {
            if ($field -eq 'pathForms') {
                $copy[$field] = @(Get-LegacySaveBaselineStringSet -Values @($entry.$field) -Name "$Context.$id path form")
            }
            else {
                if ([string]::IsNullOrWhiteSpace([string]$entry.$field)) { throw "M0-SAV-01 $Context.$id has an empty $field." }
                $copy[$field] = [string]$entry.$field
            }
        }
        $byId.Add($id, [pscustomobject]$copy)
        $byCode.Add($hex.hexCode, $id)
    }
    return [pscustomobject][ordered]@{ byId = $byId; byCode = $byCode }
}

function Assert-LegacySaveBaselineEntriesMatch {
    param([Parameter(Mandatory = $true)][object]$ActualEntries, [Parameter(Mandatory = $true)][object]$ExpectedEntries, [Parameter(Mandatory = $true)][string]$Context)

    if ($ActualEntries.Count -ne $ExpectedEntries.byId.Count) { throw "M0-SAV-01 $Context count drifted: expected $($ExpectedEntries.byId.Count), actual $($ActualEntries.Count)." }
    $actualById = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($entry in @($ActualEntries)) {
        if ($actualById.ContainsKey([string]$entry.id)) { throw "Duplicate M0-SAV-01 observed $Context id: $($entry.id)" }
        $actualById.Add([string]$entry.id, $entry)
    }
    foreach ($expectedId in $ExpectedEntries.byId.Keys) {
        if (-not $actualById.ContainsKey($expectedId)) { throw "M0-SAV-01 $Context is missing id: $expectedId" }
        if ([string]$actualById[$expectedId].hexCode -cne [string]$ExpectedEntries.byId[$expectedId].hexCode) {
            throw "M0-SAV-01 $Context code drifted for ${expectedId}: expected $($ExpectedEntries.byId[$expectedId].hexCode), actual $($actualById[$expectedId].hexCode)."
        }
    }
}

function Test-LegacySaveBaselineSourceSymbol {
    param([Parameter(Mandatory = $true)][string]$Text, [Parameter(Mandatory = $true)][string]$Symbol, [Parameter(Mandatory = $true)][string]$Context)

    if (-not [regex]::IsMatch($Text, "\b$([regex]::Escape($Symbol))\s*\(")) { throw "M0-SAV-01 source symbol is missing: $Context.$Symbol" }
}

function Get-LegacySaveBaselineCatalogContext {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot, [Parameter(Mandatory = $true)][object]$Catalog)

    $catalogFields = @('schemaVersion', 'catalogId', 'catalogVersion', 'sourceWorkPackage', 'expectedSourceFileCount', 'sources', 'header', 'expectedFileTypes', 'expectedDataTypes', 'expectedSparseMarkers', 'profileConflict', 'legacyMutationEvidence')
    Assert-LegacySaveBaselineObjectShape -Value $Catalog -Required $catalogFields -Allowed $catalogFields -Context 'M0-SAV-01 catalog'
    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or [string]$Catalog.sourceWorkPackage -cne 'M0-SAV-01' -or [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) { throw 'Unsupported M0-SAV-01 catalog.' }
    Assert-LegacySaveBaselineSemanticVersion $Catalog.catalogVersion 'catalogVersion'
    if ([int]$Catalog.expectedSourceFileCount -ne 5) { throw 'M0-SAV-01 source file count drifted.' }
    if (@($Catalog.sources).Count -ne 5) { throw 'M0-SAV-01 source file count drifted.' }
    if (@($Catalog.expectedFileTypes).Count -ne 4) { throw 'M0-SAV-01 file type count drifted.' }
    if (@($Catalog.expectedDataTypes).Count -ne 19) { throw 'M0-SAV-01 data type count drifted.' }
    if (@($Catalog.expectedSparseMarkers).Count -ne 11) { throw 'M0-SAV-01 sparse marker count drifted.' }
    if (@($Catalog.legacyMutationEvidence).Count -ne 2) { throw 'M0-SAV-01 legacy mutation evidence count drifted.' }

    $sourceByPath = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($source in @($Catalog.sources)) {
        $sourceFact = Get-LegacySaveBaselineSourceFile -ProjectRoot $ProjectRoot -SourceCatalog $source
        if ($sourceByPath.ContainsKey($sourceFact.relativePath)) { throw "Duplicate M0-SAV-01 source path: $($sourceFact.relativePath)" }
        $sourceByPath.Add($sourceFact.relativePath, $sourceFact)
    }

    $headerFields = @('headerHex', 'zipHeaderHex', 'formatVersion', 'dataCount', 'minimumBytes', 'encoding')
    Assert-LegacySaveBaselineObjectShape -Value $Catalog.header -Required $headerFields -Allowed $headerFields -Context 'M0-SAV-01 header catalog'
    $header = [ordered]@{
        headerHex = ConvertTo-LegacySaveBaselineUInt64Hex -Literal ([string]$Catalog.header.headerHex) -Context 'headerHex'
        zipHeaderHex = ConvertTo-LegacySaveBaselineUInt64Hex -Literal ([string]$Catalog.header.zipHeaderHex) -Context 'zipHeaderHex'
        formatVersion = [int]$Catalog.header.formatVersion
        dataCount = [int]$Catalog.header.dataCount
        minimumBytes = [int]$Catalog.header.minimumBytes
        encoding = [string]$Catalog.header.encoding
    }
    if ($header.formatVersion -ne 1808 -or $header.dataCount -ne 0 -or $header.minimumBytes -ne 16 -or $header.encoding -cne 'Encoding.Unicode') { throw 'M0-SAV-01 header catalog drifted.' }

    $fileTypes = Get-LegacySaveBaselineExpectedEntries -Entries @($Catalog.expectedFileTypes) -Context 'file type' -AdditionalFields @('pathForms', 'writerMethod', 'readerMethod')
    $dataTypes = Get-LegacySaveBaselineExpectedEntries -Entries @($Catalog.expectedDataTypes) -Context 'data type'
    $sparseMarkers = Get-LegacySaveBaselineExpectedEntries -Entries @($Catalog.expectedSparseMarkers) -Context 'sparse marker'

    $profileFields = @('candidateSaveProfileIds', 'legacyFloatCodes', 'upstreamConflictingCodes', 'automaticSelectionStatus')
    Assert-LegacySaveBaselineObjectShape -Value $Catalog.profileConflict -Required $profileFields -Allowed $profileFields -Context 'M0-SAV-01 profile conflict catalog'
    $candidateProfiles = @(Get-LegacySaveBaselineStringSet -Values @($Catalog.profileConflict.candidateSaveProfileIds) -Name 'M0-SAV-01 candidate save profile')
    Assert-LegacySaveBaselineStringSet -Actual $candidateProfiles -Expected @('GEmueraSnake', 'Upstream1808') -Context 'M0-SAV-01 candidate save profile'
    $legacyFloatCodes = New-Object 'System.Collections.Generic.List[string]'
    foreach ($value in @(Get-LegacySaveBaselineStringSet -Values @($Catalog.profileConflict.legacyFloatCodes) -Name 'M0-SAV-01 legacy float code')) { $legacyFloatCodes.Add((ConvertTo-LegacySaveBaselineHexByte -Value $value -Context 'legacy float code').hexCode) }
    $upstreamConflictCodes = New-Object 'System.Collections.Generic.List[string]'
    foreach ($value in @(Get-LegacySaveBaselineStringSet -Values @($Catalog.profileConflict.upstreamConflictingCodes) -Name 'M0-SAV-01 upstream conflicting code')) { $upstreamConflictCodes.Add((ConvertTo-LegacySaveBaselineHexByte -Value $value -Context 'upstream conflicting code').hexCode) }
    Assert-LegacySaveBaselineStringSet -Actual $legacyFloatCodes.ToArray() -Expected @('0x20', '0x21', '0x22', '0x23') -Context 'M0-SAV-01 legacy float code'
    Assert-LegacySaveBaselineStringSet -Actual $upstreamConflictCodes.ToArray() -Expected @('0x20', '0x21', '0x22') -Context 'M0-SAV-01 upstream conflicting code'
    if ([string]$Catalog.profileConflict.automaticSelectionStatus -cne 'Unbound') { throw 'M0-SAV-01 automatic selection status drifted.' }

    $mutationEvidence = New-Object 'System.Collections.Generic.List[object]'
    $mutationIds = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($mutation in @($Catalog.legacyMutationEvidence)) {
        $mutationFields = @('id', 'relativePath', 'requiredLiterals')
        Assert-LegacySaveBaselineObjectShape -Value $mutation -Required $mutationFields -Allowed $mutationFields -Context 'M0-SAV-01 legacy mutation evidence'
        $id = [string]$mutation.id
        if ($id -notmatch '^[a-z][a-z0-9-]*$' -or -not $mutationIds.Add($id)) { throw "Invalid or duplicate M0-SAV-01 legacy mutation evidence id: $id" }
        $relativePath = ([string]$mutation.relativePath).Replace('\', '/')
        if (-not $sourceByPath.ContainsKey($relativePath)) { throw "M0-SAV-01 mutation evidence references untracked source: $relativePath" }
        $literals = @(Get-LegacySaveBaselineStringSet -Values @($mutation.requiredLiterals) -Name "M0-SAV-01 mutation $id literal")
        foreach ($literal in $literals) {
            if ($sourceByPath[$relativePath].text.IndexOf($literal, [StringComparison]::Ordinal) -lt 0) { throw "M0-SAV-01 mutation evidence literal is missing: $id" }
        }
        $mutationEvidence.Add([pscustomobject][ordered]@{ id = $id; relativePath = $relativePath; requiredLiterals = @($literals); status = 'ObservedStatic' })
    }

    $canonicalCatalog = [ordered]@{
        schemaVersion = '1.0.0'; catalogId = [string]$Catalog.catalogId; catalogVersion = [string]$Catalog.catalogVersion; sourceWorkPackage = 'M0-SAV-01'
        expectedSourceFileCount = 5
        sources = @($sourceByPath.Values | ForEach-Object { [ordered]@{ relativePath = $_.relativePath; sha256 = $_.sha256; byteLength = $_.byteLength } })
        header = $header
        expectedFileTypes = @($fileTypes.byId.Values | Sort-Object @{ Expression = { $_.numericCode } }, @{ Expression = { $_.id } } | ForEach-Object { [ordered]@{ id = $_.id; hexCode = $_.hexCode; pathForms = @($_.pathForms); writerMethod = $_.writerMethod; readerMethod = $_.readerMethod } })
        expectedDataTypes = @($dataTypes.byId.Values | Sort-Object @{ Expression = { $_.numericCode } }, @{ Expression = { $_.id } } | ForEach-Object { [ordered]@{ id = $_.id; hexCode = $_.hexCode } })
        expectedSparseMarkers = @($sparseMarkers.byId.Values | Sort-Object @{ Expression = { $_.numericCode } }, @{ Expression = { $_.id } } | ForEach-Object { [ordered]@{ id = $_.id; hexCode = $_.hexCode } })
        profileConflict = [ordered]@{ candidateSaveProfileIds = @($candidateProfiles); legacyFloatCodes = @(Get-LegacySaveBaselineStringSet -Values $legacyFloatCodes.ToArray() -Name 'canonical legacy float code'); upstreamConflictingCodes = @(Get-LegacySaveBaselineStringSet -Values $upstreamConflictCodes.ToArray() -Name 'canonical upstream conflicting code'); automaticSelectionStatus = 'Unbound' }
        legacyMutationEvidence = @($mutationEvidence | Sort-Object id | ForEach-Object { [ordered]@{ id = $_.id; relativePath = $_.relativePath; requiredLiterals = @($_.requiredLiterals) } })
    }
    return [pscustomobject][ordered]@{
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = Get-LegacySaveBaselineCanonicalHash -Value $canonicalCatalog
        sourceByPath = $sourceByPath
        header = $header
        fileTypes = $fileTypes
        dataTypes = $dataTypes
        sparseMarkers = $sparseMarkers
        candidateProfiles = @($candidateProfiles)
        legacyFloatCodes = @(Get-LegacySaveBaselineStringSet -Values $legacyFloatCodes.ToArray() -Name 'M0-SAV-01 legacy float code')
        upstreamConflictCodes = @(Get-LegacySaveBaselineStringSet -Values $upstreamConflictCodes.ToArray() -Name 'M0-SAV-01 upstream conflicting code')
        mutationEvidence = @($mutationEvidence | Sort-Object id)
    }
}

function New-LegacySaveBaselineReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][object]$Catalog,
        [string]$OutputPath = ''
    )

    $context = Get-LegacySaveBaselineCatalogContext -ProjectRoot $ProjectRoot -Catalog $Catalog
    $reader = $context.sourceByPath['Scripts/Emuera/Sub/EraBinaryDataReader.cs']
    $writer = $context.sourceByPath['Scripts/Emuera/Sub/EraBinaryDataWriter.cs']
    $evaluator = $context.sourceByPath['Scripts/Emuera/GameData/Variable/VariableEvaluator.cs']
    if ($null -eq $reader -or $null -eq $writer -or $null -eq $evaluator) { throw 'M0-SAV-01 required source binding is missing.' }

    if ($reader.text.IndexOf('fs.Length < 16', [StringComparison]::Ordinal) -lt 0 -or $reader.text.IndexOf('Encoding.Unicode', [StringComparison]::Ordinal) -lt 0 -or $reader.text.IndexOf('version == EraBDConst.Version1808', [StringComparison]::Ordinal) -lt 0) { throw 'M0-SAV-01 reader header guard drifted.' }
    if ($writer.text.IndexOf('EraBDConst.ZipHeader', [StringComparison]::Ordinal) -lt 0 -or $writer.text.IndexOf('EraBDConst.Header', [StringComparison]::Ordinal) -lt 0) { throw 'M0-SAV-01 writer header selection drifted.' }

    $observedHeader = [ordered]@{
        headerHex = ConvertTo-LegacySaveBaselineUInt64Hex -Literal (Get-LegacySaveBaselineConstLiteral -Text $reader.text -ConstName 'Header') -Context 'observed Header'
        zipHeaderHex = ConvertTo-LegacySaveBaselineUInt64Hex -Literal (Get-LegacySaveBaselineConstLiteral -Text $reader.text -ConstName 'ZipHeader') -Context 'observed ZipHeader'
        formatVersion = [int](Get-LegacySaveBaselineConstLiteral -Text $reader.text -ConstName 'Version1808')
        dataCount = [int](Get-LegacySaveBaselineConstLiteral -Text $reader.text -ConstName 'DataCount')
        minimumBytes = 16
        encoding = 'Encoding.Unicode'
    }
    foreach ($field in @('headerHex', 'zipHeaderHex', 'formatVersion', 'dataCount', 'minimumBytes', 'encoding')) {
        if ([string]$observedHeader[$field] -cne [string]$context.header[$field]) { throw "M0-SAV-01 header fact drifted: $field" }
    }

    $observedFileTypes = @(Get-LegacySaveBaselineEnumEntries -Text $reader.text -EnumName 'EraSaveFileType')
    $observedDataTypes = @(Get-LegacySaveBaselineEnumEntries -Text $reader.text -EnumName 'EraSaveDataType')
    $sparsePattern = 'static\s+class\s+Ebdb.*?\{(?<body>.*?)^\s*\}'
    $sparseMatch = [regex]::Match($reader.text, $sparsePattern, [Text.RegularExpressions.RegexOptions]::Singleline -bor [Text.RegularExpressions.RegexOptions]::Multiline)
    if (-not $sparseMatch.Success) { throw 'M0-SAV-01 sparse marker class was not found.' }
    $observedSparseMarkers = New-Object 'System.Collections.Generic.List[object]'
    foreach ($match in [regex]::Matches($sparseMatch.Groups['body'].Value, '(?m)^\s*public\s+const\s+byte\s+(?<id>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<hex>0x[0-9A-Fa-f]+)\s*;')) {
        $hex = ConvertTo-LegacySaveBaselineHexByte -Value $match.Groups['hex'].Value -Context "Ebdb.$($match.Groups['id'].Value)"
        $observedSparseMarkers.Add([pscustomobject][ordered]@{ id = $match.Groups['id'].Value; hexCode = $hex.hexCode; numericCode = $hex.number })
    }
    $observedSparseMarkers = @($observedSparseMarkers | Sort-Object @{ Expression = { $_.numericCode } }, @{ Expression = { $_.id } })
    Assert-LegacySaveBaselineEntriesMatch -ActualEntries $observedFileTypes -ExpectedEntries $context.fileTypes -Context 'file type'
    Assert-LegacySaveBaselineEntriesMatch -ActualEntries $observedDataTypes -ExpectedEntries $context.dataTypes -Context 'data type'
    Assert-LegacySaveBaselineEntriesMatch -ActualEntries $observedSparseMarkers -ExpectedEntries $context.sparseMarkers -Context 'sparse marker'

    $fileTypes = New-Object 'System.Collections.Generic.List[object]'
    foreach ($entry in @($context.fileTypes.byId.Values | Sort-Object @{ Expression = { $_.numericCode } }, @{ Expression = { $_.id } })) {
        Test-LegacySaveBaselineSourceSymbol -Text $evaluator.text -Symbol $entry.writerMethod -Context "file type $($entry.id) writer"
        Test-LegacySaveBaselineSourceSymbol -Text $evaluator.text -Symbol $entry.readerMethod -Context "file type $($entry.id) reader"
        $fileTypes.Add([pscustomobject][ordered]@{ id = $entry.id; hexCode = $entry.hexCode; pathForms = @($entry.pathForms); writerMethod = $entry.writerMethod; readerMethod = $entry.readerMethod; sourceStatus = 'ObservedStatic' })
    }

    $dataTypes = @($observedDataTypes | ForEach-Object { [pscustomobject][ordered]@{ id = $_.id; hexCode = $_.hexCode; sourceStatus = 'ObservedStatic' } })
    $sparseMarkers = @($observedSparseMarkers | ForEach-Object { [pscustomobject][ordered]@{ id = $_.id; hexCode = $_.hexCode; sourceStatus = 'ObservedStatic' } })
    $legacyFloatObserved = @($dataTypes | Where-Object { $_.id -match '^Float' } | ForEach-Object hexCode)
    Assert-LegacySaveBaselineStringSet -Actual $legacyFloatObserved -Expected $context.legacyFloatCodes -Context 'M0-SAV-01 observed legacy float code'

    $profileConflict = [ordered]@{
        status = 'StaticConflictObserved'
        candidateSaveProfileIds = @($context.candidateProfiles)
        legacyFloatCodes = @($context.legacyFloatCodes)
        upstreamConflictingCodes = @($context.upstreamConflictCodes)
        automaticSelectionStatus = 'Unbound'
        reason = 'The legacy reader declares Float 0x20..0x23 while the target design records a conflicting upstream interpretation for 0x20..0x22; no source-only rule may choose a SaveProfileId.'
    }
    $fixtureEvidence = [ordered]@{
        status = 'Uncovered'
        sourceSaveFilesRead = 0
        offsetMapStatus = 'Uncovered'
        roundTripStatus = 'Uncovered'
        gameDirectoryAccess = 'NotUsed'
        reason = 'M0-SAV-01 inventories source only; it neither opens a save file nor writes an isolated copy.'
    }
    $saveProfileRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'No runtime SaveProfile resolver, pin, manifest binding or codec registry is introduced by this offline inventory.' }
    $candidateParseCommit = [ordered]@{ status = 'NotImplemented'; reason = 'Legacy code still exposes in-place load mutations; M0-SAV-01 does not create a candidate ParsedSave or commit boundary.' }
    $m1Eligibility = [ordered]@{ status = 'Blocked'; reason = 'Static save facts and conflict visibility do not establish fixture round trips, session isolation, generation guards or a LegacySessionFacade.' }
    $protocolCounts = [ordered]@{
        sourceFileCount = [int]$context.sourceByPath.Count
        fileTypeCount = [int]$fileTypes.Count
        dataTypeCount = [int]$dataTypes.Count
        sparseMarkerCount = [int]$sparseMarkers.Count
        profileCandidateCount = [int]$context.candidateProfiles.Count
        legacyMutationEvidenceCount = [int]$context.mutationEvidence.Count
    }
    if ($protocolCounts.sourceFileCount -ne 5 -or $protocolCounts.fileTypeCount -ne 4 -or $protocolCounts.dataTypeCount -ne 19 -or $protocolCounts.sparseMarkerCount -ne 11 -or $protocolCounts.profileCandidateCount -ne 2 -or $protocolCounts.legacyMutationEvidenceCount -ne 2) { throw 'M0-SAV-01 protocol count drifted.' }

    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'; workPackage = 'M0-SAV-01'; catalogId = $context.catalogId; catalogVersion = $context.catalogVersion; catalogHash = $context.catalogHash
        sourceFiles = @($context.sourceByPath.Values | ForEach-Object { [ordered]@{ relativePath = $_.relativePath; sha256 = $_.sha256; byteLength = $_.byteLength } })
        protocolCounts = $protocolCounts; header = $observedHeader; fileTypes = $fileTypes.ToArray(); dataTypes = $dataTypes; sparseMarkers = $sparseMarkers
        profileConflict = $profileConflict; legacyMutationEvidence = @($context.mutationEvidence); fixtureEvidence = $fixtureEvidence; saveProfileRuntime = $saveProfileRuntime; candidateParseCommit = $candidateParseCommit; m1Eligibility = $m1Eligibility
    }
    $saveBaselineSetHash = Get-LegacySaveBaselineCanonicalHash -Value $setPayload
    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-SAV-01'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        catalogId = $context.catalogId
        catalogVersion = $context.catalogVersion
        catalogHash = $context.catalogHash
        saveBaselineSetHash = $saveBaselineSetHash
        sourceFiles = @($context.sourceByPath.Values | ForEach-Object { [pscustomobject][ordered]@{ relativePath = $_.relativePath; sha256 = $_.sha256; byteLength = $_.byteLength } })
        protocolCounts = $protocolCounts
        header = $observedHeader
        fileTypes = $fileTypes.ToArray()
        dataTypes = $dataTypes
        sparseMarkers = $sparseMarkers
        profileConflict = $profileConflict
        legacyMutationEvidence = @($context.mutationEvidence)
        fixtureEvidence = $fixtureEvidence
        saveProfileRuntime = $saveProfileRuntime
        candidateParseCommit = $candidateParseCommit
        m1Eligibility = $m1Eligibility
        uncovered = @(
            'M0-SAV-01 has no original save-file byte fixture, offset map, parse result, target read/write result or isolated round trip.',
            'The source-only float code conflict does not determine Upstream1808 versus GEmueraSnake for any game, save file or CompatibilityPack.',
            'No runtime SaveProfile resolver, codec registry, candidate store, LegacySessionFacade, generation guard, Parser/VM wiring, game run, APK or device evidence is introduced.'
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, (($report | ConvertTo-Json -Depth 100) + "`n"), $script:LegacySaveBaselineUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-LegacySaveBaselineReport
