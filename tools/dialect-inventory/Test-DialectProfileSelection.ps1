[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectProfileSelection.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-profile-selection.json'
$reportSchemaPath = Join-Path $toolRoot 'dialect-profile-selection.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'dialect-profile-selection-catalog.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-profile-selection-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-ProfileSelectionContract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-ProfileSelectionThrows([scriptblock]$Action, [string]$Pattern, [string]$Failure) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) { throw "$Failure Actual: $($caught.Exception.Message)" }
}

function Read-ProfileSelectionJson([string]$Path) {
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-ProfileSelectionObject([object]$Value) {
    return ($Value | ConvertTo-Json -Depth 80 | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing DIA-11 module: $modulePath"
    }

    Import-Module $modulePath -Force
    $catalog = Read-ProfileSelectionJson $catalogPath
    $planPreflight = Read-ProfileSelectionJson (Join-Path $generatedRoot 'dialect-plan-preflight.json')
    $reportSchema = Read-ProfileSelectionJson $reportSchemaPath
    $catalogSchema = Read-ProfileSelectionJson $catalogSchemaPath

    $actual = New-DialectProfileSelectionReport -PlanPreflight $planPreflight -Catalog $catalog -ProjectRoot $ProjectRoot
    Assert-ProfileSelectionContract ($actual.workPackage -ceq 'M0-DIA-11') 'Unexpected DIA-11 work package.'
    Assert-ProfileSelectionContract ($actual.executionStatus -ceq 'InProgress' -and $actual.gateStatus -ceq 'Blocked' -and $actual.blockerCode -ceq 'EvidenceMissing') 'DIA-11 status was incorrectly advanced.'
    Assert-ProfileSelectionContract ($actual.result -ceq 'Partial') 'DIA-11 must remain Partial.'
    Assert-ProfileSelectionContract ($actual.selectionSetHash -match '^[0-9a-f]{64}$') 'DIA-11 selection set hash is invalid.'
    Assert-ProfileSelectionContract ($actual.sourcePlanPreflightHash -ceq $planPreflight.preflightSetHash) 'DIA-11 did not pin DIA-10 evidence.'
    Assert-ProfileSelectionContract ($actual.currentRuntimeIsolation.status -ceq 'Failed') 'Runtime isolation was incorrectly advanced.'
    Assert-ProfileSelectionContract ($actual.parserVmConsumption.status -ceq 'NotConsumed') 'DIA-11 was incorrectly wired into Parser/VM.'
    Assert-ProfileSelectionContract ($actual.compatibilityPlanRuntime.status -ceq 'NotImplemented') 'DIA-11 was incorrectly presented as a runtime plan.'
    Assert-ProfileSelectionContract ($actual.m1Eligibility.status -ceq 'Blocked') 'DIA-11 incorrectly advanced M1 eligibility.'
    Assert-ProfileSelectionContract ($reportSchema.properties.workPackage.const -ceq 'M0-DIA-11') 'DIA-11 report schema work package drifted.'
    Assert-ProfileSelectionContract ($catalogSchema.properties.sourceWorkPackage.const -ceq 'M0-DIA-11') 'DIA-11 catalog schema work package drifted.'

    Assert-ProfileSelectionContract ((@($actual.legacyCoreProfiles.legacyCoreProfileEnum) -join ',') -ceq 'Snake,SnakeModernMobile,V24Pure') 'Legacy CoreProfile enum set drifted.'
    $v24 = @($actual.legacyCoreProfiles | Where-Object legacyCoreProfileEnum -ceq 'V24Pure')
    $snake = @($actual.legacyCoreProfiles | Where-Object legacyCoreProfileEnum -ceq 'Snake')
    $modern = @($actual.legacyCoreProfiles | Where-Object legacyCoreProfileEnum -ceq 'SnakeModernMobile')
    Assert-ProfileSelectionContract ($v24.Count -eq 1 -and $v24[0].launcherProfileId -ceq 'v24pure' -and $v24[0].preflightEvidenceStatus -ceq 'EvidenceBacked') 'v24 profile mapping drifted.'
    Assert-ProfileSelectionContract ($snake.Count -eq 1 -and $snake[0].launcherProfileId -ceq 'snake' -and $snake[0].preflightEvidenceStatus -ceq 'EvidenceBacked') 'Snake profile mapping drifted.'
    Assert-ProfileSelectionContract ($modern.Count -eq 1 -and $modern[0].launcherProfileId -eq '' -and $modern[0].preflightEvidenceStatus -ceq 'Uncovered') 'SnakeModernMobile must remain separate from the launcher profiles.'
    Assert-ProfileSelectionContract ($modern[0].runnerProfileStatus -ceq 'Unsupported') 'Legacy runner coverage for SnakeModernMobile drifted.'

    Assert-ProfileSelectionContract ((@($actual.launcherProfileContract.supportedProfileIds) -join ',') -ceq 'snake,v24pure') 'Launcher profile ids drifted.'
    Assert-ProfileSelectionContract ($actual.launcherProfileContract.unknownProfileFallback -ceq 'v24pure') 'Launcher fallback drifted.'
    Assert-ProfileSelectionContract ((@($actual.modernMarkerContract.markerFileNames) -join ',') -ceq 'modern_core.txt,snake_modern_core.txt') 'Modern marker contract drifted.'
    Assert-ProfileSelectionContract ((@($actual.legacyMarkerContract.markerFileNames) -join ',') -ceq 'legacy_snake_core.txt,snake_core.txt') 'Legacy marker contract drifted.'
    Assert-ProfileSelectionContract ((@($actual.selectionPrecedence | ForEach-Object outputLegacyCoreProfileEnum) -join ',') -ceq 'V24Pure,Snake,SnakeModernMobile,Snake,V24Pure') 'DetectCoreProfile precedence drifted.'
    Assert-ProfileSelectionContract ($actual.selectionPrecedence[1].inputKind -ceq 'LauncherProfileSnake') 'Launcher must retain precedence over marker selection.'

    $profileSnapshot = @($actual.legacyCoreProfiles | ForEach-Object { $_.legacyCoreProfileEnum + ':' + $_.preflightEvidenceStatus })
    $planPreflight.profiles[0].legacyCoreProfileEnum = 'MUTATED_AFTER_SELECTION_REPORT'
    Assert-ProfileSelectionContract ((@($actual.legacyCoreProfiles | ForEach-Object { $_.legacyCoreProfileEnum + ':' + $_.preflightEvidenceStatus }) -join '|') -ceq ($profileSnapshot -join '|')) 'DIA-11 retained a mutable reference to DIA-10 input.'
    $planPreflight = Read-ProfileSelectionJson (Join-Path $generatedRoot 'dialect-plan-preflight.json')

    $reorderedCatalog = Copy-ProfileSelectionObject $catalog
    [array]::Reverse($reorderedCatalog.sourceFiles)
    [array]::Reverse($reorderedCatalog.expected.legacyCoreProfileEnums)
    [array]::Reverse($reorderedCatalog.expected.modernMarkerFileNames)
    [array]::Reverse($reorderedCatalog.expected.legacyMarkerFileNames)
    [array]::Reverse($reorderedCatalog.expected.selectionPrecedence)
    $reordered = New-DialectProfileSelectionReport $planPreflight $reorderedCatalog $ProjectRoot
    Assert-ProfileSelectionContract ($reordered.selectionSetHash -ceq $actual.selectionSetHash) 'Catalog enumeration order changed the DIA-11 set hash.'

    $advancedPlan = Copy-ProfileSelectionObject $planPreflight
    $advancedPlan.compatibilityPlanRuntime.status = 'Implemented'
    Assert-ProfileSelectionThrows { New-DialectProfileSelectionReport $advancedPlan $catalog $ProjectRoot } 'runtime compatibility plan status must remain NotImplemented' 'DIA-11 accepted an advanced DIA-10 runtime status.'

    $sourceDriftCatalog = Copy-ProfileSelectionObject $catalog
    $sourceDriftCatalog.sourceFiles[0].sha256 = ('0' * 64)
    Assert-ProfileSelectionThrows { New-DialectProfileSelectionReport $planPreflight $sourceDriftCatalog $ProjectRoot } 'source hash drifted' 'DIA-11 accepted source drift.'

    $markerDriftCatalog = Copy-ProfileSelectionObject $catalog
    $markerDriftCatalog.expected.modernMarkerFileNames = @('modern_core.txt')
    Assert-ProfileSelectionThrows { New-DialectProfileSelectionReport $planPreflight $markerDriftCatalog $ProjectRoot } 'modern marker file set.*drifted' 'DIA-11 accepted an incomplete modern marker catalog.'

    $outputPath = Join-Path $testRoot 'dialect-profile-selection.json'
    $written = New-DialectProfileSelectionReport -PlanPreflight $planPreflight -Catalog $catalog -ProjectRoot $ProjectRoot -OutputPath $outputPath
    Assert-ProfileSelectionContract (Test-Path -LiteralPath $outputPath -PathType Leaf) 'DIA-11 report was not written.'
    Assert-ProfileSelectionContract ((Read-ProfileSelectionJson $outputPath).selectionSetHash -ceq $written.selectionSetHash) 'Written DIA-11 report hash drifted.'

    Write-Output 'M0 dialect profile selection contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot).TrimEnd('\') + '\'
    if ($resolvedTest.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        $resolvedTest.Contains('gemuera-m0-dia-profile-selection-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
