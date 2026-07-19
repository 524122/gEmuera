Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# M0-DIA-11 reads source and existing M0 evidence only.  It is deliberately
# not a runtime profile resolver and must not inspect a game directory.
$script:DialectProfileSelectionUtf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-DialectProfileSelectionSha256Hex {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)

    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-DialectProfileSelectionCanonicalHash {
    param([Parameter(Mandatory = $true)][object]$Value, [int]$Depth = 30)

    return Get-DialectProfileSelectionSha256Hex -Bytes $script:DialectProfileSelectionUtf8NoBom.GetBytes(($Value | ConvertTo-Json -Depth $Depth -Compress))
}

function Assert-DialectProfileSelectionHash {
    param([object]$Value, [string]$Name)

    if ([string]$Value -notmatch '^[0-9a-f]{64}$') { throw "Invalid DIA-11 hash: $Name" }
}

function Get-DialectProfileSelectionSortedStrings {
    param([object[]]$Values, [Parameter(Mandatory = $true)][string]$Name)

    $set = New-Object 'System.Collections.Generic.SortedSet[string]' ([StringComparer]::Ordinal)
    foreach ($value in @($Values)) {
        if ($null -eq $value -or [string]::IsNullOrWhiteSpace([string]$value)) { throw "$Name contains an empty value." }
        $text = [string]$value
        if (-not $set.Add($text)) { throw "Duplicate $Name value: $text" }
    }
    if ($set.Count -eq 0) { throw "$Name must not be empty." }
    $result = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in $set) { $result.Add($item) }
    return $result.ToArray()
}

function Assert-DialectProfileSelectionStringSet {
    param([object[]]$Actual, [object[]]$Expected, [Parameter(Mandatory = $true)][string]$Context)

    $actualValues = @(Get-DialectProfileSelectionSortedStrings -Values $Actual -Name "$Context actual values")
    $expectedValues = @(Get-DialectProfileSelectionSortedStrings -Values $Expected -Name "$Context expected values")
    if ($actualValues.Count -ne $expectedValues.Count) { throw "$Context count drifted: expected $($expectedValues.Count), actual $($actualValues.Count)." }
    for ($index = 0; $index -lt $expectedValues.Count; $index++) {
        if ($actualValues[$index] -cne $expectedValues[$index]) { throw "$Context value drifted: expected $($expectedValues[$index]), actual $($actualValues[$index])." }
    }
}

function Get-DialectProfileSelectionMethodBlock {
    param([Parameter(Mandatory = $true)][string]$Text, [Parameter(Mandatory = $true)][string]$Signature)

    $signatureIndex = $Text.IndexOf($Signature, [StringComparison]::Ordinal)
    if ($signatureIndex -lt 0) { throw "Required DIA-11 source method is missing: $Signature" }
    $openBrace = $Text.IndexOf('{', $signatureIndex)
    if ($openBrace -lt 0) { throw "Required DIA-11 source method has no opening brace: $Signature" }
    $depth = 0
    for ($index = $openBrace; $index -lt $Text.Length; $index++) {
        $character = $Text[$index]
        if ($character -eq '{') { $depth++ }
        elseif ($character -eq '}') {
            $depth--
            if ($depth -eq 0) { return $Text.Substring($signatureIndex, $index - $signatureIndex + 1) }
        }
    }
    throw "Required DIA-11 source method has no closing brace: $Signature"
}

function Get-DialectProfileSelectionQuotedTextFiles {
    param([Parameter(Mandatory = $true)][string]$MethodBlock, [Parameter(Mandatory = $true)][string]$Kind)

    $names = New-Object 'System.Collections.Generic.List[string]'
    foreach ($match in [regex]::Matches($MethodBlock, '"(?<name>[A-Za-z0-9_\-]+\.txt)"')) {
        $names.Add($match.Groups['name'].Value)
    }
    return @(Get-DialectProfileSelectionSortedStrings -Values $names.ToArray() -Name "$Kind marker file")
}

function Get-DialectProfileSelectionSourceCatalog {
    param([Parameter(Mandatory = $true)][object]$Catalog, [Parameter(Mandatory = $true)][string]$ProjectRoot)

    if ([string]$Catalog.schemaVersion -cne '1.0.0' -or
        [string]$Catalog.sourceWorkPackage -cne 'M0-DIA-11' -or
        [string]$Catalog.sourcePlanPreflightWorkPackage -cne 'M0-DIA-10' -or
        [string]$Catalog.catalogVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
        [string]::IsNullOrWhiteSpace([string]$Catalog.catalogId)) {
        throw 'Unsupported DIA-11 profile selection catalog.'
    }

    $project = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $byId = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($sourceFile in @($Catalog.sourceFiles)) {
        $id = [string]$sourceFile.id
        $path = [string]$sourceFile.path
        $expectedHash = [string]$sourceFile.sha256
        if ([string]::IsNullOrWhiteSpace($id) -or [string]::IsNullOrWhiteSpace($path)) { throw 'DIA-11 catalog source file is incomplete.' }
        if ($byId.ContainsKey($id)) { throw "Duplicate DIA-11 source file id: $id" }
        Assert-DialectProfileSelectionHash $expectedHash "catalog source file $id"
        $resolvedPath = [IO.Path]::GetFullPath((Join-Path $project $path))
        if (-not $resolvedPath.StartsWith($project, [StringComparison]::OrdinalIgnoreCase)) { throw "DIA-11 source path escapes project root: $path" }
        if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) { throw "DIA-11 source file is missing: $path" }
        $actualHash = Get-DialectProfileSelectionSha256Hex -Bytes ([IO.File]::ReadAllBytes($resolvedPath))
        if ($actualHash -cne $expectedHash) { throw "DIA-11 source hash drifted: $path" }
        $byId.Add($id, [pscustomobject][ordered]@{ id = $id; path = $path.Replace('\\', '/'); sha256 = $actualHash; fullPath = $resolvedPath })
    }
    foreach ($requiredId in @('FirstWindow', 'Program', 'LegacyRunnerConfig')) {
        if (-not $byId.ContainsKey($requiredId)) { throw "DIA-11 catalog is missing required source file: $requiredId" }
    }
    if ($byId.Count -ne 3) { throw 'DIA-11 catalog must bind exactly three profile-selection source files.' }

    $expected = $Catalog.expected
    if ($null -eq $expected -or [string]$expected.launcherFallbackProfileId -cne 'v24pure') { throw 'DIA-11 launcher fallback must remain v24pure.' }
    $legacyCoreProfileEnums = @(Get-DialectProfileSelectionSortedStrings -Values @($expected.legacyCoreProfileEnums) -Name 'DIA-11 expected legacy CoreProfile enum')
    $launcherProfileIds = @(Get-DialectProfileSelectionSortedStrings -Values @($expected.launcherProfileIds) -Name 'DIA-11 expected launcher profile id')
    $modernMarkerFileNames = @(Get-DialectProfileSelectionSortedStrings -Values @($expected.modernMarkerFileNames) -Name 'DIA-11 expected modern marker file')
    $legacyMarkerFileNames = @(Get-DialectProfileSelectionSortedStrings -Values @($expected.legacyMarkerFileNames) -Name 'DIA-11 expected legacy marker file')
    if (@($expected.selectionPrecedence).Count -ne 5) { throw 'DIA-11 catalog must define five DetectCoreProfile precedence rules.' }
    $precedence = New-Object 'System.Collections.Generic.SortedDictionary[int,object]'
    foreach ($rule in @($expected.selectionPrecedence)) {
        $order = [int]$rule.order
        if ($order -lt 1 -or $precedence.ContainsKey($order) -or [string]::IsNullOrWhiteSpace([string]$rule.inputKind) -or [string]::IsNullOrWhiteSpace([string]$rule.outputLegacyCoreProfileEnum)) {
            throw 'Invalid DIA-11 catalog selection precedence rule.'
        }
        $precedence.Add($order, [pscustomobject][ordered]@{ order = $order; inputKind = [string]$rule.inputKind; outputLegacyCoreProfileEnum = [string]$rule.outputLegacyCoreProfileEnum })
    }
    if ((@($precedence.Keys) -join ',') -cne '1,2,3,4,5') { throw 'DIA-11 catalog selection precedence order is incomplete.' }

    $canonicalSources = New-Object 'System.Collections.Generic.List[object]'
    foreach ($source in $byId.Values | Sort-Object path) { $canonicalSources.Add([ordered]@{ id = $source.id; path = $source.path; sha256 = $source.sha256 }) }
    $canonicalCatalog = [ordered]@{
        schemaVersion = '1.0.0'
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        sourceWorkPackage = 'M0-DIA-11'
        sourcePlanPreflightWorkPackage = 'M0-DIA-10'
        sourceFiles = $canonicalSources.ToArray()
        expected = [ordered]@{
            legacyCoreProfileEnums = @($legacyCoreProfileEnums)
            launcherProfileIds = @($launcherProfileIds)
            launcherFallbackProfileId = 'v24pure'
            modernMarkerFileNames = @($modernMarkerFileNames)
            legacyMarkerFileNames = @($legacyMarkerFileNames)
            selectionPrecedence = @($precedence.Values)
        }
    }
    return [pscustomobject][ordered]@{
        catalogId = [string]$Catalog.catalogId
        catalogVersion = [string]$Catalog.catalogVersion
        catalogHash = Get-DialectProfileSelectionCanonicalHash -Value $canonicalCatalog
        sourceById = $byId
        legacyCoreProfileEnums = $legacyCoreProfileEnums
        launcherProfileIds = $launcherProfileIds
        launcherFallbackProfileId = 'v24pure'
        modernMarkerFileNames = $modernMarkerFileNames
        legacyMarkerFileNames = $legacyMarkerFileNames
        selectionPrecedence = @($precedence.Values)
    }
}

function Assert-DialectProfileSelectionPlanPreflight {
    param([Parameter(Mandatory = $true)][object]$PlanPreflight)

    if ([string]$PlanPreflight.schemaVersion -cne '1.0.0' -or [string]$PlanPreflight.workPackage -cne 'M0-DIA-10') { throw 'DIA-11 requires M0-DIA-10 plan preflight evidence.' }
    Assert-DialectProfileSelectionHash $PlanPreflight.preflightSetHash 'M0-DIA-10 preflightSetHash'
    if ([string]$PlanPreflight.executionStatus -cne 'InProgress' -or [string]$PlanPreflight.gateStatus -cne 'Blocked' -or [string]$PlanPreflight.blockerCode -cne 'EvidenceMissing' -or [string]$PlanPreflight.result -cne 'Partial') { throw 'DIA-10 evidence status was incorrectly advanced.' }
    if ([string]$PlanPreflight.currentRuntimeIsolation.status -cne 'Failed') { throw 'DIA-10 current runtime isolation must remain Failed.' }
    if ([string]$PlanPreflight.parserVmConsumption.status -cne 'NotConsumed') { throw 'DIA-10 parser/VM consumption must remain NotConsumed.' }
    if ([string]$PlanPreflight.compatibilityPlanRuntime.status -cne 'NotImplemented') { throw 'DIA-10 runtime compatibility plan status must remain NotImplemented.' }
    if ([string]$PlanPreflight.m1Eligibility.status -cne 'Blocked') { throw 'DIA-10 M1 eligibility must remain Blocked.' }
    $profiles = New-Object 'System.Collections.Generic.SortedDictionary[string,object]' ([StringComparer]::Ordinal)
    foreach ($profile in @($PlanPreflight.profiles)) {
        $profileId = [string]$profile.profileId
        if ([string]::IsNullOrWhiteSpace($profileId) -or $profiles.ContainsKey($profileId)) { throw "Invalid DIA-10 profile evidence: $profileId" }
        $profiles.Add($profileId, $profile)
    }
    if ($profiles.Count -ne 2 -or -not $profiles.ContainsKey('v24pure') -or -not $profiles.ContainsKey('snake')) { throw 'DIA-10 profile evidence must contain exactly v24pure and snake.' }
    if ([string]$profiles['v24pure'].legacyCoreProfileEnum -cne 'V24Pure' -or [string]$profiles['snake'].legacyCoreProfileEnum -cne 'Snake') { throw 'DIA-10 profile evidence mapping drifted.' }
    $modern = @($PlanPreflight.unsupportedLegacyCoreProfiles | Where-Object { $_.legacyCoreProfileEnum -ceq 'SnakeModernMobile' -and $_.status -ceq 'Uncovered' })
    if ($modern.Count -ne 1) { throw 'DIA-10 must keep SnakeModernMobile Uncovered.' }
    return $profiles
}

function New-DialectProfileSelectionReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, Position = 0)][object]$PlanPreflight,
        [Parameter(Mandatory = $true, Position = 1)][object]$Catalog,
        [Parameter(Mandatory = $true, Position = 2)][string]$ProjectRoot,
        [string]$OutputPath = ''
    )

    $preflightProfiles = Assert-DialectProfileSelectionPlanPreflight -PlanPreflight $PlanPreflight
    $catalogContext = Get-DialectProfileSelectionSourceCatalog -Catalog $Catalog -ProjectRoot $ProjectRoot
    $firstWindowText = [IO.File]::ReadAllText($catalogContext.sourceById['FirstWindow'].fullPath, [Text.Encoding]::UTF8)
    $programText = [IO.File]::ReadAllText($catalogContext.sourceById['Program'].fullPath, [Text.Encoding]::UTF8)
    $runnerText = [IO.File]::ReadAllText($catalogContext.sourceById['LegacyRunnerConfig'].fullPath, [Text.Encoding]::UTF8)

    $enumMatch = [regex]::Match($programText, 'public\s+enum\s+EmueraCoreProfile\s*\{(?<body>[^}]*)\}', [Text.RegularExpressions.RegexOptions]::Singleline)
    if (-not $enumMatch.Success) { throw 'DIA-11 could not locate EmueraCoreProfile enum.' }
    $enumNames = New-Object 'System.Collections.Generic.List[string]'
    foreach ($match in [regex]::Matches($enumMatch.Groups['body'].Value, '^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*,?', [Text.RegularExpressions.RegexOptions]::Multiline)) { $enumNames.Add($match.Groups['name'].Value) }
    $enumNames = @(Get-DialectProfileSelectionSortedStrings -Values $enumNames.ToArray() -Name 'EmueraCoreProfile enum')
    Assert-DialectProfileSelectionStringSet -Actual $enumNames -Expected @($catalogContext.legacyCoreProfileEnums) -Context 'DIA-11 legacy CoreProfile enum set'

    $v24Constant = [regex]::Match($firstWindowText, 'public\s+const\s+string\s+CoreProfileV24Pure\s*=\s*"(?<value>[^"]+)"\s*;')
    $snakeConstant = [regex]::Match($firstWindowText, 'public\s+const\s+string\s+CoreProfileSnake\s*=\s*"(?<value>[^"]+)"\s*;')
    if (-not $v24Constant.Success -or -not $snakeConstant.Success) { throw 'DIA-11 launcher CoreProfile constants are missing.' }
    $launcherProfileIds = @(Get-DialectProfileSelectionSortedStrings -Values @($v24Constant.Groups['value'].Value, $snakeConstant.Groups['value'].Value) -Name 'FirstWindow launcher profile id')
    Assert-DialectProfileSelectionStringSet -Actual $launcherProfileIds -Expected @($catalogContext.launcherProfileIds) -Context 'DIA-11 launcher profile id set'
    $normalizerBlock = Get-DialectProfileSelectionMethodBlock -Text $firstWindowText -Signature 'static string NormalizeCoreProfileName'
    if ($normalizerBlock.IndexOf('CoreProfileSnake', [StringComparison]::Ordinal) -lt 0 -or $normalizerBlock.IndexOf('OrdinalIgnoreCase', [StringComparison]::Ordinal) -lt 0 -or $normalizerBlock.IndexOf('return CoreProfileV24Pure;', [StringComparison]::Ordinal) -lt 0) { throw 'DIA-11 launcher profile normalizer contract drifted.' }

    $detectBlock = Get-DialectProfileSelectionMethodBlock -Text $programText -Signature 'private static EmueraCoreProfile DetectCoreProfile'
    $emptyIndex = $detectBlock.IndexOf('string.IsNullOrEmpty(exeDir)', [StringComparison]::Ordinal)
    $launcherIndex = $detectBlock.IndexOf('FirstWindow.SelectedCoreProfileName', [StringComparison]::Ordinal)
    $modernIndex = $detectBlock.IndexOf('IsModernSnakeCoreRequested(exeDir)', [StringComparison]::Ordinal)
    $legacyIndex = $detectBlock.IndexOf('IsLegacySnakeCoreRequested(exeDir)', [StringComparison]::Ordinal)
    $defaultIndex = $detectBlock.LastIndexOf('return EmueraCoreProfile.V24Pure;', [StringComparison]::Ordinal)
    if ($emptyIndex -lt 0 -or $launcherIndex -lt 0 -or $modernIndex -lt 0 -or $legacyIndex -lt 0 -or $defaultIndex -lt 0 -or -not ($emptyIndex -lt $launcherIndex -and $launcherIndex -lt $modernIndex -and $modernIndex -lt $legacyIndex -and $legacyIndex -lt $defaultIndex)) { throw 'DIA-11 DetectCoreProfile precedence drifted.' }
    if ($detectBlock.IndexOf('StringComparison.OrdinalIgnoreCase', [StringComparison]::Ordinal) -lt 0 -or $detectBlock.IndexOf('return EmueraCoreProfile.Snake;', $launcherIndex) -lt 0) { throw 'DIA-11 launcher Snake selection contract drifted.' }

    $modernMarkers = Get-DialectProfileSelectionQuotedTextFiles -MethodBlock (Get-DialectProfileSelectionMethodBlock -Text $programText -Signature 'private static bool IsModernSnakeCoreRequested') -Kind 'modern'
    $legacyMarkers = Get-DialectProfileSelectionQuotedTextFiles -MethodBlock (Get-DialectProfileSelectionMethodBlock -Text $programText -Signature 'private static bool IsLegacySnakeCoreRequested') -Kind 'legacy'
    Assert-DialectProfileSelectionStringSet -Actual $modernMarkers -Expected @($catalogContext.modernMarkerFileNames) -Context 'DIA-11 modern marker file set'
    Assert-DialectProfileSelectionStringSet -Actual $legacyMarkers -Expected @($catalogContext.legacyMarkerFileNames) -Context 'DIA-11 legacy marker file set'
    if ($runnerText.IndexOf('profile_must_be_v24pure_or_snake', [StringComparison]::Ordinal) -lt 0 -or $runnerText.IndexOf('CoreProfileV24Pure', [StringComparison]::Ordinal) -lt 0 -or $runnerText.IndexOf('CoreProfileSnake', [StringComparison]::Ordinal) -lt 0) { throw 'DIA-11 legacy runner profile contract drifted.' }

    $selectionPrecedence = @(
        [pscustomobject][ordered]@{ order = 1; inputKind = 'EmptyExeDir'; outputLegacyCoreProfileEnum = 'V24Pure'; source = 'Program.DetectCoreProfile' },
        [pscustomobject][ordered]@{ order = 2; inputKind = 'LauncherProfileSnake'; outputLegacyCoreProfileEnum = 'Snake'; source = 'FirstWindow.SelectedCoreProfileName' },
        [pscustomobject][ordered]@{ order = 3; inputKind = 'ModernMarker'; outputLegacyCoreProfileEnum = 'SnakeModernMobile'; source = 'Program.IsModernSnakeCoreRequested' },
        [pscustomobject][ordered]@{ order = 4; inputKind = 'LegacyMarker'; outputLegacyCoreProfileEnum = 'Snake'; source = 'Program.IsLegacySnakeCoreRequested' },
        [pscustomobject][ordered]@{ order = 5; inputKind = 'Default'; outputLegacyCoreProfileEnum = 'V24Pure'; source = 'Program.DetectCoreProfile' }
    )
    $expectedPrecedence = @($catalogContext.selectionPrecedence | ForEach-Object { $_.inputKind + [char]0 + $_.outputLegacyCoreProfileEnum })
    $actualPrecedence = @($selectionPrecedence | ForEach-Object { $_.inputKind + [char]0 + $_.outputLegacyCoreProfileEnum })
    if (($actualPrecedence -join '|') -cne ($expectedPrecedence -join '|')) { throw 'DIA-11 selection precedence catalog drifted.' }

    $legacyCoreProfiles = @(
        [pscustomobject][ordered]@{ legacyCoreProfileEnum = 'Snake'; launcherProfileId = 'snake'; preflightEvidenceStatus = 'EvidenceBacked'; runnerProfileStatus = 'Supported'; selectionAvailability = 'LauncherOrLegacyMarker' },
        [pscustomobject][ordered]@{ legacyCoreProfileEnum = 'SnakeModernMobile'; launcherProfileId = ''; preflightEvidenceStatus = 'Uncovered'; runnerProfileStatus = 'Unsupported'; selectionAvailability = 'ModernMarkerOnly' },
        [pscustomobject][ordered]@{ legacyCoreProfileEnum = 'V24Pure'; launcherProfileId = 'v24pure'; preflightEvidenceStatus = 'EvidenceBacked'; runnerProfileStatus = 'Supported'; selectionAvailability = 'LauncherOrDefault' }
    )
    foreach ($profile in $legacyCoreProfiles) {
        if ($profile.preflightEvidenceStatus -eq 'EvidenceBacked') {
            $matching = @($preflightProfiles.Values | Where-Object legacyCoreProfileEnum -ceq $profile.legacyCoreProfileEnum)
            if ($matching.Count -ne 1) { throw "DIA-10 evidence is missing for launcher profile: $($profile.legacyCoreProfileEnum)" }
        }
    }

    $sourceIdentities = New-Object 'System.Collections.Generic.List[object]'
    foreach ($source in $catalogContext.sourceById.Values | Sort-Object path) { $sourceIdentities.Add([ordered]@{ id = $source.id; path = $source.path; sha256 = $source.sha256 }) }
    $currentRuntimeIsolation = [ordered]@{ status = 'Failed'; reason = 'CoreProfile remains process-wide legacy static state; DIA-11 only records source selection precedence.' }
    $parserVmConsumption = [ordered]@{ status = 'NotConsumed'; reason = 'DIA-11 is an offline source report and is not read by the legacy Parser or VM.' }
    $compatibilityPlanRuntime = [ordered]@{ status = 'NotImplemented'; reason = 'DIA-11 does not create a runtime resolver, CompatibilityPlan, session facade or frozen registry.' }
    $m1Eligibility = [ordered]@{ status = 'Blocked'; reason = 'M0-DIA-10/M0-SES-01 remain static evidence only; profile selection facts do not isolate sessions.' }
    $setPayload = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-11'
        sourcePlanPreflightHash = [string]$PlanPreflight.preflightSetHash
        catalogId = $catalogContext.catalogId
        catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash
        sourceIdentities = $sourceIdentities.ToArray()
        legacyCoreProfiles = $legacyCoreProfiles
        launcherProfileContract = [ordered]@{ supportedProfileIds = @($launcherProfileIds); unknownProfileFallback = 'v24pure'; normalizerComparison = 'OrdinalIgnoreCase for snake; all other values fall back to v24pure' }
        modernMarkerContract = [ordered]@{ markerFileNames = @($modernMarkers); outputLegacyCoreProfileEnum = 'SnakeModernMobile' }
        legacyMarkerContract = [ordered]@{ markerFileNames = @($legacyMarkers); outputLegacyCoreProfileEnum = 'Snake' }
        selectionPrecedence = $selectionPrecedence
        currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime
        m1Eligibility = $m1Eligibility
    }
    $selectionSetHash = Get-DialectProfileSelectionCanonicalHash -Value $setPayload -Depth 40
    $report = [ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-11'
        generatedAtUtc = [DateTime]::UtcNow.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourcePlanPreflightHash = [string]$PlanPreflight.preflightSetHash
        catalogId = $catalogContext.catalogId
        catalogVersion = $catalogContext.catalogVersion
        catalogHash = $catalogContext.catalogHash
        selectionSetHash = $selectionSetHash
        sourceIdentities = $sourceIdentities.ToArray()
        legacyCoreProfiles = $legacyCoreProfiles
        launcherProfileContract = [ordered]@{ supportedProfileIds = @($launcherProfileIds); unknownProfileFallback = 'v24pure'; normalizerComparison = 'OrdinalIgnoreCase for snake; all other values fall back to v24pure' }
        modernMarkerContract = [ordered]@{ markerFileNames = @($modernMarkers); outputLegacyCoreProfileEnum = 'SnakeModernMobile' }
        legacyMarkerContract = [ordered]@{ markerFileNames = @($legacyMarkers); outputLegacyCoreProfileEnum = 'Snake' }
        selectionPrecedence = $selectionPrecedence
        currentRuntimeIsolation = $currentRuntimeIsolation
        parserVmConsumption = $parserVmConsumption
        compatibilityPlanRuntime = $compatibilityPlanRuntime
        m1Eligibility = $m1Eligibility
        uncovered = @(
            'DIA-11 proves only the current source-level selection order and marker names; it does not inspect any game directory or execute a resolver.',
            'SnakeModernMobile is selected only by a legacy marker path in current source and remains Uncovered by the DIA-10 v24/Snake static registry evidence.',
            'No runtime CompatibilityPlan, LegacySessionFacade, feature flag, parser/VM consumption, D2 frozen registry, game run, APK or device evidence is introduced.'
        )
    }
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutput)) | Out-Null
        [IO.File]::WriteAllText($fullOutput, (($report | ConvertTo-Json -Depth 40) + "`n"), $script:DialectProfileSelectionUtf8NoBom)
    }
    return [pscustomobject]$report
}

Export-ModuleMember -Function New-DialectProfileSelectionReport
