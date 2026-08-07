[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'docs\NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectModuleComposition.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-module-composition.json'
$reportSchemaPath = Join-Path $toolRoot 'dialect-module-composition.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'dialect-module-composition-catalog.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-module-composition-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-ModuleCompositionContract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-ModuleCompositionThrows([scriptblock]$Action, [string]$Pattern, [string]$Failure) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) { throw "$Failure Actual: $($caught.Exception.Message)" }
}

function Read-ModuleCompositionJson([string]$Path) {
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-ModuleCompositionObject([object]$Value) {
    return ($Value | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing DIA-16 module: $modulePath"
    }

    Import-Module $modulePath -Force
    $compatibilityPack = Read-ModuleCompositionJson (Join-Path $generatedRoot 'dialect-compatibility-pack.json')
    $policySurface = Read-ModuleCompositionJson (Join-Path $generatedRoot 'dialect-policy-surface.json')
    $catalog = Read-ModuleCompositionJson $catalogPath
    $reportSchema = Read-ModuleCompositionJson $reportSchemaPath
    $catalogSchema = Read-ModuleCompositionJson $catalogSchemaPath

    $actual = New-DialectModuleCompositionReport -CompatibilityPack $compatibilityPack -PolicySurface $policySurface -Catalog $catalog
    Assert-ModuleCompositionContract ($actual.workPackage -ceq 'M0-DIA-16') 'Unexpected DIA-16 work package.'
    Assert-ModuleCompositionContract ($actual.executionStatus -ceq 'InProgress' -and $actual.gateStatus -ceq 'Blocked' -and $actual.blockerCode -ceq 'EvidenceMissing') 'DIA-16 status was incorrectly advanced.'
    Assert-ModuleCompositionContract ($actual.result -ceq 'Partial') 'DIA-16 must remain Partial.'
    Assert-ModuleCompositionContract ($actual.moduleCompositionSetHash -match '^[0-9a-f]{64}$') 'DIA-16 composition set hash is invalid.'
    Assert-ModuleCompositionContract ($actual.sourceCompatibilityPackHash -ceq $compatibilityPack.compatibilityPackSetHash) 'DIA-16 did not pin DIA-12 evidence.'
    Assert-ModuleCompositionContract ($actual.sourcePolicySurfaceHash -ceq $policySurface.policySurfaceSetHash) 'DIA-16 did not pin DIA-15 evidence.'
    Assert-ModuleCompositionContract ($actual.currentRuntimeIsolation.status -ceq 'Failed') 'Runtime isolation was incorrectly advanced.'
    Assert-ModuleCompositionContract ($actual.parserVmConsumption.status -ceq 'NotConsumed') 'DIA-16 was incorrectly wired into Parser/VM.'
    Assert-ModuleCompositionContract ($actual.compatibilityPlanRuntime.status -ceq 'NotImplemented') 'DIA-16 was incorrectly presented as a runtime CompatibilityPlan.'
    Assert-ModuleCompositionContract ($actual.resolverRuntime.status -ceq 'NotImplemented') 'DIA-16 was incorrectly presented as a runtime resolver.'
    Assert-ModuleCompositionContract ($actual.runtimeModuleCatalog.status -ceq 'NotImplemented') 'DIA-16 was incorrectly presented as a runtime module catalog.'
    Assert-ModuleCompositionContract ($actual.m1Eligibility.status -ceq 'Blocked') 'DIA-16 incorrectly advanced M1 eligibility.'
    Assert-ModuleCompositionContract ($reportSchema.properties.workPackage.const -ceq 'M0-DIA-16') 'DIA-16 report schema work package drifted.'
    Assert-ModuleCompositionContract ($catalogSchema.properties.sourceWorkPackage.const -ceq 'M0-DIA-16') 'DIA-16 catalog schema work package drifted.'

    Assert-ModuleCompositionContract ($actual.compositionCounts.moduleDescriptorCount -eq 2 -and $actual.compositionCounts.dependencyEdgeCount -eq 1 -and $actual.compositionCounts.portDeclarationCount -eq 10 -and $actual.compositionCounts.profileClosureCount -eq 2) 'DIA-16 composition count drifted.'
    Assert-ModuleCompositionContract ($actual.currentCompatibilityPackExposure.status -ceq 'NoRuntimeCapabilitiesDeclared') 'DIA-16 accepted runtime capability exposure.'
    Assert-ModuleCompositionContract ((@($actual.currentCompatibilityPackExposure.allowedCapabilityIds).Count) -eq 0) 'DIA-16 capability exposure must be empty.'

    $moduleIds = @($actual.modules | ForEach-Object moduleId)
    Assert-ModuleCompositionContract (($moduleIds -join ',') -ceq 'gemuera.v24,game.snake') 'DIA-16 module order drifted.'
    $v24 = @($actual.modules | Where-Object moduleId -ceq 'gemuera.v24')
    $snake = @($actual.modules | Where-Object moduleId -ceq 'game.snake')
    Assert-ModuleCompositionContract ($v24.Count -eq 1 -and (@($v24[0].declaredPortTypeIds).Count -eq 0) -and (@($v24[0].dependencyModuleIds).Count -eq 0)) 'DIA-16 v24 descriptor drifted.'
    Assert-ModuleCompositionContract ($snake.Count -eq 1 -and (@($snake[0].declaredPortTypeIds).Count -eq 10) -and ((@($snake[0].dependencyModuleIds) -join ',') -ceq 'gemuera.v24')) 'DIA-16 Snake descriptor drifted.'
    $v24Profile = @($actual.profileClosures | Where-Object profileId -ceq 'v24pure')
    $snakeProfile = @($actual.profileClosures | Where-Object profileId -ceq 'snake')
    Assert-ModuleCompositionContract ($v24Profile.Count -eq 1 -and ((@($v24Profile[0].resolvedModuleIds) -join ',') -ceq 'gemuera.v24')) 'DIA-16 v24 profile closure drifted.'
    Assert-ModuleCompositionContract ($snakeProfile.Count -eq 1 -and ((@($snakeProfile[0].resolvedModuleIds) -join ',') -ceq 'gemuera.v24,game.snake')) 'DIA-16 Snake profile closure drifted.'
    Assert-ModuleCompositionContract (@($actual.modules | Where-Object { $_.descriptorStatus -cne 'StaticCandidate' -or $_.runtimeStatus -cne 'NotImplemented' -or $_.distributionEligibility -cne 'Blocked' }).Count -eq 0) 'DIA-16 incorrectly advanced a module descriptor.'

    $snapshot = @($actual.modules | ForEach-Object { $_.moduleId + ':' + ($_.declaredPortTypeIds -join ',') })
    $compatibilityPack.packs[0].builtInModuleIds[0] = 'MUTATED_AFTER_COMPOSITION_REPORT'
    $policySurface.contracts[0].portTypeId = 'MUTATED_AFTER_COMPOSITION_REPORT'
    Assert-ModuleCompositionContract ((@($actual.modules | ForEach-Object { $_.moduleId + ':' + ($_.declaredPortTypeIds -join ',') }) -join '|') -ceq ($snapshot -join '|')) 'DIA-16 retained a mutable reference to source evidence.'
    $compatibilityPack = Read-ModuleCompositionJson (Join-Path $generatedRoot 'dialect-compatibility-pack.json')
    $policySurface = Read-ModuleCompositionJson (Join-Path $generatedRoot 'dialect-policy-surface.json')

    $reorderedCatalog = Copy-ModuleCompositionObject $catalog
    [array]::Reverse($reorderedCatalog.modules)
    foreach ($module in @($reorderedCatalog.modules)) { [array]::Reverse($module.declaredPortTypeIds); [array]::Reverse($module.dependencies) }
    $reordered = New-DialectModuleCompositionReport $compatibilityPack $policySurface $reorderedCatalog
    Assert-ModuleCompositionContract ($reordered.moduleCompositionSetHash -ceq $actual.moduleCompositionSetHash) 'Catalog enumeration order changed the DIA-16 composition hash.'

    $stalePackCatalog = Copy-ModuleCompositionObject $catalog
    $stalePackCatalog.sourceCompatibilityPackHash = ('0' * 64)
    Assert-ModuleCompositionThrows { New-DialectModuleCompositionReport $compatibilityPack $policySurface $stalePackCatalog } 'source CompatibilityPack hash drifted' 'DIA-16 accepted stale DIA-12 evidence.'

    $unknownModuleCatalog = Copy-ModuleCompositionObject $catalog
    $unknownModuleCatalog.modules[1].dependencies[0].moduleId = 'unknown.future.module'
    Assert-ModuleCompositionThrows { New-DialectModuleCompositionReport $compatibilityPack $policySurface $unknownModuleCatalog } 'references unknown dependency module' 'DIA-16 accepted an unknown module dependency.'

    $duplicatePortCatalog = Copy-ModuleCompositionObject $catalog
    $duplicatePortCatalog.modules[0].declaredPortTypeIds = @($duplicatePortCatalog.modules[1].declaredPortTypeIds[0])
    Assert-ModuleCompositionThrows { New-DialectModuleCompositionReport $compatibilityPack $policySurface $duplicatePortCatalog } 'Duplicate DIA-16 port declaration' 'DIA-16 accepted duplicate port ownership.'

    $cycleCatalog = Copy-ModuleCompositionObject $catalog
    $cycleCatalog.modules[0].dependencies = @([pscustomobject]@{ moduleId = 'game.snake'; versionRange = '[1.0.0,2.0.0)' })
    Assert-ModuleCompositionThrows { New-DialectModuleCompositionReport $compatibilityPack $policySurface $cycleCatalog } 'dependency cycle' 'DIA-16 accepted a module dependency cycle.'

    $exposedPack = Copy-ModuleCompositionObject $compatibilityPack
    $exposedPack.declarationPolicy.allowedCapabilityIds = @('compatibility.plan.selection.v1')
    Assert-ModuleCompositionThrows { New-DialectModuleCompositionReport $exposedPack $policySurface $catalog } 'must retain no runtime capability exposure' 'DIA-16 accepted DIA-12 runtime capability exposure.'

    $outputPath = Join-Path $testRoot 'dialect-module-composition.json'
    $written = New-DialectModuleCompositionReport -CompatibilityPack $compatibilityPack -PolicySurface $policySurface -Catalog $catalog -OutputPath $outputPath
    Assert-ModuleCompositionContract (Test-Path -LiteralPath $outputPath -PathType Leaf) 'DIA-16 report was not written.'
    Assert-ModuleCompositionContract ((Read-ModuleCompositionJson $outputPath).moduleCompositionSetHash -ceq $written.moduleCompositionSetHash) 'Written DIA-16 report hash drifted.'

    Write-Output 'M0 dialect module composition contract tests passed.'
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
    if ($resolvedTest.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        $resolvedTest.Contains('gemuera-m0-dia-module-composition-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
