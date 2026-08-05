[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [Parameter(Mandatory = $true)][string]$V24ProjectRoot,
    [Parameter(Mandatory = $true)][string]$SnakeProjectRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-legacy-dialect-diff-' + [Guid]::NewGuid().ToString('N'))

function Assert-DialectDiffContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-DialectDiffThrows {
    param([scriptblock]$Action, [string]$Pattern, [string]$Message)
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Message }
    if ($caught.Exception.Message -notmatch $Pattern) {
        throw "$Message Actual: $($caught.Exception.Message)"
    }
}

try {
    Import-Module (Join-Path $toolRoot 'LegacyDialectUpstreamDiff.psm1') -Force
    $catalog = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $toolRoot 'legacy-dialect-upstream-diff.json') | ConvertFrom-Json
    $outputPath = Join-Path $testRoot 'legacy-dialect-upstream-diff.json'
    $report = New-LegacyDialectUpstreamDiffReport -ProjectRoot $ProjectRoot -V24ProjectRoot $V24ProjectRoot -SnakeProjectRoot $SnakeProjectRoot -Catalog $catalog -OutputPath $outputPath

    Assert-DialectDiffContract ($report.workPackage -eq 'ERB-DIALECT-UPSTREAM-DIFF' -and $report.result -eq 'Passed') 'Dual-upstream dialect report did not pass.'
    Assert-DialectDiffContract ($report.upstreams.v24.instructionCount -eq 303 -and $report.upstreams.v24.expressionFunctionCount -eq 266) 'v24 executable registration counts drifted.'
    Assert-DialectDiffContract ($report.upstreams.snake.instructionCount -eq 326 -and $report.upstreams.snake.expressionFunctionCount -eq 347) 'Snake executable registration counts drifted.'
    Assert-DialectDiffContract ($report.current.instructionCount -eq 327 -and $report.current.expressionFunctionCount -eq 362) 'Current handler-store counts drifted.'
    Assert-DialectDiffContract ((@($report.deltas.portOnlyInstructions).Count -eq 1) -and (@($report.deltas.portOnlyInstructions)[0] -ceq 'OUTPUTLOG')) 'Port-only instruction set drifted.'
    Assert-DialectDiffContract (@($report.deltas.snakeOnlyInstructions).Count -eq 23) 'Snake-only instruction delta drifted.'
    Assert-DialectDiffContract (@($report.deltas.snakeOnlyExpressionFunctions).Count -eq 83) 'Snake-only expression-function delta drifted.'
    Assert-DialectDiffContract (@($report.deltas.v24OnlyExpressionFunctions).Count -eq 2) 'v24-only expression-function delta drifted.'
    Assert-DialectDiffContract (@($report.deltas.currentOnlyExpressionFunctionsVsSnake).Count -eq 15) 'Current-to-Snake exclusion delta drifted.'
    Assert-DialectDiffContract (@($report.deltas.currentOnlyExpressionFunctionsVsSnake) -contains 'GROTATE') 'GROTATE is no longer guarded as a port-only expression function.'
    Assert-DialectDiffContract ($report.profileSurfaces.v24.instructionCount -eq 303 -and $report.profileSurfaces.v24.expressionFunctionCount -eq 266) 'v24 profile surface no longer equals its upstream registry.'
    Assert-DialectDiffContract ($report.profileSurfaces.snake.instructionCount -eq 326 -and $report.profileSurfaces.snake.expressionFunctionCount -eq 347) 'Snake profile surface no longer equals its upstream registry.'
    Assert-DialectDiffContract ($report.verification.publicKeySets -eq 'Passed' -and $report.verification.profileVisibilityDeclarations -eq 'Passed') 'Profile/public-key verification was not completed.'
    Assert-DialectDiffContract ($report.verification.instructionParameterAndReturnTypes -eq 'Passed' -and $report.verification.expressionParameterAndReturnTypes -eq 'Passed') 'Runtime-introspected parameter/return gates did not pass.'
    Assert-DialectDiffContract ($report.verification.runtimeExecution -eq 'Partial') 'Runtime execution gate was not marked Partial.'
    Assert-DialectDiffContract ($report.verification.runtimeEvidence.verified -and $report.verification.runtimeEvidence.totalMismatchCount -eq 0) 'Runtime reflection evidence did not verify zero mismatches.'
    Assert-DialectDiffContract (-not $report.verification.runtimeEvidence.behaviorVerified -and $report.verification.runtimeEvidence.behaviorFunctionCount -eq 0) 'Runtime reflection evidence overstated behavior coverage.'
    Assert-DialectDiffContract ((Test-Path -LiteralPath $outputPath -PathType Leaf)) 'Dual-upstream dialect report was not written.'

    $tampered = $catalog | ConvertTo-Json -Depth 10 | ConvertFrom-Json
    $tampered.v24.instructionSourceSha256 = ('0' * 64)
    Assert-DialectDiffThrows {
        New-LegacyDialectUpstreamDiffReport -ProjectRoot $ProjectRoot -V24ProjectRoot $V24ProjectRoot -SnakeProjectRoot $SnakeProjectRoot -Catalog $tampered
    } 'v24 instruction source hash drifted' 'Upstream source identity drift was accepted.'

    Write-Output 'Legacy dual-upstream dialect diff tests passed.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
