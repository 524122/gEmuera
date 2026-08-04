[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'docs\NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectModuleVisibility.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-module-visibility.json'
$reportSchemaPath = Join-Path $toolRoot 'dialect-module-visibility.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'dialect-module-visibility-catalog.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-module-visibility-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-ModuleVisibilityContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-ModuleVisibilityThrows {
    param([scriptblock]$Action, [string]$Pattern, [string]$Failure)
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) {
        throw "$Failure Actual: $($caught.Exception.Message)"
    }
}

function Copy-ModuleVisibilityObject {
    param([object]$Value)
    return ($Value | ConvertTo-Json -Depth 60 | ConvertFrom-Json)
}

function Read-ModuleVisibilityJson {
    param([string]$Path)
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing DIA-09 module: $modulePath"
    }
    Import-Module $modulePath -Force
    $catalog = Read-ModuleVisibilityJson $catalogPath
    $reportSchema = Read-ModuleVisibilityJson $reportSchemaPath
    $catalogSchema = Read-ModuleVisibilityJson $catalogSchemaPath
    $snapshots = Read-ModuleVisibilityJson (Join-Path $generatedRoot 'dialect-registry-snapshots.json')
    $ownership = Read-ModuleVisibilityJson (Join-Path $generatedRoot 'dialect-ownership-evidence.json')
    $lookup = Read-ModuleVisibilityJson (Join-Path $generatedRoot 'dialect-name-lookup-contract.json')

    $actual = New-DialectModuleVisibilityReport -RegistrySnapshot $snapshots -OwnershipEvidence $ownership -NameLookupContract $lookup -Catalog $catalog
    Assert-ModuleVisibilityContract ($actual.workPackage -eq 'M0-DIA-09') 'Unexpected module visibility work package.'
    Assert-ModuleVisibilityContract ($reportSchema.properties.workPackage.const -eq 'M0-DIA-09') 'DIA-09 report schema work package drifted.'
    Assert-ModuleVisibilityContract ($catalogSchema.properties.sourceWorkPackage.const -eq 'M0-DIA-09') 'DIA-09 catalog schema work package drifted.'
    Assert-ModuleVisibilityContract ($actual.executionStatus -eq 'InProgress' -and $actual.gateStatus -eq 'Blocked' -and $actual.blockerCode -eq 'EvidenceMissing') 'DIA-09 gate status was incorrectly advanced.'
    Assert-ModuleVisibilityContract ($actual.result -eq 'Partial') 'DIA-09 must not claim ownership or D2 completion.'
    Assert-ModuleVisibilityContract ($actual.coverage.unresolvedOwnershipInputCount -eq 131) 'Unexpected unresolved ownership input count.'
    Assert-ModuleVisibilityContract ($actual.coverage.v24VisibleCandidateCount -eq 123) 'Unexpected v24-visible candidate count.'
    Assert-ModuleVisibilityContract ($actual.coverage.snakeOnlyCandidateCount -eq 8) 'Unexpected Snake-only candidate count.'
    Assert-ModuleVisibilityContract ($actual.coverage.missingSnakeProjectionCount -eq 0) 'Unexpected unresolved key without Snake projection.'
    Assert-ModuleVisibilityContract ($actual.currentRuntimeIsolation.status -eq 'Failed') 'Legacy runtime isolation was incorrectly advanced.'
    Assert-ModuleVisibilityContract ($actual.parserVmConsumption.status -eq 'NotConsumed') 'DIA-09 was incorrectly wired into legacy Parser/VM.'
    Assert-ModuleVisibilityContract (@($actual.entries | Where-Object ownershipEvidenceStatus -ne 'Unresolved').Count -eq 0) 'Visibility evidence incorrectly resolved ownership.'
    Assert-ModuleVisibilityContract (@($actual.entries | Where-Object aliasStatus -ne 'Unresolved').Count -eq 0) 'Visibility evidence incorrectly resolved aliases.'
    Assert-ModuleVisibilityContract (@($actual.entries | Where-Object replacementStatus -ne 'Unresolved').Count -eq 0) 'Visibility evidence incorrectly resolved replacements.'
    Assert-ModuleVisibilityContract (($actual.entries | Where-Object { $_.registryKind -eq 'Instruction' -and $_.publicKey -eq 'CALLSHARP' }).visibilityCandidate -eq 'SnakeOnlyCandidate') 'CALLSHARP lost its Snake-only projection evidence.'
    Assert-ModuleVisibilityContract (($actual.entries | Where-Object { $_.registryKind -eq 'Instruction' -and $_.publicKey -eq 'TINPUTNF' }).visibilityCandidate -eq 'V24VisibleCandidate') 'TINPUTNF lost its v24 projection evidence.'
    Assert-ModuleVisibilityContract (@($actual.entries | Where-Object { $_.registryKind -eq 'ExpressionFunction' -and $_.visibilityCandidate -eq 'SnakeOnlyCandidate' }).Count -eq 2) 'Snake-only expression function projection evidence drifted.'
    Assert-ModuleVisibilityContract (($actual.entries | Where-Object { $_.registryKind -eq 'ExpressionFunction' -and $_.publicKey -eq 'ACOS' }).visibilityCandidate -eq 'V24VisibleCandidate') 'ACOS lost its v24 projection evidence.'

    $syntheticSnapshots = [pscustomobject]@{
        workPackage='M0-DIA-02'; snapshotSetHash=('1' * 64)
        testProjectionInvariant=[pscustomobject]@{status='Passed'}; currentRuntimeIsolation=[pscustomobject]@{status='Failed'}
        profiles=[pscustomobject]@{
            v24=[pscustomobject]@{
                instructions=@([pscustomobject]@{publicKey='COMMON';moduleId='legacy.current.common';currentContribution='common'})
                expressionFunctions=@([pscustomobject]@{publicKey='COMMON_FN';moduleId='legacy.current.expression';currentContribution='expression'})
            }
            snake=[pscustomobject]@{
                instructions=@(
                    [pscustomobject]@{publicKey='COMMON';moduleId='legacy.current.common';currentContribution='common'},
                    [pscustomobject]@{publicKey='SNAKE_ONLY';moduleId='game.snake';currentContribution='snake'})
                expressionFunctions=@([pscustomobject]@{publicKey='COMMON_FN';moduleId='legacy.current.expression';currentContribution='expression'})
            }
        }
    }
    $syntheticOwnership = [pscustomobject]@{
        workPackage='M0-DIA-07'; sourceRegistrySnapshotHash=('1' * 64); evidenceSetHash=('2' * 64); currentRuntimeIsolation=[pscustomobject]@{status='Failed'}
        instructions=@(
            [pscustomobject]@{publicKey='COMMON';registryKind='Instruction';currentTargetModule='legacy.common.unresolved';currentHandler='Common';ownershipEvidenceStatus='Unresolved';aliasStatus='Unresolved';replacementStatus='Unresolved';behaviorCompatibilityStatus='Uncovered';completionEffectStatus='Uncovered'},
            [pscustomobject]@{publicKey='SNAKE_ONLY';registryKind='Instruction';currentTargetModule='game.snake.candidate';currentHandler='Snake';ownershipEvidenceStatus='Unresolved';aliasStatus='Unresolved';replacementStatus='Unresolved';behaviorCompatibilityStatus='Uncovered';completionEffectStatus='Uncovered'})
        expressionFunctions=@([pscustomobject]@{publicKey='COMMON_FN';registryKind='ExpressionFunction';currentTargetModule='legacy.expression.unresolved';currentHandler='CommonFn';ownershipEvidenceStatus='Unresolved';aliasStatus='Unresolved';replacementStatus='Unresolved';behaviorCompatibilityStatus='Uncovered';completionEffectStatus='Uncovered'})
    }
    $syntheticLookup = [pscustomobject]@{
        workPackage='M0-DIA-08'; sourceOwnershipEvidenceHash=('2' * 64); contractSetHash=('3' * 64)
        currentRuntimeIsolation=[pscustomobject]@{status='Failed'}; parserVmConsumption=[pscustomobject]@{status='NotConsumed'}
        entries=@(
            [pscustomobject]@{publicKey='COMMON';registryKind='Instruction';lookupContract='InstructionConditionalICVariable';instructionProjection='DirectInstruction'},
            [pscustomobject]@{publicKey='SNAKE_ONLY';registryKind='Instruction';lookupContract='InstructionConditionalICVariable';instructionProjection='DirectInstruction'},
            [pscustomobject]@{publicKey='COMMON_FN';registryKind='ExpressionFunction';lookupContract='ExpressionOrdinalWithConditionalCurrentCultureUpper';instructionProjection='ProjectedAsMethodInstruction'})
    }
    $syntheticCatalog = Copy-ModuleVisibilityObject $catalog
    $syntheticCatalog.expected.unresolvedOwnershipCount = 3
    $syntheticCatalog.expected.unresolvedInstructionCount = 2
    $syntheticCatalog.expected.unresolvedExpressionFunctionCount = 1
    $syntheticCatalog.expected.v24VisibleCandidateCount = 2
    $syntheticCatalog.expected.snakeOnlyCandidateCount = 1
    $syntheticCatalog.expected.missingSnakeProjectionCount = 0
    $syntheticCatalog.expected.v24VisibleInstructionCount = 1
    $syntheticCatalog.expected.snakeOnlyInstructionCount = 1
    $syntheticCatalog.expected.v24VisibleExpressionFunctionCount = 1
    $syntheticCatalog.expected.snakeOnlyExpressionFunctionCount = 0

    $synthetic = New-DialectModuleVisibilityReport -RegistrySnapshot $syntheticSnapshots -OwnershipEvidence $syntheticOwnership -NameLookupContract $syntheticLookup -Catalog $syntheticCatalog
    Assert-ModuleVisibilityContract (($synthetic.entries | Where-Object publicKey -eq 'COMMON').visibilityCandidate -eq 'V24VisibleCandidate') 'Synthetic common key was not v24-visible.'
    Assert-ModuleVisibilityContract (($synthetic.entries | Where-Object publicKey -eq 'SNAKE_ONLY').visibilityCandidate -eq 'SnakeOnlyCandidate') 'Synthetic Snake key was not Snake-only.'
    Assert-ModuleVisibilityContract ($synthetic.entries.Count -eq 3) 'Synthetic visibility entry count drifted.'

    $reorderedSnapshots = Copy-ModuleVisibilityObject $syntheticSnapshots
    [array]::Reverse($reorderedSnapshots.profiles.snake.instructions)
    $reorderedOwnership = Copy-ModuleVisibilityObject $syntheticOwnership
    [array]::Reverse($reorderedOwnership.instructions)
    $reorderedLookup = Copy-ModuleVisibilityObject $syntheticLookup
    [array]::Reverse($reorderedLookup.entries)
    $reordered = New-DialectModuleVisibilityReport -RegistrySnapshot $reorderedSnapshots -OwnershipEvidence $reorderedOwnership -NameLookupContract $reorderedLookup -Catalog $syntheticCatalog
    Assert-ModuleVisibilityContract ($synthetic.visibilitySetHash -eq $reordered.visibilitySetHash) 'Enumeration order changed the canonical visibility hash.'

    $staleOwnership = Copy-ModuleVisibilityObject $syntheticOwnership
    $staleOwnership.sourceRegistrySnapshotHash = ('9' * 64)
    Assert-ModuleVisibilityThrows { New-DialectModuleVisibilityReport $syntheticSnapshots $staleOwnership $syntheticLookup $syntheticCatalog } 'DIA-07.*DIA-02' 'Stale DIA-07/DIA-02 chain was accepted.'

    $duplicateSnapshots = Copy-ModuleVisibilityObject $syntheticSnapshots
    $duplicateSnapshots.profiles.v24.instructions += [pscustomobject]@{publicKey='COMMON';moduleId='duplicate';currentContribution='duplicate'}
    Assert-ModuleVisibilityThrows { New-DialectModuleVisibilityReport $duplicateSnapshots $syntheticOwnership $syntheticLookup $syntheticCatalog } 'Duplicate v24 instruction projection key' 'Duplicate v24 projection key was accepted.'

    $missingSnakeSnapshots = Copy-ModuleVisibilityObject $syntheticSnapshots
    $missingSnakeSnapshots.profiles.snake.instructions = @($missingSnakeSnapshots.profiles.snake.instructions | Where-Object publicKey -ne 'SNAKE_ONLY')
    Assert-ModuleVisibilityThrows { New-DialectModuleVisibilityReport $missingSnakeSnapshots $syntheticOwnership $syntheticLookup $syntheticCatalog } 'Snake instruction projection key set count mismatch|missing from Snake projection' 'Unresolved key missing from Snake projection was accepted.'

    $wrongCountsCatalog = Copy-ModuleVisibilityObject $syntheticCatalog
    $wrongCountsCatalog.expected.v24VisibleCandidateCount = 1
    Assert-ModuleVisibilityThrows { New-DialectModuleVisibilityReport $syntheticSnapshots $syntheticOwnership $syntheticLookup $wrongCountsCatalog } 'v24-visible candidate count' 'Catalog visibility count drift was accepted.'

    $outputPath = Join-Path $testRoot 'dialect-module-visibility.json'
    $written = New-DialectModuleVisibilityReport -RegistrySnapshot $syntheticSnapshots -OwnershipEvidence $syntheticOwnership -NameLookupContract $syntheticLookup -Catalog $syntheticCatalog -OutputPath $outputPath
    Assert-ModuleVisibilityContract ((Test-Path -LiteralPath $outputPath -PathType Leaf)) 'DIA-09 report was not written.'
    Assert-ModuleVisibilityContract ((Read-ModuleVisibilityJson $outputPath).visibilitySetHash -eq $written.visibilitySetHash) 'Written DIA-09 report hash drifted.'

    Write-Output 'M0 dialect module visibility contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-module-visibility-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
