[CmdletBinding()]
param(
    [string]$DesignRoot = "",
    [string]$ReportPath = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$GuardVersion = "1.0.0"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $ScriptRoot "..\.."))
if ([string]::IsNullOrWhiteSpace($DesignRoot)) {
    $DesignRoot = Join-Path $RepositoryRoot "NewFrameworkDesign"
}
$DesignRoot = [System.IO.Path]::GetFullPath($DesignRoot)

if (-not (Test-Path -LiteralPath $DesignRoot -PathType Container)) {
    throw "Design root does not exist: $DesignRoot"
}

$errors = New-Object System.Collections.Generic.List[object]
$warnings = New-Object System.Collections.Generic.List[object]

function Add-Finding {
    param(
        [Parameter(Mandatory = $true)][string]$Rule,
        [Parameter(Mandatory = $true)][string]$File,
        [int]$Line = 0,
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet("Error", "Warning")][string]$Severity = "Error"
    )

    $finding = [ordered]@{
        rule = $Rule
        file = $File
        line = $Line
        message = $Message
    }
    if ($Severity -eq "Warning") {
        $warnings.Add($finding)
    }
    else {
        $errors.Add($finding)
    }
}

function Get-RelativePath {
    param([string]$BasePath, [string]$TargetPath)

    $baseUri = New-Object System.Uri(($BasePath.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar))
    $targetUri = New-Object System.Uri($TargetPath)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Get-TableColumnCount {
    param([string]$Line)

    $withoutEscapedPipes = [regex]::Replace($Line, '\\\|', '')
    $pipeCount = [regex]::Matches($withoutEscapedPipes, '\|').Count
    if ($pipeCount -lt 2) {
        return 0
    }
    return $pipeCount - 1
}

function Test-IgnoredLinkTarget {
    param([string]$Target)

    return $Target.StartsWith('#') -or
        $Target -match '^(?i:https?|mailto|ftp|data):' -or
        $Target -match '^[A-Za-z][A-Za-z0-9+.-]*://'
}

function Decode-Utf8 {
    param([Parameter(Mandatory = $true)][string]$Base64)

    return [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($Base64))
}

$requiredFiles = @(
    "README.md",
    "M0M2ImplementationBaseline.md",
    "MigrationPlan.md",
    "CompatibilityMatrix.md",
    "KnownLimitations.md",
    "AcceptanceTraceability.md"
)
foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $DesignRoot $requiredFile) -PathType Leaf)) {
        Add-Finding -Rule "required-file" -File $requiredFile -Message "Required authority document is missing."
    }
}

$documents = @(Get-ChildItem -LiteralPath $DesignRoot -Recurse -File -Filter "*.md" | Sort-Object FullName)
if ($documents.Count -eq 0) {
    Add-Finding -Rule "document-set" -File "." -Message "No Markdown documents were found."
}

$manifestLines = New-Object System.Collections.Generic.List[string]
$totalLines = 0L
$totalBytes = 0L
$staleDocumentCountPattern = '(^|[^0-9])(36|37)\s*' + [regex]::Escape([string][char]0x4EFD)

foreach ($document in $documents) {
    $relativePath = Get-RelativePath -BasePath $DesignRoot -TargetPath $document.FullName
    $bytes = [System.IO.File]::ReadAllBytes($document.FullName)
    $text = [System.Text.Encoding]::UTF8.GetString($bytes)
    $lines = [System.IO.File]::ReadAllLines($document.FullName, [System.Text.Encoding]::UTF8)
    $totalLines += $lines.Count
    $totalBytes += $bytes.LongLength

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = ([System.BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $sha256.Dispose()
    }
    $manifestLines.Add("$relativePath`t$hash`t$($bytes.LongLength)")

    if ([string]::IsNullOrWhiteSpace($text)) {
        Add-Finding -Rule "non-empty" -File $relativePath -Message "Document is empty."
        continue
    }

    $h1Lines = @()
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        if ($lines[$lineIndex] -match '^#\s+\S') {
            $h1Lines += ($lineIndex + 1)
        }
        if ($lines[$lineIndex].IndexOf([char]0xFFFD) -ge 0) {
            Add-Finding -Rule "utf8-replacement" -File $relativePath -Line ($lineIndex + 1) -Message "U+FFFD replacement character found."
        }
    }
    if ($h1Lines.Count -ne 1) {
        Add-Finding -Rule "single-h1" -File $relativePath -Message "Expected exactly one level-1 heading; found $($h1Lines.Count)."
    }

    $insideFence = $false
    $fenceCharacter = ''
    $fenceStartLine = 0
    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        $line = $lines[$lineIndex]
        if ($line -match '^\s*(?<fence>`{3,}|~{3,})') {
            $currentCharacter = $Matches['fence'].Substring(0, 1)
            if (-not $insideFence) {
                $insideFence = $true
                $fenceCharacter = $currentCharacter
                $fenceStartLine = $lineIndex + 1
            }
            elseif ($currentCharacter -eq $fenceCharacter) {
                $insideFence = $false
                $fenceCharacter = ''
                $fenceStartLine = 0
            }
            continue
        }

        if ($insideFence) {
            continue
        }

        foreach ($match in [regex]::Matches($line, '(?<!!)\[[^\]]+\]\((?<target>[^)]+)\)')) {
            $target = $match.Groups['target'].Value.Trim()
            if ($target.StartsWith('<') -and $target.EndsWith('>')) {
                $target = $target.Substring(1, $target.Length - 2)
            }
            $target = [regex]::Replace($target, '\s+["''][^"'']*["'']\s*$', '')
            if (Test-IgnoredLinkTarget -Target $target) {
                continue
            }
            $pathPart = ($target -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($pathPart)) {
                continue
            }
            $pathPart = [System.Uri]::UnescapeDataString($pathPart)
            $resolvedTarget = [System.IO.Path]::GetFullPath((Join-Path $document.DirectoryName $pathPart))
            if (-not (Test-Path -LiteralPath $resolvedTarget)) {
                Add-Finding -Rule "relative-link" -File $relativePath -Line ($lineIndex + 1) -Message "Broken relative link: $target"
            }
        }

        if ($line -match $staleDocumentCountPattern -and $line -notmatch '(2026-07-11|224,032)') {
            Add-Finding -Rule "stale-document-count" -File $relativePath -Line ($lineIndex + 1) -Message "Hard-coded stale document count found; use generated guard metrics."
        }
        if ($line.Contains((Decode-Utf8 '5LqU5qC55Li75qKB'))) {
            Add-Finding -Rule "stale-pillar-count" -File $relativePath -Line ($lineIndex + 1) -Message "Stale five-pillar wording found; the migration plan defines six pillars."
        }
        $claimPhrases = @(
            (Decode-Utf8 '5a6M5pW05YW85a65'),
            (Decode-Utf8 '5a6M5YWo5YW85a65'),
            (Decode-Utf8 '5YWo6YOo6YCa6L+H')
        )
        $negativePhrases = @(
            (Decode-Utf8 '5LiN5b6X'),
            (Decode-Utf8 '5LiN6IO9'),
            (Decode-Utf8 '56aB5q2i'),
            (Decode-Utf8 '5LiN562J5LqO'),
            (Decode-Utf8 '5qOA5p+l'),
            (Decode-Utf8 '5a6a5LmJ'),
            (Decode-Utf8 '5Y+q5pyJ')
        )
        $hasClaim = @($claimPhrases | Where-Object { $line.Contains($_) }).Count -gt 0
        $hasQualification = @($negativePhrases | Where-Object { $line.Contains($_) }).Count -gt 0
        if ($hasClaim -and -not $hasQualification -and -not $line.Contains('`Compatible`')) {
            Add-Finding -Rule "unsupported-compatibility-claim" -File $relativePath -Line ($lineIndex + 1) -Message "Unqualified compatibility/completion claim found."
        }

        if ($line -match '^\s*\|(?:\s*:?-{3,}:?\s*\|)+\s*$') {
            $expectedColumns = Get-TableColumnCount -Line $line
            if ($lineIndex -eq 0 -or (Get-TableColumnCount -Line $lines[$lineIndex - 1]) -ne $expectedColumns) {
                Add-Finding -Rule "table-columns" -File $relativePath -Line ($lineIndex + 1) -Message "Table header and separator column counts differ."
            }
            $rowIndex = $lineIndex + 1
            while ($rowIndex -lt $lines.Count -and $lines[$rowIndex] -match '^\s*\|.*\|\s*$') {
                $actualColumns = Get-TableColumnCount -Line $lines[$rowIndex]
                if ($actualColumns -ne $expectedColumns) {
                    Add-Finding -Rule "table-columns" -File $relativePath -Line ($rowIndex + 1) -Message "Expected $expectedColumns table columns; found $actualColumns."
                }
                $rowIndex++
            }
        }
    }
    if ($insideFence) {
        Add-Finding -Rule "closed-fence" -File $relativePath -Line $fenceStartLine -Message "Fenced code block is not closed."
    }
}

$requiredPhrases = [ordered]@{
    "README.md" = @(
        (Decode-Utf8 '5b2T5YmN5ZSv5LiA5YWB6K6455u05o6l5ouG5bel5Y2V5a6e5pa955qE6IyD5Zu0'),
        (Decode-Utf8 '5LiN5b6X5Yig6Zmk5penIHJ1bm5lcuOAgXJlbmRlcmVyIOaIliBwbGF0Zm9ybSBwYXRo')
    )
    "MigrationPlan.md" = @(
        (Decode-Utf8 'TTPigJNNNyDlvZPliY3ku4XnlKjkuo7op4TliJLlkozmjqXlj6Ppo47pmanor4TlrqE='),
        (Decode-Utf8 '6ZW/5pyf5L+d55WZ55qE5YWt5qC55Li75qKB')
    )
    "M0M2ImplementationBaseline.md" = @(
        (Decode-Utf8 '56ys5LiA6Zi25q616L6555WM'),
        (Decode-Utf8 'TTDvvJrlm7rlrprml6cgZ0VtdWVyYSDooYzkuLo='),
        (Decode-Utf8 'TTHvvJpMZWdhY3lTZXNzaW9uRmFjYWRlIOS4jiBnZW5lcmF0aW9uIGd1YXJk'),
        (Decode-Utf8 'TTLvvJrml6DmjZ8gRGlzcGxheSBEVE8g6L6555WM')
    )
    "SecurityLimits.md" = @(
        (Decode-Utf8 'TTDigJNNMiDkv53mjIHml6cgQW5kcm9pZCDlpJbpg6jlrZjlgqgv55uu5b2V6YCJ5oup6Lev5b6E5LiN5Y+Y')
    )
    "SaveFormat.md" = @(
        (Decode-Utf8 '6buY6K6k6KGM5Li65pivKirlj6ror7vpooTmo4DlkI7pmLvmlq3liqDovb3lkozlhpnlm54qKg==')
    )
    "CompatibilityMatrix.md" = @(
        "SAVE.PROFILE.SELECTION",
        "DISPLAY.QUEUE_ORDER",
        "PLATFORM.ANDROID.STORAGE_MIGRATION"
    )
    "KnownLimitations.md" = @(
        "L-026",
        "SAVE.PROFILE.SELECTION",
        "L-027",
        "PLATFORM.ANDROID.STORAGE_MIGRATION",
        "L-028",
        "DISPLAY.QUEUE_ORDER"
    )
}

foreach ($fileName in $requiredPhrases.Keys) {
    $filePath = Join-Path $DesignRoot $fileName
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        continue
    }
    $fileText = [System.IO.File]::ReadAllText($filePath, [System.Text.Encoding]::UTF8)
    foreach ($phrase in $requiredPhrases[$fileName]) {
        if ($fileText.IndexOf($phrase, [System.StringComparison]::Ordinal) -lt 0) {
            Add-Finding -Rule "required-baseline-clause" -File $fileName -Message "Required production-baseline clause is missing: $phrase"
        }
    }
}

$authorityPhrase = Decode-Utf8 '5LiK5ri4IDE4MDggcHJvZmlsZSoqIOeahOWUr+S4gOWtl+iKgue6p+WumuS5iQ=='
$authorityOwners = @()
foreach ($document in $documents) {
    $text = [System.IO.File]::ReadAllText($document.FullName, [System.Text.Encoding]::UTF8)
    if ($text.Contains($authorityPhrase)) {
        $authorityOwners += (Get-RelativePath -BasePath $DesignRoot -TargetPath $document.FullName)
    }
}
if ($authorityOwners.Count -ne 1 -or $authorityOwners[0] -ne "SaveFormat.md") {
    Add-Finding -Rule "authority-owner" -File "SaveFormat.md" -Message "Upstream 1808 byte-level authority must have exactly one owner: SaveFormat.md."
}

$manifestBytes = [System.Text.Encoding]::UTF8.GetBytes(($manifestLines -join "`n"))
$manifestHasher = [System.Security.Cryptography.SHA256]::Create()
try {
    $manifestHash = ([System.BitConverter]::ToString($manifestHasher.ComputeHash($manifestBytes))).Replace('-', '')
}
finally {
    $manifestHasher.Dispose()
}

$resultStatus = "Failed"
if ($errors.Count -eq 0) {
    $resultStatus = "Passed"
}
$errorItems = $errors.ToArray()
$warningItems = $warnings.ToArray()

$result = [ordered]@{
    schema = "gemuera.doc-guard.report.v1"
    guardVersion = $GuardVersion
    generatedAtUtc = [DateTime]::UtcNow.ToString("o")
    command = "tools/doc-guards/Invoke-DocGuard.ps1"
    designRoot = $DesignRoot
    sourceManifestSha256 = $manifestHash
    metrics = [ordered]@{
        markdownDocuments = $documents.Count
        lines = $totalLines
        bytes = $totalBytes
    }
    status = $resultStatus
    errorCount = $errors.Count
    warningCount = $warnings.Count
    errors = $errorItems
    warnings = $warningItems
}

$json = $result | ConvertTo-Json -Depth 8
if (-not [string]::IsNullOrWhiteSpace($ReportPath)) {
    $resolvedReportPath = [System.IO.Path]::GetFullPath($ReportPath)
    $reportDirectory = Split-Path -Parent $resolvedReportPath
    if (-not (Test-Path -LiteralPath $reportDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($resolvedReportPath, $json, (New-Object System.Text.UTF8Encoding($false)))
}

$json
if ($errors.Count -ne 0) {
    exit 1
}
