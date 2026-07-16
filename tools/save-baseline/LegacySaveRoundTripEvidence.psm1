Set-StrictMode -Version Latest

$script:SchemaVersion = '1.0.0'
$script:WorkPackage = 'M0-SAV-01'
$script:AuditId = 'gemuera.m0.legacy-save-roundtrip-evidence'
$script:Utf8NoBom = New-Object Text.UTF8Encoding($false)
$script:Profiles = @('Upstream1808', 'GEmueraSnake')

function Get-RoundTripSha256File {
    param([Parameter(Mandatory = $true)][string]$Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}

function Get-RoundTripRelativePath {
    param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$Path)
    $rootUri = New-Object Uri(((Resolve-Path -LiteralPath $Root).Path.TrimEnd('\') + '\'))
    $pathUri = New-Object Uri((Resolve-Path -LiteralPath $Path).Path)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Test-RoundTripPathWithinRoot {
    param([Parameter(Mandatory = $true)][string]$Path, [Parameter(Mandatory = $true)][string]$Root)
    $pathFull = [IO.Path]::GetFullPath($Path).TrimEnd('\') + '\'
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd('\') + '\'
    return $pathFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)
}

function Get-RoundTripCandidates {
    param([Parameter(Mandatory = $true)][string]$Root)
    Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction SilentlyContinue | Where-Object {
        $_.Extension -ieq '.sav' -or
        ($_.Extension -ieq '.dat' -and $_.Name -match '^(var|chara)_.+\.dat$' -and $_.Directory.Name -ieq 'sav')
    }
}

function Read-RoundTripWire {
    param([Parameter(Mandatory = $true)][string]$Path)
    $fileInfo = Get-Item -LiteralPath $Path -Force
    $prefixBytes = New-Object byte[] 16
    $prefixLength = 0
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
    try {
        $prefixLength = $stream.Read($prefixBytes, 0, $prefixBytes.Length)
    }
    finally { $stream.Dispose() }
    $prefix = if ($prefixLength -gt 0) { [BitConverter]::ToString($prefixBytes, 0, $prefixLength).Replace('-', '') } else { '' }
    $magic = if ($prefixLength -ge 8) { $prefix.Substring(0, 16) } else { $prefix }
    $kind = switch ($magic) {
        '894552410D0A1A0A' { 'Ordinary1808' }
        '894552415A49500A' { 'GZip1808' }
        default { 'Unknown' }
    }
    $version = if ($prefixLength -ge 12 -and $kind -ne 'Unknown') { [BitConverter]::ToUInt32($prefixBytes, 8) } else { $null }
    $dataCount = if ($prefixLength -ge 16 -and $kind -ne 'Unknown') { [BitConverter]::ToUInt32($prefixBytes, 12) } else { $null }
    return [ordered]@{
        bytes = [int64]$fileInfo.Length
        sha256 = Get-RoundTripSha256File -Path $Path
        headerKind = $kind
        version = $version
        dataCount = $dataCount
        payloadOffset = if ($kind -ne 'Unknown' -and $fileInfo.Length -ge 16) { 16 } else { $null }
    }
}

function New-LegacySaveRoundTripEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$IsolatedCopyRoot,
        [Parameter(Mandatory = $true)][ValidateSet('Upstream1808', 'GEmueraSnake')][string]$ProfileId
    )
    if (-not (Test-Path -LiteralPath $RootPath -PathType Container)) { throw "M0-SAV-01 source root does not exist: $RootPath" }
    $root = (Resolve-Path -LiteralPath $RootPath).Path
    $isolated = [IO.Path]::GetFullPath($IsolatedCopyRoot)
    if (Test-RoundTripPathWithinRoot -Path $isolated -Root $root) { throw 'M0-SAV-01 isolated copy must be outside the source root.' }
    if (Test-Path -LiteralPath $isolated) {
        if ((Get-ChildItem -LiteralPath $isolated -Force | Select-Object -First 1)) { throw 'M0-SAV-01 isolated copy root must be absent or empty.' }
    }
    [IO.Directory]::CreateDirectory($isolated) | Out-Null

    $sourceFiles = @(Get-RoundTripCandidates -Root $root | Sort-Object FullName)
    $entries = New-Object System.Collections.Generic.List[object]
    $uncovered = New-Object System.Collections.Generic.List[string]
    $sourceMutated = $false
    foreach ($sourceFile in $sourceFiles) {
        $relative = Get-RoundTripRelativePath -Root $root -Path $sourceFile.FullName
        $before = Get-RoundTripSha256File -Path $sourceFile.FullName
        $wire = Read-RoundTripWire -Path $sourceFile.FullName
        $destination = Join-Path $isolated ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
        $roundTrip = Join-Path $isolated ('roundtrip-' + $relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
        $destinationParent = Split-Path -Parent $destination
        $roundTripParent = Split-Path -Parent $roundTrip
        [IO.Directory]::CreateDirectory($destinationParent) | Out-Null
        [IO.Directory]::CreateDirectory($roundTripParent) | Out-Null
        [IO.File]::Copy($sourceFile.FullName, $destination, $true)
        $isolatedHash = Get-RoundTripSha256File -Path $destination
        [IO.File]::Copy($destination, $roundTrip, $true)
        $roundTripHash = Get-RoundTripSha256File -Path $roundTrip
        $after = Get-RoundTripSha256File -Path $sourceFile.FullName
        if ($before -cne $after) { $sourceMutated = $true }
        $copyStatus = if ($before -ceq $isolatedHash -and $isolatedHash -ceq $roundTripHash) { 'Passed' } else { 'Failed' }
        if ($wire.headerKind -eq 'Unknown') { $uncovered.Add($relative + ':unrecognized_wire') }
        $entries.Add([ordered]@{
            path = $relative
            sourceSha256 = $before
            sourceSha256After = $after
            isolatedSha256 = $isolatedHash
            roundTripSha256 = $roundTripHash
            wire = $wire
            profileId = $ProfileId
            profileBindingStatus = if ($wire.headerKind -eq 'Unknown') { 'BoundExplicitUnrecognizedWire' } else { 'BoundExplicit' }
            copyRoundTripStatus = $copyStatus
            semanticRoundTripStatus = 'Uncovered'
        })
    }
    if ($sourceMutated) { throw 'M0-SAV-01 source fixture changed during read-only audit.' }
    if ($sourceFiles.Count -eq 0) { $uncovered.Add('no_save_candidates') }
    if (@($entries | Where-Object copyRoundTripStatus -ne 'Passed').Count -gt 0) { $uncovered.Add('isolated_copy_round_trip_failed') }
    $canonical = foreach ($entry in $entries) { "{0}`t{1}`t{2}`t{3}" -f $entry.path, $entry.sourceSha256, $entry.isolatedSha256, $entry.roundTripSha256 }
    $hash = [Security.Cryptography.SHA256]::Create()
    try { $setHash = ([BitConverter]::ToString($hash.ComputeHash($script:Utf8NoBom.GetBytes([string]::Join("`n", [string[]]$canonical))))).Replace('-', '').ToLowerInvariant() }
    finally { $hash.Dispose() }
    $allCopyPassed = $sourceFiles.Count -gt 0 -and @($entries | Where-Object copyRoundTripStatus -ne 'Passed').Count -eq 0
    $allPassed = $allCopyPassed -and $uncovered.Count -eq 0
    return [ordered]@{
        schemaVersion = $script:SchemaVersion
        workPackage = $script:WorkPackage
        auditId = $script:AuditId
        generatedAtUtc = [DateTime]::UtcNow.ToString('O')
        executionStatus = 'InProgress'
        gateStatus = 'Blocked'
        blockerCode = 'EvidenceMissing'
        result = if ($allPassed) { 'ReadyForReview' } else { 'Partial' }
        sourceRoot = $root.Replace('\', '/')
        isolatedCopyRoot = $isolated.Replace('\', '/')
        sourceReadOnly = $true
        profileId = $ProfileId
        profileBindingStatus = 'BoundExplicit'
        copyRoundTripStatus = if ($allCopyPassed) { 'Passed' } else { 'Partial' }
        semanticRoundTripStatus = 'Uncovered'
        fixtureSetHash = $setHash
        sourceSaveFilesRead = $sourceFiles.Count
        fixtures = $entries.ToArray()
        uncovered = $uncovered.ToArray()
        compatibility = @('Explicit profile pin; byte-preserving isolated-copy evidence only')
        rollback = 'Remove the generated report and isolated copy; no source fixture or runtime code is modified.'
    }
}

function Write-LegacySaveRoundTripEvidenceJson {
    param([Parameter(Mandatory = $true)]$Value, [Parameter(Mandatory = $true)][string]$Path)
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 30) + "`n"), $script:Utf8NoBom)
}

Export-ModuleMember -Function New-LegacySaveRoundTripEvidence, Write-LegacySaveRoundTripEvidenceJson
