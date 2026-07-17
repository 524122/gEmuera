[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-CoreArchitecture {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$coreRoot = Join-Path $ProjectRoot 'src\Core'
$coreProject = Join-Path $coreRoot 'GEmuera.Core.csproj'
Assert-CoreArchitecture (Test-Path -LiteralPath $coreProject -PathType Leaf) 'Core project is missing.'

$sourceFiles = @(
    Get-ChildItem -LiteralPath $coreRoot -Filter '*.cs' -Recurse -File |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' }
)
Assert-CoreArchitecture ($sourceFiles.Count -ge 3) 'Core source inventory unexpectedly shrank.'

foreach ($sourceFile in $sourceFiles) {
    $text = Get-Content -LiteralPath $sourceFile.FullName -Raw -Encoding utf8
    Assert-CoreArchitecture ($text -notmatch '(?m)^\s*(?:global\s+)?using\s+Godot(?:\.[A-Za-z0-9_]+)*\s*;') "Core source references Godot: $($sourceFile.FullName)"
    Assert-CoreArchitecture ($text -notmatch '\bGodot(?:Sharp)?\.(Node|Resource|Texture2D|Image|Variant|Callable|Object|GodotObject)\b') "Core source exposes Godot type: $($sourceFile.FullName)"
    Assert-CoreArchitecture ($text -notmatch '\[Signal\]|EmitSignal\s*\(') "Core source uses Godot signal API: $($sourceFile.FullName)"
}

$projectText = Get-Content -LiteralPath $coreProject -Raw -Encoding utf8
Assert-CoreArchitecture ($projectText -match '<TargetFrameworks>net8\.0;net10\.0</TargetFrameworks>') 'Core Godot-host and validation target frameworks are not explicit.'
Assert-CoreArchitecture ($projectText -notmatch 'Godot\.NET\.Sdk|GodotSharp|ProjectReference') 'Core project gained a Godot or legacy project dependency.'

Write-Output "Core architecture guard passed: files=$($sourceFiles.Count)."
