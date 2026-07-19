Set-StrictMode -Version Latest

$script:SchemaVersion = '1.0.0'
$script:WorkPackage = 'M0-FIX-01'
$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)
$script:AllowedLayers = @('upstream', 'legacy', 'game')
$script:AllowedAuthorization = @('Verified', 'Unverified', 'MissingEvidence')

function Get-FixtureNormalizedPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    return [IO.Path]::GetFullPath($Path).Replace('\', '/')
}

function Get-FixtureSha256File {
    param([Parameter(Mandatory = $true)][string]$Path)
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

function Get-FixtureSha256Text {
    param([AllowEmptyString()][string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($script:Utf8NoBom.GetBytes($Value)))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-FixtureRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $rootUri = New-Object Uri(((Get-FixtureNormalizedPath -Path $Root).TrimEnd('/') + '/'))
    $pathUri = New-Object Uri((Get-FixtureNormalizedPath -Path $Path))
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Test-FixturePathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )
    $candidate = (Get-FixtureNormalizedPath -Path $Path).TrimEnd('/') + '/'
    $rootPath = (Get-FixtureNormalizedPath -Path $Root).TrimEnd('/') + '/'
    return $candidate.StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)
}

function Test-FixtureSourcePathExcluded {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    $path = $RelativePath.Replace('\', '/')
    if ($path -match '(^|/)(\.git|\.godot|\.codegraph|\.agents|\.claude|\.trae|\.vs|\.vscode|\.idea|\.mono|node_modules|action_maps|artifacts|android|export|bin|obj)(/|$)') {
        return $true
    }
    if ($path -match '(^|/)(Thumbs\.db|desktop\.ini)$' -or $path -match '\.(user|suo|apk|aab|idsig)$') {
        return $true
    }
    return $false
}

function New-FixtureContentManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Root,
        [switch]$ApplySourceExclusions
    )

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "Fixture root does not exist: $Root"
    }
    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $files = @(
        Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force |
            Where-Object {
                $relative = Get-FixtureRelativePath -Root $resolvedRoot -Path $_.FullName
                -not $ApplySourceExclusions -or -not (Test-FixtureSourcePathExcluded -RelativePath $relative)
            }
    )

    $byPath = New-Object 'System.Collections.Generic.Dictionary[string,System.IO.FileInfo]' ([StringComparer]::Ordinal)
    foreach ($file in $files) {
        $relative = Get-FixtureRelativePath -Root $resolvedRoot -Path $file.FullName
        if ($relative.StartsWith('../', [StringComparison]::Ordinal) -or [IO.Path]::IsPathRooted($relative)) {
            throw "Fixture entry escaped its root: $($file.FullName)"
        }
        $byPath.Add($relative, $file)
    }

    [string[]]$paths = @($byPath.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $entries = New-Object System.Collections.Generic.List[object]
    [int64]$totalBytes = 0
    foreach ($relative in $paths) {
        $file = $byPath[$relative]
        $entry = [ordered]@{
            path = $relative
            bytes = [int64]$file.Length
            sha256 = Get-FixtureSha256File -Path $file.FullName
        }
        $entries.Add($entry)
        $totalBytes += [int64]$file.Length
    }

    $canonicalLines = foreach ($entry in $entries) {
        "{0}:{1}`t{2}`t{3}" -f $script:Utf8NoBom.GetByteCount($entry.path), $entry.path, $entry.bytes, $entry.sha256
    }
    return [ordered]@{
        schemaVersion = $script:SchemaVersion
        label = $Label
        root = Get-FixtureNormalizedPath -Path $resolvedRoot
        exclusionPolicy = if ($ApplySourceExclusions) { 'M0-FIX-01-source-v1' } else { 'None' }
        fileCount = $entries.Count
        totalBytes = $totalBytes
        canonicalSha256 = Get-FixtureSha256Text -Value ([string]::Join("`n", [string[]]$canonicalLines))
        entries = $entries
    }
}

function Write-FixtureJson {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$Depth = 30
    )
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth $Depth) + "`n"), $script:Utf8NoBom)
}

function Test-FixtureRelativeReportPath {
    param([Parameter(Mandatory = $true)][string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) {
        return $false
    }
    $normalized = $Path.Replace('\', '/')
    return -not ($normalized -eq '..' -or $normalized.StartsWith('../', [StringComparison]::Ordinal) -or $normalized.Contains('/../'))
}

function Assert-FixtureCatalog {
    param([Parameter(Mandatory = $true)]$Catalog)
    if ($Catalog.schemaVersion -ne $script:SchemaVersion) {
        throw 'unsupported_fixture_catalog_schema_version'
    }
    if ($Catalog.workPackage -ne $script:WorkPackage) {
        throw 'fixture_catalog_work_package_must_be_M0-FIX-01'
    }
    if ($null -eq $Catalog.fixtures -or @($Catalog.fixtures).Count -eq 0) {
        throw 'fixture_catalog_must_have_entries'
    }

    $ids = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::Ordinal)
    foreach ($fixture in @($Catalog.fixtures)) {
        if ([string]::IsNullOrWhiteSpace([string]$fixture.id) -or $fixture.id -notmatch '^(UP|GE|SN|FL|AN)-[A-Z0-9][A-Z0-9-]*$') {
            throw "invalid_fixture_id: $($fixture.id)"
        }
        if (-not $ids.Add([string]$fixture.id)) {
            throw "duplicate_fixture_id: $($fixture.id)"
        }
        if ($script:AllowedLayers -notcontains [string]$fixture.layer) {
            throw "invalid_fixture_layer: $($fixture.id)"
        }
        foreach ($field in @('category', 'profile', 'rootBinding')) {
            if ([string]::IsNullOrWhiteSpace([string]$fixture.$field)) {
                throw "missing_fixture_field: $($fixture.id).$field"
            }
        }
        if ($null -eq $fixture.source -or [string]::IsNullOrWhiteSpace([string]$fixture.source.origin)) {
            throw "missing_fixture_source_origin: $($fixture.id)"
        }
        if ($null -eq $fixture.authorization -or $script:AllowedAuthorization -notcontains [string]$fixture.authorization.status) {
            throw "invalid_fixture_authorization: $($fixture.id)"
        }
        if ($fixture.authorization.status -ne 'Verified' -and [bool]$fixture.authorization.redistributionAllowed) {
            throw "unverified_fixture_cannot_allow_redistribution: $($fixture.id)"
        }
        foreach ($report in @($fixture.expectedReports)) {
            if ([string]::IsNullOrWhiteSpace([string]$report.kind) -or -not (Test-FixtureRelativeReportPath -Path ([string]$report.path))) {
                throw "invalid_expected_report: $($fixture.id)"
            }
        }
    }
}

function Resolve-FixtureAuthorization {
    param(
        [Parameter(Mandatory = $true)]$Declaration,
        [string]$ResolvedRoot,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Uncovered
    )
    $status = [string]$Declaration.status
    $redistributionAllowed = [bool]$Declaration.redistributionAllowed
    $evidence = [string]$Declaration.evidence
    $evidenceStatus = if ([string]::IsNullOrWhiteSpace($evidence)) { 'Uncovered' } else { 'Declared' }
    $evidenceSha256 = ''

    if ($evidence.StartsWith('root://', [StringComparison]::OrdinalIgnoreCase)) {
        if ([string]::IsNullOrWhiteSpace($ResolvedRoot)) {
            $evidenceStatus = 'Uncovered'
            $Uncovered.Add('authorization_evidence_root_missing')
            if ($status -eq 'Verified') {
                $status = 'MissingEvidence'
                $redistributionAllowed = $false
            }
        }
        else {
            $relative = $evidence.Substring('root://'.Length).Replace('/', [IO.Path]::DirectorySeparatorChar)
            if (-not (Test-FixtureRelativeReportPath -Path $relative)) {
                throw "invalid_authorization_evidence_path: $evidence"
            }
            $evidencePath = Join-Path $ResolvedRoot $relative
            if (Test-Path -LiteralPath $evidencePath -PathType Leaf) {
                $evidenceStatus = 'Captured'
                $evidenceSha256 = Get-FixtureSha256File -Path $evidencePath
            }
            else {
                $evidenceStatus = 'Uncovered'
                $Uncovered.Add('authorization_evidence_file_missing')
                if ($status -eq 'Verified') {
                    $status = 'MissingEvidence'
                    $redistributionAllowed = $false
                }
            }
        }
    }
    if ($status -ne 'Verified') {
        $redistributionAllowed = $false
        $Uncovered.Add('authorization_not_verified')
    }

    return [ordered]@{
        status = $status
        origin = [string]$Declaration.origin
        license = [string]$Declaration.license
        evidence = $evidence
        evidenceStatus = $evidenceStatus
        evidenceSha256 = $evidenceSha256
        redistributionAllowed = $redistributionAllowed
    }
}

function Invoke-FixtureManifest {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$CatalogPath,
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [hashtable]$RootBindings = @{},
        [hashtable]$ReportBindings = @{}
    )

    if (-not (Test-Path -LiteralPath $CatalogPath -PathType Leaf)) {
        throw "Fixture catalog does not exist: $CatalogPath"
    }
    $catalogResolved = (Resolve-Path -LiteralPath $CatalogPath).Path
    $catalog = Get-Content -LiteralPath $catalogResolved -Raw | ConvertFrom-Json
    Assert-FixtureCatalog -Catalog $catalog

    $outputFull = [IO.Path]::GetFullPath($OutputDirectory)
    if (Test-Path -LiteralPath $outputFull) {
        if ((Get-ChildItem -LiteralPath $outputFull -Force | Select-Object -First 1)) {
            throw "Output directory must be absent or empty: $outputFull"
        }
    }
    foreach ($binding in $RootBindings.GetEnumerator()) {
        if (-not [string]::IsNullOrWhiteSpace([string]$binding.Value) -and
            (Test-Path -LiteralPath ([string]$binding.Value) -PathType Container) -and
            (Test-FixturePathWithinRoot -Path $outputFull -Root ([string]$binding.Value))) {
            throw "Output directory must not be inside fixture root '$($binding.Key)'."
        }
    }
    [IO.Directory]::CreateDirectory($outputFull) | Out-Null

    $resolvedFixtures = New-Object System.Collections.Generic.List[object]
    $allUncovered = New-Object System.Collections.Generic.List[string]
    foreach ($fixture in @($catalog.fixtures)) {
        $fixtureUncovered = New-Object System.Collections.Generic.List[string]
        $bindingKey = [string]$fixture.rootBinding
        $boundRoot = if ($RootBindings.ContainsKey($bindingKey)) { [string]$RootBindings[$bindingKey] } else { '' }
        $resolvedRoot = ''
        $availability = 'Uncovered'
        $content = $null

        if ([string]::IsNullOrWhiteSpace($boundRoot)) {
            $fixtureUncovered.Add('fixture_root_binding_missing')
        }
        elseif (-not (Test-Path -LiteralPath $boundRoot -PathType Container)) {
            $fixtureUncovered.Add('fixture_root_not_found')
        }
        else {
            $resolvedRoot = (Resolve-Path -LiteralPath $boundRoot).Path
            $applySourceExclusions = $fixture.layer -eq 'upstream' -or $fixture.layer -eq 'legacy'
            $fileManifest = New-FixtureContentManifest -Label ([string]$fixture.id) -Root $resolvedRoot -ApplySourceExclusions:$applySourceExclusions
            $fileName = ([string]$fixture.id) + '.files.json'
            $filePath = Join-Path $outputFull $fileName
            Write-FixtureJson -Value $fileManifest -Path $filePath
            $content = [ordered]@{
                manifestPath = $fileName
                documentSha256 = Get-FixtureSha256File -Path $filePath
                canonicalSha256 = $fileManifest.canonicalSha256
                fileCount = $fileManifest.fileCount
                totalBytes = $fileManifest.totalBytes
                exclusionPolicy = $fileManifest.exclusionPolicy
            }
            $availability = 'Captured'
        }

        $authorization = Resolve-FixtureAuthorization -Declaration $fixture.authorization -ResolvedRoot $resolvedRoot -Uncovered $fixtureUncovered
        $reportRoot = if ($ReportBindings.ContainsKey($bindingKey)) { [string]$ReportBindings[$bindingKey] } else { '' }
        $resolvedReports = New-Object System.Collections.Generic.List[object]
        foreach ($expected in @($fixture.expectedReports)) {
            $reportStatus = 'Uncovered'
            $reportSha = ''
            $reportBytes = 0
            $reportSource = ''
            if (-not [string]::IsNullOrWhiteSpace($reportRoot) -and (Test-Path -LiteralPath $reportRoot -PathType Container)) {
                $candidate = Join-Path $reportRoot ([string]$expected.path)
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    $reportStatus = 'Captured'
                    $reportSha = Get-FixtureSha256File -Path $candidate
                    $reportBytes = [int64](Get-Item -LiteralPath $candidate).Length
                    $reportSource = Get-FixtureNormalizedPath -Path $candidate
                }
            }
            if ($reportStatus -eq 'Uncovered' -and [bool]$expected.required) {
                $fixtureUncovered.Add('expected_report_missing:' + [string]$expected.kind)
            }
            $resolvedReports.Add([ordered]@{
                kind = [string]$expected.kind
                path = ([string]$expected.path).Replace('\', '/')
                required = [bool]$expected.required
                status = $reportStatus
                source = $reportSource
                bytes = $reportBytes
                sha256 = $reportSha
            })
        }

        $evidenceStatus = if ($availability -eq 'Uncovered') {
            'Uncovered'
        }
        elseif ($fixtureUncovered.Count -gt 0) {
            'Partial'
        }
        else {
            'ReadyForReview'
        }
        foreach ($reason in $fixtureUncovered) {
            $allUncovered.Add(([string]$fixture.id) + ':' + $reason)
        }
        $resolvedFixtures.Add([ordered]@{
            id = [string]$fixture.id
            layer = [string]$fixture.layer
            category = [string]$fixture.category
            profile = [string]$fixture.profile
            rootBinding = $bindingKey
            availabilityStatus = $availability
            evidenceStatus = $evidenceStatus
            source = [ordered]@{
                name = [string]$fixture.source.name
                origin = [string]$fixture.source.origin
                revision = [string]$fixture.source.revision
                localRoot = if ($resolvedRoot) { Get-FixtureNormalizedPath -Path $resolvedRoot } else { '' }
            }
            authorization = $authorization
            content = $content
            expectedReports = $resolvedReports.ToArray()
            expectedCoverage = @($fixture.expectedCoverage)
            uncovered = $fixtureUncovered.ToArray()
            notes = [string]$fixture.notes
        })
    }

    $readyCount = @($resolvedFixtures | Where-Object { $_.evidenceStatus -eq 'ReadyForReview' }).Count
    $result = [ordered]@{
        schemaVersion = $script:SchemaVersion
        workPackage = $script:WorkPackage
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = if ($readyCount -eq $resolvedFixtures.Count) { 'ReadyForReview' } else { 'Partial' }
        catalog = [ordered]@{
            path = Get-FixtureNormalizedPath -Path $catalogResolved
            sha256 = Get-FixtureSha256File -Path $catalogResolved
        }
        preparedBy = [Environment]::UserName
        reviewedBy = ''
        approvedBy = ''
        gateDecision = 'NeedsEvidence'
        compatibility = @('None: fixture evidence metadata only')
        rollback = 'Remove M0-FIX-01 reports and revert the fixture catalog/tool; no runtime path is modified.'
        fixtureCount = $resolvedFixtures.Count
        readyForReviewCount = $readyCount
        uncovered = $allUncovered.ToArray()
        fixtures = $resolvedFixtures.ToArray()
    }
    Write-FixtureJson -Value $result -Path (Join-Path $outputFull 'fixture-manifest.json')
    return $result
}

Export-ModuleMember -Function New-FixtureContentManifest, Invoke-FixtureManifest
