[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorsctionPreference = 'Stop'
$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'docs\NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectNameLookupContract.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-name-lookup-contract.json'

function Assert-LookupContract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-LookupThrows([scriptblock]$Action, [string]$Pattern, [string]$Failure) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) { throw "$Failure sctual: $($caught.Exception.Message)" }
}

function Read-LookupJson([string]$Path) {
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-LookupObject([object]$Value) {
    return ($Value | ConvertTo-Json -Depth 60 | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) { throw "Missing DIA-08 module: $modulePath" }
    Import-Module $modulePath -Force
    $catalog = Read-LookupJson $catalogPath
    $inventory = Read-LookupJson (Join-Path $generatedRoot 'dialect-inventory.json')
    $ownership = Read-LookupJson (Join-Path $generatedRoot 'dialect-ownership-evidence.json')
    $actual = New-DialectNameLookupContractReport -Inventory $inventory -OwnershipEvidence $ownership -Catalog $catalog -ProjectRoot $ProjectRoot

    Assert-LookupContract ($actual.workPackage -ceq 'M0-DIA-08') 'Unexpected lookup work package.'
    Assert-LookupContract ($actual.instructionRegistrationCount -eq 327) 'Instruction registration count drifted.'
    Assert-LookupContract ($actual.expressionRegistrationCount -eq 362) 'Expression registration count drifted.'
    Assert-LookupContract ($actual.crossRegistryCollisionCount -eq 9) 'Cross-registry collision count drifted.'
    Assert-LookupContract ($actual.instructionLookupSurfaceCount -eq 680) 'Instruction lookup surface count drifted.'
    Assert-LookupContract ($actual.currentRuntimeIsolation.status -ceq 'Failed') 'Runtime isolation was overstated.'
    Assert-LookupContract ($actual.parserVmConsumption.status -ceq 'NotConsumed') 'DIA-08 was incorrectly presented as runtime wiring.'
    Assert-LookupContract ($actual.semanticStatus.alias -ceq 'Unresolved') 'Semantic alias status was overstated.'
    Assert-LookupContract ($actual.semanticStatus.replacement -ceq 'Unresolved') 'Semantic replacement status was overstated.'
    Assert-LookupContract ($actual.instructionLookup.registryComparer.selector -ceq 'Config.ICVariable') 'Instruction comparer selector drifted.'
    Assert-LookupContract ($actual.instructionLookup.registryComparer.whenTrue -ceq 'OrdinalIgnoreCase') 'Instruction true comparer drifted.'
    Assert-LookupContract ($actual.instructionLookup.registryComparer.whenFalse -ceq 'Ordinal') 'Instruction false comparer drifted.'
    Assert-LookupContract ($actual.instructionLookup.normalizer -ceq 'Identity') 'Instruction normalizer drifted.'
    Assert-LookupContract ($actual.expressionLookup.registryComparer -ceq 'Ordinal') 'Expression comparer drifted.'
    Assert-LookupContract ($actual.expressionLookup.normalizer.whenTrue -ceq 'CurrentCultureToUpper') 'Expression normalizer drifted.'
    Assert-LookupContract ($actual.expressionLookup.normalizer.selector -ceq 'Config.ICFunction') 'Expression normalizer selector drifted.'
    Assert-LookupContract ($actual.projection.collisionPolicy -ceq 'ExistingInstructionWins') 'Projection collision policy drifted.'
    Assert-LookupContract ($actual.mutableDictionaryExposure.instructionRegistry -ceq 'ImmutableProfileSurface') 'Instruction surface exposure was missed.'
    Assert-LookupContract ($actual.mutableDictionaryExposure.expressionRegistry -ceq 'ImmutableProfileSurface') 'Expression surface exposure was missed.'
    Assert-LookupContract ($actual.sourceTextRewrite.classification -ceq 'SourceTextRewrite') 'Rename was misclassified.'
    Assert-LookupContract ($actual.sourceTextRewrite.registryAliasMechanism -ceq 'None') 'Rename was incorrectly modeled as registry alias.'
    Assert-LookupContract ($actual.sourceTextRewrite.registryReplacementMechanism -ceq 'None') 'Rename was incorrectly modeled as registry replacement.'
    Assert-LookupContract ($actual.coverage.semanticAliasUnresolvedCount -eq 689) 'Per-key semantic alias status was overstated.'
    Assert-LookupContract ($actual.coverage.semanticReplacementUnresolvedCount -eq 689) 'Per-key semantic replacement status was overstated.'
    Assert-LookupContract ($actual.keyDomain.asciiUpperPublicKeyCount -eq 687) 'ASCII uppercase public-key count drifted.'
    Assert-LookupContract ($actual.keyDomain.nonAsciiOrMixedCasePublicKeyCount -eq 2) 'Unicode public-key count drifted.'
    Assert-LookupContract (@($actual.entries | Where-Object semanticAliasStatus -cne 'Unresolved').Count -eq 0) 'An entry resolved semantic alias without evidence.'
    Assert-LookupContract (@($actual.entries | Where-Object semanticReplacementStatus -cne 'Unresolved').Count -eq 0) 'An entry resolved semantic replacement without evidence.'

    $expectedCollisions = @('BITMAP_CACHE_ENABLE','ENCODETOUNI','GETTIME','OUTPUTLOG','POWER','PRINTCPERLINE','SAVENOS','SETANIMETIMER','VARSIZE')
    Assert-LookupContract ((@($actual.collisions.publicKey) -join ',') -ceq ($expectedCollisions -join ',')) 'Collision key set drifted.'
    Assert-LookupContract (@($actual.collisions | Where-Object resolution -cne 'InstructionPrecedence').Count -eq 0) 'A collision lost instruction precedence.'
    Assert-LookupContract (@($actual.entries | Where-Object { $_.registryKind -eq 'ExpressionFunction' -and $_.instructionProjection -eq 'SuppressedByInstructionCollision' }).Count -eq 9) 'Projected expression collision count drifted.'
    Assert-LookupContract (@($actual.entries | Where-Object { $_.registryKind -eq 'ExpressionFunction' -and $_.instructionProjection -eq 'ProjectedAsMethodInstruction' }).Count -eq 353) 'Projected method instruction count drifted.'

    Assert-LookupContract (Test-LegacyInstructionLookupMatch -RegisteredKey 'PRINT' -InputName 'print' -IgnoreCaseVariable $true) 'ICVariable=true did not ignore instruction case.'
    Assert-LookupContract (-not (Test-LegacyInstructionLookupMatch -RegisteredKey 'PRINT' -InputName 'print' -IgnoreCaseVariable $false)) 'ICVariable=false ignored instruction case.'
    Assert-LookupContract (Test-LegacyInstructionLookupMatch -RegisteredKey 'PRINT' -InputName 'PRINT' -IgnoreCaseVariable $true) 'Exact instruction lookup failed with ignore-case enabled.'
    Assert-LookupContract (Test-LegacyInstructionLookupMatch -RegisteredKey 'PRINT' -InputName 'PRINT' -IgnoreCaseVariable $false) 'Exact instruction lookup failed with ignore-case disabled.'
    Assert-LookupContract ((Get-LegacyExpressionLookupKey -InputName 'rand' -IgnoreCaseFunction $true -CultureName 'en-US') -ceq 'RAND') 'ICFunction=true did not normalize an ASCII expression name.'
    Assert-LookupContract ((Get-LegacyExpressionLookupKey -InputName 'rand' -IgnoreCaseFunction $false -CultureName 'en-US') -ceq 'rand') 'ICFunction=false unexpectedly normalized an expression name.'
    Assert-LookupContract ((Get-LegacyExpressionLookupKey -InputName 'i' -IgnoreCaseFunction $true -CultureName 'tr-TR') -ceq ([string][char]0x0130)) 'Current-culture Turkish casing risk was not preserved.'
    Assert-LookupContract ((Get-LegacyExpressionLookupKey -InputName 'i' -IgnoreCaseFunction $true -CultureName 'en-US') -ceq 'I') 'English casing model drifted.'

    $reorderedInventory = Copy-LookupObject $inventory
    $reorderedInventory.instructionRegistrations = @($reorderedInventory.instructionRegistrations | Sort-Object publicKey -Descending)
    $reorderedInventory.expressionRegistrations = @($reorderedInventory.expressionRegistrations | Sort-Object publicKey -Descending)
    $reorderedCatalog = Copy-LookupObject $catalog
    $reorderedCatalog.sourceFiles = @($reorderedCatalog.sourceFiles | Sort-Object id -Descending)
    $reorderedCatalog.expectedCollisionKeys = @($reorderedCatalog.expectedCollisionKeys | Sort-Object -Descending)
    $reordered = New-DialectNameLookupContractReport $reorderedInventory $ownership $reorderedCatalog $ProjectRoot
    Assert-LookupContract ($reordered.contractSetHash -ceq $actual.contractSetHash) 'Enumeration order changed the lookup contract hash.'

    $duplicate = Copy-LookupObject $inventory
    $duplicate.instructionRegistrations = @($duplicate.instructionRegistrations) + @(Copy-LookupObject $duplicate.instructionRegistrations[0])
    Assert-LookupThrows { New-DialectNameLookupContractReport $duplicate $ownership $catalog $ProjectRoot } 'Duplicate instruction public key' 'Duplicate instruction key was accepted.'

    $stale = Copy-LookupObject $ownership
    $stale.sourceInventoryHash = ('9' * 64)
    Assert-LookupThrows { New-DialectNameLookupContractReport $inventory $stale $catalog $ProjectRoot } 'DIA-07 does not match DIA-01' 'A stale DIA-07 input was accepted.'

    $sourceDrift = Copy-LookupObject $catalog
    $sourceDrift.sourceFiles[0].sha256 = ('0' * 64)
    Assert-LookupThrows { New-DialectNameLookupContractReport $inventory $ownership $sourceDrift $ProjectRoot } 'source hash drifted' 'Source drift was accepted.'

    $duplicateCollision = Copy-LookupObject $catalog
    $duplicateCollision.expectedCollisionKeys = @($duplicateCollision.expectedCollisionKeys) + @($duplicateCollision.expectedCollisionKeys[0])
    Assert-LookupThrows { New-DialectNameLookupContractReport $inventory $ownership $duplicateCollision $ProjectRoot } 'Duplicate expected collision key' 'Duplicate collision catalog entry was accepted.'

    $reportSchema = Read-LookupJson (Join-Path $toolRoot 'dialect-name-lookup-contract.schema.json')
    $catalogSchema = Read-LookupJson (Join-Path $toolRoot 'dialect-name-lookup-contract-catalog.schema.json')
    Assert-LookupContract ($reportSchema.properties.workPackage.const -ceq 'M0-DIA-08') 'Report schema work package drifted.'
    Assert-LookupContract ($catalogSchema.properties.sourceWorkPackage.const -ceq 'M0-DIA-08') 'Catalog schema work package drifted.'

    Write-Output 'M0 dialect name lookup contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
