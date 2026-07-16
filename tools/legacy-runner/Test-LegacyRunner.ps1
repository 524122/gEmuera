[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GodotPath,
    [Parameter(Mandatory = $true)][string]$GameRoot,
    [string]$ProjectRoot = (Get-Location).Path,
    [switch]$SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-runner-test-' + [Guid]::NewGuid().ToString('N'))
$runner = Join-Path $PSScriptRoot 'Invoke-LegacyRunner.ps1'

try {
    $runnerArguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $runner,
        '-GodotPath', $GodotPath,
        '-ProjectRoot', $ProjectRoot,
        '-GameRoot', $GameRoot,
        '-Profile', 'snake',
        '-OutputDirectory', $testRoot,
        '-RepeatCount', '1',
        '-TimeoutSeconds', '360'
    )
    if ($SkipBuild) {
        $runnerArguments += '-SkipBuild'
    }
    & powershell @runnerArguments
    if ($LASTEXITCODE -ne 0) { throw "Legacy runner integration failed with exit code $LASTEXITCODE." }

    $summary = Get-Content -LiteralPath (Join-Path $testRoot 'summary.json') -Raw | ConvertFrom-Json
    if ($summary.result -ne 'PassedRunnerConsistency') { throw 'Runner summary did not pass.' }
    foreach ($name in @('identity.json', 'state.json', 'display.json', 'display.raw.json', 'screenshots.json', 'hit-test.json', 'effects.json', 'errors.json', 'timeline.json', 'semantic-trace.json', 'trace.json', 'trace.raw.json', 'metrics.json', 'artifacts.json', 'diagnostics.zip', 'emuera_startup_errors.log', 'emuera.log')) {
        if (-not (Test-Path -LiteralPath (Join-Path $testRoot "run-001\$name") -PathType Leaf)) {
            throw "Required runner artifact is missing: $name"
        }
    }
    $mutation = Get-Content -LiteralPath (Join-Path $testRoot 'run-001\fixture-mutations.json') -Raw | ConvertFrom-Json
    if ([int]$mutation.changeCount -ne 0) { throw "Runner fixture changed: $($mutation.changeCount)" }
    Write-Output 'Legacy runner integration test passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
    if ($resolvedTest.StartsWith($resolvedTemp, [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
