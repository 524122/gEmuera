Set-StrictMode -Version Latest

$script:SchemaVersion = '1.0.0'
$script:WorkPackage = 'M0-SAV-01'
$script:AuditId = 'gemuera.m0.legacy-save-fixture-audit'
$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)

function Get-LegacySaveFixtureSha256Text {
    param([AllowEmptyString()][string]$Value)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($script:Utf8NoBom.GetBytes($Value)))).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-LegacySaveFixtureRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )
    $rootUri = New-Object Uri(((Resolve-Path -LiteralPath $Root).Path.TrimEnd('\') + '\'))
    $pathUri = New-Object Uri((Resolve-Path -LiteralPath $Path).Path)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Get-LegacySaveFixtureTypeCandidate {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    $name = [IO.Path]::GetFileName($RelativePath)
    if ($name -match '^global\.sav$') { return 'Global' }
    if ($name -match '^save[^/\\]*\.sav$') { return 'Normal' }
    if ($name -match '^var_.+\.dat$') { return 'Var' }
    if ($name -match '^chara_.+\.dat$') { return 'CharVar' }
    return 'Unknown'
}

function Get-LegacySaveFixtureCandidateFiles {
    param([Parameter(Mandatory = $true)][string]$Root)
    Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction SilentlyContinue | Where-Object {
        $_.Extension -ieq '.sav' -or
        ($_.Extension -ieq '.dat' -and $_.Name -match '^(var|chara)_.+\.dat$' -and $_.Directory.Name -ieq 'sav')
    }
}

function Read-LegacySaveFixtureFile {
    param([Parameter(Mandatory = $true)][IO.FileInfo]$File)

    $ordinaryHeader = '894552410D0A1A0A'
    $zipHeader = '894552415A49500A'
    $prefix = New-Object byte[] 16
    $prefixRead = 0
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::Open($File.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    [int64]$bytesRead = 0
    try {
        $buffer = New-Object byte[] 65536
        while (($count = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $sha.TransformBlock($buffer, 0, $count, $buffer, 0) | Out-Null
            if ($prefixRead -lt $prefix.Length) {
                $copy = [Math]::Min($prefix.Length - $prefixRead, $count)
                [Array]::Copy($buffer, 0, $prefix, $prefixRead, $copy)
                $prefixRead += $copy
            }
            $bytesRead += $count
        }
        $sha.TransformFinalBlock([byte[]]::new(0), 0, 0) | Out-Null
    }
    finally {
        $stream.Dispose()
    }

    $sha256 = ([BitConverter]::ToString($sha.Hash)).Replace('-', '').ToLowerInvariant()
    $sha.Dispose()
    $prefixHex = if ($prefixRead -eq 0) { '' } else { ([BitConverter]::ToString($prefix, 0, $prefixRead)).Replace('-', '') }
    $magicHex = if ($prefixRead -ge 8) { $prefixHex.Substring(0, 16) } else { $prefixHex }
    $headerKind = switch ($magicHex) {
        $ordinaryHeader { 'Ordinary1808' }
        $zipHeader { 'GZip1808' }
        default { 'Unknown' }
    }
    $formatStatus = if ($bytesRead -lt 16) { 'Truncated' } elseif ($headerKind -eq 'Unknown') { 'Unrecognized' } else { 'HeaderOnly' }
    $version = $null
    $dataCount = $null
    if ($prefixRead -ge 12 -and $headerKind -ne 'Unknown') { $version = [BitConverter]::ToUInt32($prefix, 8) }
    if ($prefixRead -ge 16 -and $headerKind -ne 'Unknown') { $dataCount = [BitConverter]::ToUInt32($prefix, 12) }

    return [ordered]@{
        bytes = $bytesRead
        sha256 = $sha256
        magicHex = $magicHex
        headerKind = $headerKind
        formatStatus = $formatStatus
        version = $version
        dataCount = $dataCount
        offsets = [ordered]@{
            header = [ordered]@{ offset = 0; length = 8; valueHex = if ($prefixRead -ge 8) { $prefixHex.Substring(0, 16) } else { '' } }
            version = [ordered]@{ offset = 8; length = 4; value = $version }
            dataCount = [ordered]@{ offset = 12; length = 4; value = $dataCount }
            payload = [ordered]@{ offset = if ($headerKind -eq 'Unknown') { $null } else { 16 }; length = if ($headerKind -ne 'Unknown' -and $bytesRead -ge 16) { $bytesRead - 16 } else { 0 } }
        }
    }
}

function New-LegacySaveFixtureAuditReport {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][string]$RootPath)

    if (-not (Test-Path -LiteralPath $RootPath -PathType Container)) { throw "M0-SAV-01 fixture root does not exist: $RootPath" }
    $resolvedRoot = (Resolve-Path -LiteralPath $RootPath).Path
    $files = @(Get-LegacySaveFixtureCandidateFiles -Root $resolvedRoot | Sort-Object FullName)
    $fixtures = New-Object System.Collections.Generic.List[object]
    foreach ($file in $files) {
        $relative = Get-LegacySaveFixtureRelativePath -Root $resolvedRoot -Path $file.FullName
        $wire = Read-LegacySaveFixtureFile -File $file
        $fixtures.Add([ordered]@{
            path = $relative
            fileTypeCandidate = Get-LegacySaveFixtureTypeCandidate -RelativePath $relative
            extension = $file.Extension.ToLowerInvariant()
            readOnly = $true
            wire = $wire
        })
    }
    $byPath = @{}
    foreach ($fixture in $fixtures) { $byPath[[string]$fixture.path] = $fixture }
    [string[]]$orderedPaths = @($byPath.Keys)
    [Array]::Sort($orderedPaths, [StringComparer]::Ordinal)
    $orderedFixtures = foreach ($path in $orderedPaths) { $byPath[$path] }
    $canonicalLines = foreach ($fixture in $orderedFixtures) {
        "{0}`t{1}`t{2}`t{3}`t{4}" -f $fixture.path, $fixture.wire.bytes, $fixture.wire.sha256, $fixture.wire.headerKind, $fixture.wire.formatStatus
    }
    $binary = @($orderedFixtures | Where-Object { $_.wire.headerKind -ne 'Unknown' -and $_.wire.formatStatus -eq 'HeaderOnly' })
    $uncovered = New-Object System.Collections.Generic.List[string]
    $uncovered.Add('No profile-specific parser or decompressor was run; wire evidence is header-only.')
    $uncovered.Add('No isolated copy was written and no read/write round-trip was attempted.')
    if ($binary.Count -eq 0) { $uncovered.Add('No binary 1808 header was observed in the selected fixture root.') }

    return [ordered]@{
        schemaVersion = $script:SchemaVersion
        workPackage = $script:WorkPackage
        auditId = $script:AuditId
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = 'Partial'
        sourceRoot = $resolvedRoot.Replace('\', '/')
        sourceReadOnly = $true
        candidatePolicy = 'All .sav files; .dat only when named var_* or chara_* under a sav directory; no source writes.'
        sourceSaveFilesRead = $orderedFixtures.Count
        binaryHeaderCount = $binary.Count
        offsetMapStatus = if ($binary.Count -gt 0) { 'HeaderOnly' } else { 'Uncovered' }
        roundTripStatus = 'Uncovered'
        profileBindingStatus = 'Unbound'
        fixtureSetHash = Get-LegacySaveFixtureSha256Text -Value ([string]::Join("`n", [string[]]$canonicalLines))
        fixtures = $orderedFixtures
        uncovered = $uncovered.ToArray()
    }
}

function Write-LegacySaveFixtureAuditJson {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Path
    )
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 30) + "`n"), $script:Utf8NoBom)
}

Export-ModuleMember -Function New-LegacySaveFixtureAuditReport, Write-LegacySaveFixtureAuditJson
