[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolRoot = Join-Path $ProjectRoot 'tools\save-baseline'
$modulePath = Join-Path $toolRoot 'LegacySaveBaseline.psm1'
$catalogPath = Join-Path $toolRoot 'legacy-save-baseline.json'
$reportSchemaPath = Join-Path $toolRoot 'legacy-save-baseline.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'legacy-save-baseline-catalog.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-sav-baseline-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-LegacySaveBaselineContract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-LegacySaveBaselineThrows([scriptblock]$Action, [string]$Pattern, [string]$Failure) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) { throw "$Failure Actual: $($caught.Exception.Message)" }
}

function Read-LegacySaveBaselineJson([string]$Path) {
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-LegacySaveBaselineObject([object]$Value) {
    return ($Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing M0-SAV-01 module: $modulePath"
    }

    Import-Module $modulePath -Force
    $catalog = Read-LegacySaveBaselineJson $catalogPath
    $reportSchema = Read-LegacySaveBaselineJson $reportSchemaPath
    $catalogSchema = Read-LegacySaveBaselineJson $catalogSchemaPath
    $actual = New-LegacySaveBaselineReport -ProjectRoot $ProjectRoot -Catalog $catalog

    Assert-LegacySaveBaselineContract ($actual.workPackage -ceq 'M0-SAV-01') 'Unexpected M0-SAV-01 work package.'
    Assert-LegacySaveBaselineContract ($actual.executionStatus -ceq 'InProgress' -and $actual.gateStatus -ceq 'Blocked' -and $actual.blockerCode -ceq 'EvidenceMissing' -and $actual.result -ceq 'Partial') 'M0-SAV-01 status was incorrectly advanced.'
    Assert-LegacySaveBaselineContract ($actual.saveBaselineSetHash -match '^[0-9a-f]{64}$') 'M0-SAV-01 baseline hash is invalid.'
    Assert-LegacySaveBaselineContract ($reportSchema.properties.workPackage.const -ceq 'M0-SAV-01') 'M0-SAV-01 report schema drifted.'
    Assert-LegacySaveBaselineContract ($catalogSchema.properties.sourceWorkPackage.const -ceq 'M0-SAV-01') 'M0-SAV-01 catalog schema drifted.'
    Assert-LegacySaveBaselineContract ($actual.protocolCounts.sourceFileCount -eq 5 -and $actual.protocolCounts.fileTypeCount -eq 4 -and $actual.protocolCounts.dataTypeCount -eq 19 -and $actual.protocolCounts.sparseMarkerCount -eq 11 -and $actual.protocolCounts.profileCandidateCount -eq 2 -and $actual.protocolCounts.legacyMutationEvidenceCount -eq 2) 'M0-SAV-01 protocol count drifted.'
    Assert-LegacySaveBaselineContract (($actual.header.headerHex -ceq '0x0A1A0A0D41524589') -and ($actual.header.zipHeaderHex -ceq '0x0A50495A41524589') -and ($actual.header.formatVersion -eq 1808) -and ($actual.header.minimumBytes -eq 16) -and ($actual.header.encoding -ceq 'Encoding.Unicode')) 'M0-SAV-01 header facts drifted.'
    Assert-LegacySaveBaselineContract ((@($actual.fileTypes | ForEach-Object id) -join ',') -ceq 'Normal,Global,Var,CharVar') 'M0-SAV-01 file type order drifted.'
    Assert-LegacySaveBaselineContract ((@($actual.dataTypes | Where-Object id -ceq 'FloatArray2D')[0].hexCode -ceq '0x22') -and (@($actual.dataTypes | Where-Object id -ceq 'PcFloat')[0].hexCode -ceq '0x04')) 'M0-SAV-01 float type facts drifted.'
    Assert-LegacySaveBaselineContract ((@($actual.profileConflict.upstreamConflictingCodes) -join ',') -ceq '0x20,0x21,0x22') 'M0-SAV-01 profile conflict facts drifted.'
    Assert-LegacySaveBaselineContract ($actual.profileConflict.automaticSelectionStatus -ceq 'Unbound') 'M0-SAV-01 incorrectly selected a save profile.'
    Assert-LegacySaveBaselineContract ($actual.fixtureEvidence.status -ceq 'Uncovered' -and $actual.fixtureEvidence.sourceSaveFilesRead -eq 0 -and $actual.fixtureEvidence.offsetMapStatus -ceq 'Uncovered' -and $actual.fixtureEvidence.roundTripStatus -ceq 'Uncovered') 'M0-SAV-01 incorrectly claimed fixture evidence.'
    Assert-LegacySaveBaselineContract ($actual.saveProfileRuntime.status -ceq 'NotImplemented' -and $actual.candidateParseCommit.status -ceq 'NotImplemented' -and $actual.m1Eligibility.status -ceq 'Blocked') 'M0-SAV-01 incorrectly advanced runtime architecture.'
    Assert-LegacySaveBaselineContract (@($actual.legacyMutationEvidence | Where-Object { $_.status -cne 'ObservedStatic' }).Count -eq 0) 'M0-SAV-01 mutation evidence drifted.'

    $snapshot = @($actual.fileTypes | ForEach-Object { $_.id + ':' + $_.hexCode }) -join '|'
    $catalog.expectedFileTypes[0].id = 'MUTATED_AFTER_REPORT'
    Assert-LegacySaveBaselineContract ((@($actual.fileTypes | ForEach-Object { $_.id + ':' + $_.hexCode }) -join '|') -ceq $snapshot) 'M0-SAV-01 retained a mutable catalog reference.'
    $catalog = Read-LegacySaveBaselineJson $catalogPath

    $reorderedCatalog = Copy-LegacySaveBaselineObject $catalog
    [array]::Reverse($reorderedCatalog.sources)
    [array]::Reverse($reorderedCatalog.expectedFileTypes)
    [array]::Reverse($reorderedCatalog.expectedDataTypes)
    [array]::Reverse($reorderedCatalog.expectedSparseMarkers)
    [array]::Reverse($reorderedCatalog.profileConflict.candidateSaveProfileIds)
    $reordered = New-LegacySaveBaselineReport -ProjectRoot $ProjectRoot -Catalog $reorderedCatalog
    Assert-LegacySaveBaselineContract ($reordered.saveBaselineSetHash -ceq $actual.saveBaselineSetHash) 'M0-SAV-01 catalog enumeration order changed the baseline hash.'

    $staleSourceCatalog = Copy-LegacySaveBaselineObject $catalog
    $staleSourceCatalog.sources[0].sha256 = ('0' * 64)
    Assert-LegacySaveBaselineThrows { New-LegacySaveBaselineReport -ProjectRoot $ProjectRoot -Catalog $staleSourceCatalog } 'source hash drifted' 'M0-SAV-01 accepted stale source evidence.'

    $invalidProfileCatalog = Copy-LegacySaveBaselineObject $catalog
    $invalidProfileCatalog.profileConflict.automaticSelectionStatus = 'Selected'
    Assert-LegacySaveBaselineThrows { New-LegacySaveBaselineReport -ProjectRoot $ProjectRoot -Catalog $invalidProfileCatalog } 'automatic selection status drifted' 'M0-SAV-01 accepted automatic profile selection.'

    $missingTypeCatalog = Copy-LegacySaveBaselineObject $catalog
    $missingTypeCatalog.expectedDataTypes = @($missingTypeCatalog.expectedDataTypes | Where-Object id -cne 'FloatArray3D')
    Assert-LegacySaveBaselineThrows { New-LegacySaveBaselineReport -ProjectRoot $ProjectRoot -Catalog $missingTypeCatalog } 'data type count drifted' 'M0-SAV-01 accepted an incomplete data type inventory.'

    $outputPath = Join-Path $testRoot 'legacy-save-baseline.json'
    $written = New-LegacySaveBaselineReport -ProjectRoot $ProjectRoot -Catalog $catalog -OutputPath $outputPath
    Assert-LegacySaveBaselineContract (Test-Path -LiteralPath $outputPath -PathType Leaf) 'M0-SAV-01 report was not written.'
    Assert-LegacySaveBaselineContract ((Read-LegacySaveBaselineJson $outputPath).saveBaselineSetHash -ceq $written.saveBaselineSetHash) 'M0-SAV-01 written report hash drifted.'

    Write-Output 'M0 legacy save baseline contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
finally {
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\\') + '\\'
    $resolvedTest = [IO.Path]::GetFullPath($testRoot).TrimEnd('\\') + '\\'
    if ($resolvedTest.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and $resolvedTest.Contains('gemuera-m0-sav-baseline-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
