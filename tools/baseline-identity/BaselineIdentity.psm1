Set-StrictMode -Version Latest

$script:SchemaVersion = "1.0.0"
$script:WorkPackage = "M0-ID-01"
$script:Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Get-NormalizedFullPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [IO.Path]::GetFullPath($Path).Replace('\', '/')
}

function Get-Sha256File {
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

function Get-Sha256Text {
    param([AllowEmptyString()][string]$Text)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = $script:Utf8NoBom.GetBytes($Text)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Test-PathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $normalizedPath = (Get-NormalizedFullPath -Path $Path).TrimEnd('/') + '/'
    $normalizedRoot = (Get-NormalizedFullPath -Path $Root).TrimEnd('/') + '/'
    return $normalizedPath.StartsWith($normalizedRoot, [StringComparison]::OrdinalIgnoreCase)
}

function Get-RelativeManifestPath {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $rootUri = New-Object Uri(((Get-NormalizedFullPath -Path $Root).TrimEnd('/') + '/'))
    $pathUri = New-Object Uri((Get-NormalizedFullPath -Path $Path))
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()).Replace('\', '/')
}

function Test-SourcePathExcluded {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [string]$OutputDirectoryRelative
    )

    $path = $RelativePath.Replace('\', '/')
    if ($OutputDirectoryRelative -and
        ($path -eq $OutputDirectoryRelative -or $path.StartsWith($OutputDirectoryRelative.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase))) {
        return $true
    }

    if ($path -match '(^|/)(\.git|\.godot|\.codegraph|\.agents|\.claude|\.trae|\.vs|\.vscode|\.idea|\.mono|node_modules|action_maps|artifacts|android|export|bin|obj|uEmuera-0\.2\.9d|XEmuera-0\.5\.1)(/|$)') {
        return $true
    }

    if ($path -match '(^|/)(Thumbs\.db|desktop\.ini)$' -or $path -match '\.(user|suo|apk|aab|idsig)$') {
        return $true
    }

    return $false
}

function New-FileManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][System.IO.FileInfo[]]$Files
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $entries = New-Object System.Collections.Generic.List[object]
    $fileByRelativePath = New-Object 'System.Collections.Generic.Dictionary[string,System.IO.FileInfo]' ([StringComparer]::Ordinal)
    foreach ($file in $Files) {
        $relativePath = Get-RelativeManifestPath -Root $resolvedRoot -Path $file.FullName
        $fileByRelativePath.Add($relativePath, $file)
    }
    [string[]]$relativePaths = @($fileByRelativePath.Keys)
    [Array]::Sort($relativePaths, [StringComparer]::Ordinal)

    foreach ($relativePath in $relativePaths) {
        $file = $fileByRelativePath[$relativePath]
        $entries.Add([ordered]@{
            path = $relativePath
            bytes = [int64]$file.Length
            sha256 = Get-Sha256File -Path $file.FullName
        })
    }

    $canonicalLines = foreach ($entry in $entries) {
        "{0}:{1}`t{2}`t{3}" -f $script:Utf8NoBom.GetByteCount($entry.path), $entry.path, $entry.bytes, $entry.sha256
    }
    $canonicalText = [string]::Join("`n", [string[]]$canonicalLines)

    [int64]$totalBytes = 0
    foreach ($entry in $entries) {
        $totalBytes += [int64]$entry.bytes
    }

    return [ordered]@{
        schemaVersion = $script:SchemaVersion
        label = $Label
        root = Get-NormalizedFullPath -Path $resolvedRoot
        fileCount = $entries.Count
        totalBytes = $totalBytes
        canonicalSha256 = Get-Sha256Text -Text $canonicalText
        entries = $entries
    }
}

function Get-DirectoryManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Root,
        [string]$OutputDirectory,
        [switch]$ApplySourceExclusions
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $outputRelative = $null
    if ($OutputDirectory -and (Test-PathWithinRoot -Path $OutputDirectory -Root $resolvedRoot)) {
        $outputRelative = Get-RelativeManifestPath -Root $resolvedRoot -Path $OutputDirectory
    }

    $files = @(
        Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force |
            Where-Object {
                $relativePath = Get-RelativeManifestPath -Root $resolvedRoot -Path $_.FullName
                -not $ApplySourceExclusions -or
                    -not (Test-SourcePathExcluded -RelativePath $relativePath -OutputDirectoryRelative $outputRelative)
            }
    )
    return New-FileManifest -Label $Label -Root $resolvedRoot -Files $files
}

function Get-SelectedFilesManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string[]]$Paths
    )

    $files = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    foreach ($path in $Paths) {
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            $files.Add((Get-Item -LiteralPath $path))
        }
    }
    return New-FileManifest -Label $Label -Root $Root -Files $files.ToArray()
}

function Write-JsonFile {
    param(
        [Parameter(Mandatory = $true)]$Value,
        [Parameter(Mandatory = $true)][string]$Path,
        [int]$Depth = 20
    )

    $json = $Value | ConvertTo-Json -Depth $Depth
    [IO.File]::WriteAllText($Path, $json + "`n", $script:Utf8NoBom)
}

function Write-ManifestFile {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$FileName
    )

    $path = Join-Path $OutputDirectory $FileName
    Write-JsonFile -Value $Manifest -Path $path
    return [ordered]@{
        reportPath = $FileName.Replace('\', '/')
        documentSha256 = Get-Sha256File -Path $path
        canonicalSha256 = $Manifest.canonicalSha256
        fileCount = $Manifest.fileCount
        totalBytes = $Manifest.totalBytes
    }
}

function Invoke-CapturedProcess {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string]$Arguments
    )

    $startInfo = New-Object Diagnostics.ProcessStartInfo
    $startInfo.FileName = $Executable
    $startInfo.Arguments = $Arguments
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $startInfo
    try {
        [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        return [ordered]@{
            exitCode = $process.ExitCode
            stdout = $stdout.Trim()
            stderr = $stderr.Trim()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Resolve-ExecutablePath {
    param(
        [string]$ExplicitPath,
        [Parameter(Mandatory = $true)][string[]]$CommandNames
    )

    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "Explicit executable does not exist: $ExplicitPath"
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    foreach ($name in $CommandNames) {
        $command = Get-Command $name -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($command) {
            return $command.Source
        }
    }
    return $null
}

function Get-ExecutableIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string]$ExecutablePath,
        [Parameter(Mandatory = $true)][string]$VersionArguments,
        [string]$DetailArguments
    )

    if (-not $ExecutablePath) {
        return [ordered]@{ name = $Name; status = 'Uncovered' }
    }

    $version = Invoke-CapturedProcess -Executable $ExecutablePath -Arguments $VersionArguments
    $detail = $null
    if ($DetailArguments) {
        $detail = Invoke-CapturedProcess -Executable $ExecutablePath -Arguments $DetailArguments
    }
    $detailText = if ($detail) { ($detail.stdout + "`n" + $detail.stderr).Trim() } else { '' }
    return [ordered]@{
        name = $Name
        status = if ($version.exitCode -eq 0) { 'Captured' } else { 'CommandFailed' }
        executable = Get-NormalizedFullPath -Path $ExecutablePath
        executableSha256 = Get-Sha256File -Path $ExecutablePath
        versionExitCode = $version.exitCode
        version = ($version.stdout + "`n" + $version.stderr).Trim()
        detailSha256 = Get-Sha256Text -Text $detailText
        detail = $detailText
    }
}

function Get-ProjectMetadata {
    param([Parameter(Mandatory = $true)][string]$ProjectFile)

    [xml]$project = [IO.File]::ReadAllText($ProjectFile, [Text.Encoding]::UTF8)
    $targetFrameworks = @($project.SelectNodes('/Project/PropertyGroup/TargetFramework') | ForEach-Object { $_.'#text' } | Where-Object { $_ } | Select-Object -Unique)
    $packages = @($project.SelectNodes('/Project/ItemGroup/PackageReference') | ForEach-Object {
        [ordered]@{ name = [string]$_.Include; version = [string]$_.Version }
    })
    return [ordered]@{
        path = Get-NormalizedFullPath -Path $ProjectFile
        sha256 = Get-Sha256File -Path $ProjectFile
        sdk = [string]$project.Project.Sdk
        targetFrameworks = $targetFrameworks
        packages = $packages
    }
}

function Get-ToolchainMetadataManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $files = @(Get-ChildItem -LiteralPath $resolvedRoot -File -Recurse -Force | Where-Object {
        $_.Name -in @('source.properties', 'package.xml', 'packages.xml')
    })
    return New-FileManifest -Label $Label -Root $resolvedRoot -Files $files
}

function Invoke-BaselineIdentity {
    [CmdletBinding()]
    param(
        [string]$ProjectRoot = (Get-Location).Path,
        [string]$OutputDirectory,
        [string]$GameRoot,
        [string[]]$ArtifactPath = @(),
        [string]$GodotExecutable,
        [string]$JavaExecutable,
        [string]$AndroidSdkRoot,
        [string]$AndroidNdkRoot,
        [string]$ExportTemplatesRoot
    )

    if (-not (Test-Path -LiteralPath $ProjectRoot -PathType Container)) {
        throw "Project root does not exist: $ProjectRoot"
    }
    $projectRootResolved = (Resolve-Path -LiteralPath $ProjectRoot).Path
    if (-not $OutputDirectory) {
        $OutputDirectory = Join-Path $projectRootResolved 'artifacts\baseline-identity'
    }
    $outputFullPath = [IO.Path]::GetFullPath($OutputDirectory)
    [IO.Directory]::CreateDirectory($outputFullPath) | Out-Null

    $errors = New-Object System.Collections.Generic.List[string]
    $uncovered = New-Object System.Collections.Generic.List[string]

    foreach ($artifact in $ArtifactPath) {
        if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
            $errors.Add("Explicit artifact does not exist: $artifact")
        }
    }
    if ($GameRoot -and -not (Test-Path -LiteralPath $GameRoot -PathType Container)) {
        $errors.Add("Explicit game root does not exist: $GameRoot")
    }
    foreach ($toolRoot in @($AndroidSdkRoot, $AndroidNdkRoot, $ExportTemplatesRoot) | Where-Object { $_ }) {
        if (-not (Test-Path -LiteralPath $toolRoot -PathType Container)) {
            $errors.Add("Explicit toolchain root does not exist: $toolRoot")
        }
    }
    if ($errors.Count -gt 0) {
        throw [string]::Join([Environment]::NewLine, $errors)
    }

    $sourceManifest = Get-DirectoryManifest -Label 'source-tree' -Root $projectRootResolved -OutputDirectory $outputFullPath -ApplySourceExclusions
    $sourceSummary = Write-ManifestFile -Manifest $sourceManifest -OutputDirectory $outputFullPath -FileName 'source-tree.manifest.json'

    $configRelativePaths = @('project.godot', 'gemuera-c#.csproj', 'export_presets.cfg', 'config.toml', 'package.json', 'package-lock.json')
    $configPaths = @($configRelativePaths | ForEach-Object { Join-Path $projectRootResolved $_ } | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })
    $configurationManifest = Get-SelectedFilesManifest -Label 'configuration' -Root $projectRootResolved -Paths $configPaths
    $configurationSummary = Write-ManifestFile -Manifest $configurationManifest -OutputDirectory $outputFullPath -FileName 'configuration.manifest.json'

    $resourceRoot = Join-Path $projectRootResolved 'Resources'
    if (Test-Path -LiteralPath $resourceRoot -PathType Container) {
        $resourceManifest = Get-DirectoryManifest -Label 'resources' -Root $resourceRoot
        $resourceSummary = Write-ManifestFile -Manifest $resourceManifest -OutputDirectory $outputFullPath -FileName 'resources.manifest.json'
    }
    else {
        $resourceSummary = [ordered]@{ status = 'Uncovered' }
        $uncovered.Add('Resources directory is absent.')
    }

    $fontRoot = Join-Path $projectRootResolved 'Fonts'
    if (Test-Path -LiteralPath $fontRoot -PathType Container) {
        $fontManifest = Get-DirectoryManifest -Label 'fonts' -Root $fontRoot
        $fontSummary = Write-ManifestFile -Manifest $fontManifest -OutputDirectory $outputFullPath -FileName 'fonts.manifest.json'
    }
    else {
        $fontSummary = [ordered]@{ status = 'Uncovered' }
        $uncovered.Add('Fonts directory is absent.')
    }

    $gameSummary = [ordered]@{ status = 'Uncovered' }
    if ($GameRoot) {
        $gameManifest = Get-DirectoryManifest -Label 'game-directory' -Root $GameRoot
        $gameSummary = Write-ManifestFile -Manifest $gameManifest -OutputDirectory $outputFullPath -FileName 'game.manifest.json'
        $gameSummary.status = 'Captured'
        $gameSummary.root = Get-NormalizedFullPath -Path (Resolve-Path -LiteralPath $GameRoot).Path
    }
    else {
        $uncovered.Add('Game directory was not supplied.')
    }

    $artifactEntries = New-Object System.Collections.Generic.List[object]
    foreach ($artifact in $ArtifactPath) {
        $resolvedArtifact = (Resolve-Path -LiteralPath $artifact).Path
        $item = Get-Item -LiteralPath $resolvedArtifact
        $artifactEntries.Add([ordered]@{
            path = Get-NormalizedFullPath -Path $resolvedArtifact
            bytes = [int64]$item.Length
            sha256 = Get-Sha256File -Path $resolvedArtifact
            extension = $item.Extension.ToLowerInvariant()
        })
    }
    if ($artifactEntries.Count -eq 0) {
        $uncovered.Add('No runtime artifact or APK was supplied.')
    }

    $dotnetPath = Resolve-ExecutablePath -CommandNames @('dotnet')
    $resolvedGodotPath = Resolve-ExecutablePath -ExplicitPath $GodotExecutable -CommandNames @('godot', 'godot4')
    $resolvedJavaPath = Resolve-ExecutablePath -ExplicitPath $JavaExecutable -CommandNames @('java')
    $dotnetIdentity = Get-ExecutableIdentity -Name '.NET SDK' -ExecutablePath $dotnetPath -VersionArguments '--version' -DetailArguments '--info'
    $godotIdentity = Get-ExecutableIdentity -Name 'Godot .NET' -ExecutablePath $resolvedGodotPath -VersionArguments '--version'
    $javaIdentity = Get-ExecutableIdentity -Name 'Java' -ExecutablePath $resolvedJavaPath -VersionArguments '-version'
    if ($dotnetIdentity.status -ne 'Captured') { $uncovered.Add('.NET SDK identity was not captured.') }
    if ($godotIdentity.status -ne 'Captured') { $uncovered.Add('Godot executable identity was not captured.') }
    if ($javaIdentity.status -ne 'Captured') { $uncovered.Add('Java executable identity was not captured.') }

    if (-not $AndroidSdkRoot) {
        $AndroidSdkRoot = if ($env:ANDROID_SDK_ROOT) { $env:ANDROID_SDK_ROOT } else { $env:ANDROID_HOME }
    }
    if (-not $AndroidNdkRoot -and $env:ANDROID_NDK_ROOT) {
        $AndroidNdkRoot = $env:ANDROID_NDK_ROOT
    }

    $toolchainManifests = [ordered]@{}
    foreach ($toolchainSpec in @(
        @{ Key = 'androidSdk'; Label = 'android-sdk-metadata'; Root = $AndroidSdkRoot },
        @{ Key = 'androidNdk'; Label = 'android-ndk-metadata'; Root = $AndroidNdkRoot },
        @{ Key = 'exportTemplates'; Label = 'godot-export-templates'; Root = $ExportTemplatesRoot }
    )) {
        if ($toolchainSpec.Root -and (Test-Path -LiteralPath $toolchainSpec.Root -PathType Container)) {
            $manifest = if ($toolchainSpec.Key -eq 'exportTemplates') {
                Get-DirectoryManifest -Label $toolchainSpec.Label -Root $toolchainSpec.Root
            }
            else {
                Get-ToolchainMetadataManifest -Label $toolchainSpec.Label -Root $toolchainSpec.Root
            }
            $fileName = $toolchainSpec.Label + '.manifest.json'
            $summary = Write-ManifestFile -Manifest $manifest -OutputDirectory $outputFullPath -FileName $fileName
            $summary.status = 'Captured'
            $summary.root = Get-NormalizedFullPath -Path (Resolve-Path -LiteralPath $toolchainSpec.Root).Path
            $toolchainManifests[$toolchainSpec.Key] = $summary
        }
        else {
            $toolchainManifests[$toolchainSpec.Key] = [ordered]@{ status = 'Uncovered' }
            $uncovered.Add("$($toolchainSpec.Label) was not supplied or discovered.")
        }
    }

    $projectFile = Join-Path $projectRootResolved 'gemuera-c#.csproj'
    $projectMetadata = if (Test-Path -LiteralPath $projectFile -PathType Leaf) {
        Get-ProjectMetadata -ProjectFile $projectFile
    }
    else {
        $uncovered.Add('gemuera-c#.csproj is absent.')
        [ordered]@{ status = 'Uncovered' }
    }

    $gitIdentity = [ordered]@{ status = 'Unavailable'; reason = 'No usable Git metadata; tree manifest is authoritative.' }
    $gitCommand = Get-Command git -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($gitCommand) {
        $revision = Invoke-CapturedProcess -Executable $gitCommand.Source -Arguments ("-C `"{0}`" rev-parse HEAD" -f $projectRootResolved)
        if ($revision.exitCode -eq 0) {
            $status = Invoke-CapturedProcess -Executable $gitCommand.Source -Arguments ("-C `"{0}`" status --porcelain" -f $projectRootResolved)
            $gitIdentity = [ordered]@{
                status = 'Captured'
                revision = $revision.stdout.Trim()
                dirty = -not [string]::IsNullOrWhiteSpace($status.stdout)
            }
        }
    }

    $identity = [ordered]@{
        schemaVersion = $script:SchemaVersion
        workPackage = $script:WorkPackage
        generatedAtUtc = [DateTime]::UtcNow.ToString('o')
        status = if ($uncovered.Count -eq 0) { 'Complete' } else { 'Partial' }
        source = [ordered]@{
            identityKind = 'treeManifest'
            root = Get-NormalizedFullPath -Path $projectRootResolved
            manifest = $sourceSummary
            git = $gitIdentity
            exclusionPolicy = 'M0-ID-01-v1'
        }
        toolchain = [ordered]@{
            dotnet = $dotnetIdentity
            godot = $godotIdentity
            java = $javaIdentity
            project = $projectMetadata
            manifests = $toolchainManifests
        }
        configurations = $configurationSummary
        resources = $resourceSummary
        fonts = $fontSummary
        artifacts = [ordered]@{
            status = if ($artifactEntries.Count -gt 0) { 'Captured' } else { 'Uncovered' }
            entries = $artifactEntries
        }
        game = $gameSummary
        uncovered = $uncovered
        errors = @()
    }

    $identityPath = Join-Path $outputFullPath 'identity.json'
    Write-JsonFile -Value $identity -Path $identityPath
    return [ordered]@{
        status = $identity.status
        identityPath = Get-NormalizedFullPath -Path $identityPath
        identitySha256 = Get-Sha256File -Path $identityPath
        sourceTreeSha256 = $sourceManifest.canonicalSha256
        uncoveredCount = $uncovered.Count
    }
}

Export-ModuleMember -Function Invoke-BaselineIdentity
