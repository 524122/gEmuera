[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $scriptRoot '..\..')).Path
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $scriptRoot '10-Source-Index.md'
}
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path.TrimEnd('\', '/')
$sourceRoots = @(
    (Join-Path $repositoryRoot 'Scripts'),
    (Join-Path $repositoryRoot 'src\Core'),
    (Join-Path $repositoryRoot 'test'),
    (Join-Path $repositoryRoot 'tools\core-contracts')
) | Where-Object { Test-Path -LiteralPath $_ }

$typePattern = '^(?:\s)*(?:(?:public|internal|private|protected)\s+)?(?:(?:static|sealed|abstract|partial|readonly)\s+)*(?:class|interface|enum|struct|record(?:\s+(?:class|struct))?)\s+([A-Za-z_][A-Za-z0-9_]*)'
$items = New-Object System.Collections.Generic.List[object]

foreach ($sourceRoot in $sourceRoots) {
    Get-ChildItem -LiteralPath $sourceRoot -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = $_.FullName.Substring($repositoryRoot.Length).TrimStart([char[]]@('\', '/')).Replace('\', '/')
            $types = @(
                Select-String -LiteralPath $_.FullName -Encoding utf8 -Pattern $typePattern |
                    ForEach-Object {
                        foreach ($match in $_.Matches) {
                            $match.Groups[1].Value
                        }
                    } |
                    Select-Object -Unique
            )
            if ($types.Count -eq 0) { $types = @('—') }
            if ($relative -match '^(Scripts/[^/]+)') {
                $group = $Matches[1]
            }
            elseif ($relative -match '^(src/Core/[^/]+)') {
                $group = $Matches[1]
            }
            elseif ($relative -match '^(tools/core-contracts)') {
                $group = $Matches[1]
            }
            elseif ($relative -match '^(test/[^/]+)') {
                $group = $Matches[1]
            }
            else {
                $group = $relative.Split('/')[0]
            }
            $items.Add([pscustomobject]@{ Group = $group; Path = $relative; Types = ($types -join ', ') })
        }
}

$builder = New-Object System.Text.StringBuilder
[void]$builder.AppendLine('# 源码索引')
[void]$builder.AppendLine()
[void]$builder.AppendLine('> **生成文件。** 此索引只覆盖仓库自有的 `Scripts/`、`src/Core/`、`test/` 与 `tools/core-contracts/` C# 源文件；跳过 `bin/`、`obj/`、`.godot/`、`android/build/`、`addons/gdUnit4/` 和 `node_modules/`。')
[void]$builder.AppendLine('>')
[void]$builder.AppendLine('> 重新生成：`powershell -NoProfile -ExecutionPolicy Bypass -File docs/gEmueraCodeWiki/Update-SourceMap.ps1`。声明名由轻量正则提取，用于定位，不等同于公开 API 或完整调用图。')
[void]$builder.AppendLine()
[void]$builder.AppendLine('## 使用方式')
[void]$builder.AppendLine()
[void]$builder.AppendLine('- 先用本页按目录/文件定位，再结合其他 Wiki 页面和 CodeGraph 确认调用链。')
[void]$builder.AppendLine('- `partial` 类型的成员分布在多个文件；请同时查看同名的所有文件。')
[void]$builder.AppendLine('- 需要修改 ERB 语义时，先读 [`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md) 与 [`../../ERBAPI.md`](../../ERBAPI.md)。')
[void]$builder.AppendLine()

foreach ($group in ($items | Group-Object Group | Sort-Object Name)) {
    [void]$builder.AppendLine(('## `{0}`' -f $group.Name))
    [void]$builder.AppendLine()
    [void]$builder.AppendLine('| 文件 | 识别到的类型声明 |')
    [void]$builder.AppendLine('| --- | --- |')
    foreach ($item in ($group.Group | Sort-Object Path)) {
        $types = $item.Types.Replace('|', '\|')
        [void]$builder.AppendLine(('| `{0}` | `{1}` |' -f $item.Path, $types))
    }
    [void]$builder.AppendLine()
}

$outputDirectory = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
}
[System.IO.File]::WriteAllText($OutputPath, $builder.ToString(), (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Updated $OutputPath with $($items.Count) source-file entries."