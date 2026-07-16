[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectPlanPreflight.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-plan-preflight.json'
$schemaPath = Join-Path $toolRoot 'dialect-plan-preflight.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'dialect-plan-preflight-catalog.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-plan-preflight-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-PlanPreflightContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-PlanPreflightThrows {
    param([scriptblock]$Action, [string]$Pattern, [string]$Failure)

    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) {
        throw "$Failure Actual: $($caught.Exception.Message)"
    }
}

function Read-PlanPreflightJson {
    param([string]$Path)
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-PlanPreflightObject {
    param([object]$Value)
    return ($Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing DIA-10 module: $modulePath"
    }

    Import-Module $modulePath -Force
    $catalog = Read-PlanPreflightJson $catalogPath
    $schema = Read-PlanPreflightJson $schemaPath
    $catalogSchema = Read-PlanPreflightJson $catalogSchemaPath
    $registrySnapshot = Read-PlanPreflightJson (Join-Path $generatedRoot 'dialect-registry-snapshots.json')
    $sessionInventory = Read-PlanPreflightJson (Join-Path $generatedRoot 'legacy-session-state-inventory.json')

    $actual = New-DialectPlanPreflightReport -RegistrySnapshot $registrySnapshot -SessionInventory $sessionInventory -Catalog $catalog
    Assert-PlanPreflightContract ($actual.workPackage -eq 'M0-DIA-10') 'Unexpected DIA-10 work package.'
    Assert-PlanPreflightContract ($schema.properties.workPackage.const -eq 'M0-DIA-10') 'DIA-10 report schema work package drifted.'
    Assert-PlanPreflightContract ($catalogSchema.properties.sourceWorkPackage.const -eq 'M0-DIA-10') 'DIA-10 catalog schema work package drifted.'
    Assert-PlanPreflightContract ($actual.executionStatus -eq 'InProgress' -and $actual.gateStatus -eq 'Blocked' -and $actual.blockerCode -eq 'EvidenceMissing') 'DIA-10 gate status was incorrectly advanced.'
    Assert-PlanPreflightContract ($actual.result -eq 'Partial') 'DIA-10 must not claim D1 completion.'
    Assert-PlanPreflightContract ($actual.profiles.Count -eq 2) 'DIA-10 must only expose the two evidence-backed legacy profiles.'
    Assert-PlanPreflightContract ($actual.currentRuntimeIsolation.status -eq 'Failed') 'Legacy runtime isolation was incorrectly advanced.'
    Assert-PlanPreflightContract ($actual.parserVmConsumption.status -eq 'NotConsumed') 'DIA-10 was incorrectly wired into Parser/VM.'
    Assert-PlanPreflightContract ($actual.compatibilityPlanRuntime.status -eq 'NotImplemented') 'DIA-10 was incorrectly presented as a runtime CompatibilityPlan.'
    Assert-PlanPreflightContract ($actual.m1Eligibility.status -eq 'Blocked') 'DIA-10 incorrectly advanced M1 eligibility.'
    Assert-PlanPreflightContract ($actual.preflightSetHash -match '^[0-9a-f]{64}$') 'DIA-10 preflight set hash is invalid.'

    $v24 = @($actual.profiles | Where-Object profileId -eq 'v24pure')
    $snake = @($actual.profiles | Where-Object profileId -eq 'snake')
    Assert-PlanPreflightContract ($v24.Count -eq 1 -and $snake.Count -eq 1) 'DIA-10 profile identities drifted.'
    Assert-PlanPreflightContract ($v24[0].legacyCoreProfileEnum -eq 'V24Pure' -and $v24[0].registryEvidence.registryProjectionId -eq 'v24') 'v24 legacy projection mapping drifted.'
    Assert-PlanPreflightContract ($snake[0].legacyCoreProfileEnum -eq 'Snake' -and $snake[0].registryEvidence.registryProjectionId -eq 'snake') 'Snake legacy projection mapping drifted.'
    Assert-PlanPreflightContract ($v24[0].registryEvidence.instructionCount -eq 290 -and $v24[0].registryEvidence.expressionFunctionCount -eq 358) 'Unexpected v24 static plan counts.'
    Assert-PlanPreflightContract ($snake[0].registryEvidence.instructionCount -eq 326 -and $snake[0].registryEvidence.expressionFunctionCount -eq 360) 'Unexpected Snake static plan counts.'
    Assert-PlanPreflightContract ($v24[0].planSemanticHash -ne $snake[0].planSemanticHash) 'v24 and Snake plan semantic hashes must differ.'
    Assert-PlanPreflightContract (@($v24[0].registryEvidence.selectedTestProjectionModuleIds | Where-Object { $_ -eq 'game.snake' }).Count -eq 0) 'Snake test module leaked into v24 preflight.'
    Assert-PlanPreflightContract (@($snake[0].registryEvidence.selectedTestProjectionModuleIds | Where-Object { $_ -eq 'game.snake' }).Count -eq 1) 'Snake test module is missing from Snake preflight.'
    Assert-PlanPreflightContract (@($actual.unsupportedLegacyCoreProfiles | Where-Object { $_.legacyCoreProfileEnum -eq 'SnakeModernMobile' -and $_.status -eq 'Uncovered' }).Count -eq 1) 'SnakeModernMobile must remain explicitly uncovered.'

    $v24A = New-LegacyDialectPlanPreflight -RegistrySnapshot $registrySnapshot -SessionInventory $sessionInventory -Catalog $catalog -ProfileId 'v24pure' -SelectionSource 'LegacyLauncherManual' -RequestedGeneration 7
    $snakeB = New-LegacyDialectPlanPreflight -RegistrySnapshot $registrySnapshot -SessionInventory $sessionInventory -Catalog $catalog -ProfileId 'snake' -SelectionSource 'M0RunnerFixture' -RequestedGeneration 8
    $v24Again = New-LegacyDialectPlanPreflight -RegistrySnapshot $registrySnapshot -SessionInventory $sessionInventory -Catalog $catalog -ProfileId 'v24pure' -SelectionSource 'StaticContract' -RequestedGeneration 9
    Assert-PlanPreflightContract ($v24A.planSemanticHash -eq $v24Again.planSemanticHash) 'A-to-B-to-A changed the v24 plan semantic hash.'
    Assert-PlanPreflightContract ($v24A.requestMetadata.requestedGeneration -eq 7 -and $v24Again.requestMetadata.requestedGeneration -eq 9) 'Generation metadata was not retained.'
    Assert-PlanPreflightContract ($v24A.planSemanticHash -ne $snakeB.planSemanticHash) 'A-to-B profiles unexpectedly shared a semantic hash.'
    Assert-PlanPreflightContract ($v24A.requestMetadata.selectionSource -eq 'LegacyLauncherManual' -and $v24Again.requestMetadata.selectionSource -eq 'StaticContract') 'Selection source metadata drifted.'

    $v24ProjectionModules = @($v24A.registryEvidence.selectedTestProjectionModuleIds)
    $registrySnapshot.profiles.v24.selectedModuleIds[0] = 'MUTATED_AFTER_PREFLIGHT'
    Assert-PlanPreflightContract (($v24A.registryEvidence.selectedTestProjectionModuleIds -join '|') -eq ($v24ProjectionModules -join '|')) 'Preflight retained a mutable reference to source registry projection.'
    $registrySnapshot = Read-PlanPreflightJson (Join-Path $generatedRoot 'dialect-registry-snapshots.json')

    $reorderedCatalog = Copy-PlanPreflightObject $catalog
    [array]::Reverse($reorderedCatalog.profiles)
    $reordered = New-DialectPlanPreflightReport -RegistrySnapshot $registrySnapshot -SessionInventory $sessionInventory -Catalog $reorderedCatalog
    Assert-PlanPreflightContract ($actual.preflightSetHash -eq $reordered.preflightSetHash) 'Catalog enumeration order changed the DIA-10 preflight hash.'

    Assert-PlanPreflightThrows {
        New-LegacyDialectPlanPreflight $registrySnapshot $sessionInventory $catalog 'snake-modern-mobile' 'StaticContract' 0
    } 'Unsupported legacy profile.*SnakeModernMobile' 'SnakeModernMobile was accepted without evidence-backed projection.'

    $staleRegistry = Copy-PlanPreflightObject $registrySnapshot
    $staleRegistry.profiles.v24.sourceInventoryHash = ('0' * 64)
    Assert-PlanPreflightThrows {
        New-DialectPlanPreflightReport $staleRegistry $sessionInventory $catalog
    } 'DIA-02 profile source inventory hash' 'Stale DIA-02 profile input was accepted.'

    $advancedSession = Copy-PlanPreflightObject $sessionInventory
    $advancedSession.m1Eligibility.status = 'Passed'
    Assert-PlanPreflightThrows {
        New-DialectPlanPreflightReport $registrySnapshot $advancedSession $catalog
    } 'M1 eligibility must remain Blocked' 'DIA-10 accepted an incorrectly advanced M1 status.'

    $wrongCountsCatalog = Copy-PlanPreflightObject $catalog
    (@($wrongCountsCatalog.profiles | Where-Object profileId -eq 'v24pure'))[0].expected.instructionCount = 1
    Assert-PlanPreflightThrows {
        New-DialectPlanPreflightReport $registrySnapshot $sessionInventory $wrongCountsCatalog
    } 'expected instruction count' 'DIA-10 accepted catalog count drift.'

    $outputPath = Join-Path $testRoot 'dialect-plan-preflight.json'
    $written = New-DialectPlanPreflightReport -RegistrySnapshot $registrySnapshot -SessionInventory $sessionInventory -Catalog $catalog -OutputPath $outputPath
    Assert-PlanPreflightContract (Test-Path -LiteralPath $outputPath -PathType Leaf) 'DIA-10 report was not written.'
    Assert-PlanPreflightContract ((Read-PlanPreflightJson $outputPath).preflightSetHash -eq $written.preflightSetHash) 'Written DIA-10 report hash drifted.'

    Write-Output 'M0 dialect plan preflight contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-plan-preflight-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
