[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [Parameter(Mandatory = $true)][string]$V24ProjectRoot,
    [Parameter(Mandatory = $true)][string]$SnakeProjectRoot,
    [string]$CatalogPath = '',
    [string]$OutputPath = ''
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($CatalogPath)) { $CatalogPath = Join-Path $toolRoot 'legacy-dialect-upstream-diff.json' }
if ([string]::IsNullOrWhiteSpace($OutputPath)) { $OutputPath = Join-Path $ProjectRoot 'docs\NewFrameworkDesign\generated\legacy-dialect-upstream-diff.json' }
try {
    Import-Module (Join-Path $toolRoot 'LegacyDialectUpstreamDiff.psm1') -Force
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath $CatalogPath | ConvertFrom-Json
    $report = New-LegacyDialectUpstreamDiffReport -ProjectRoot $ProjectRoot -V24ProjectRoot $V24ProjectRoot -SnakeProjectRoot $SnakeProjectRoot -Catalog $catalog -OutputPath $OutputPath
    Write-Output "ERB_DIALECT_DIFF_OUTPUT=$([IO.Path]::GetFullPath($OutputPath))"
    Write-Output "ERB_DIALECT_DIFF_V24_INSTRUCTIONS=$($report.profileSurfaces.v24.instructionCount)"
    Write-Output "ERB_DIALECT_DIFF_V24_FUNCTIONS=$($report.profileSurfaces.v24.expressionFunctionCount)"
    Write-Output "ERB_DIALECT_DIFF_SNAKE_INSTRUCTIONS=$($report.profileSurfaces.snake.instructionCount)"
    Write-Output "ERB_DIALECT_DIFF_SNAKE_FUNCTIONS=$($report.profileSurfaces.snake.expressionFunctionCount)"
    Write-Output "ERB_DIALECT_DIFF_RESULT=$($report.result)"
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
