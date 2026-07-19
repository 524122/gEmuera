[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$inventoryModulePath = Join-Path $ProjectRoot 'tools\dialect-inventory\DialectInventory.psm1'
$signatureModulePath = Join-Path $ProjectRoot 'tools\dialect-inventory\DialectSignatureInventory.psm1'
$resolutionModulePath = Join-Path $ProjectRoot 'tools\dialect-inventory\DialectSignatureResolution.psm1'
$classificationPath = Join-Path $ProjectRoot 'tools\dialect-inventory\dialect-classification.json'
$catalogPath = Join-Path $ProjectRoot 'tools\dialect-inventory\instruction-signature-resolution.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-signature-resolution-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-ResolutionContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Copy-ResolutionObject {
    param([Parameter(Mandatory = $true)][object]$Value)
    return ($Value | ConvertTo-Json -Depth 30 | ConvertFrom-Json)
}

function Assert-ResolutionThrows {
    param([scriptblock]$Action, [string]$MessagePattern, [string]$FailureMessage)
    $caught = $null
    try { & $Action }
    catch { $caught = $_ }
    if ($null -eq $caught) { throw $FailureMessage }
    if ([string]$caught.Exception.Message -notmatch $MessagePattern) {
        throw "$FailureMessage Unexpected error: $($caught.Exception.Message)"
    }
}

try {
    if (-not (Test-Path -LiteralPath $resolutionModulePath -PathType Leaf)) {
        throw "Dialect signature resolution module is missing: $resolutionModulePath"
    }
    if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
        throw "Dialect signature resolution catalog is missing: $catalogPath"
    }

    Import-Module $inventoryModulePath -Force
    Import-Module $signatureModulePath -Force
    Import-Module $resolutionModulePath -Force

    $inventory = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $classificationPath
    $signatures = New-DialectSignatureInventory -ProjectRoot $ProjectRoot -Inventory $inventory
    $catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $outputPath = Join-Path $testRoot 'actual-resolution.json'
    $actual = New-DialectSignatureResolutionReport -SignatureInventory $signatures -Catalog $catalog -OutputPath $outputPath

    Assert-ResolutionContract ($actual.workPackage -eq 'M0-DIA-04') 'Unexpected signature resolution work package.'
    Assert-ResolutionContract ($actual.executionStatus -eq 'InProgress' -and $actual.gateStatus -eq 'Blocked' -and $actual.blockerCode -eq 'EvidenceMissing') 'M0 gate status was incorrectly advanced.'
    Assert-ResolutionContract ($actual.result -eq 'Partial') 'Static signature resolution must not claim behavior completion.'
    Assert-ResolutionContract ($actual.sourceDescriptorSetHash -eq $signatures.descriptorSetHash) 'DIA-04 source descriptor hash mismatch.'
    Assert-ResolutionContract ($actual.instructionCount -eq 326) "Unexpected resolved instruction count: $($actual.instructionCount)."
    Assert-ResolutionContract ($actual.coverage.sourceResolvedCount -eq 214) 'Unexpected DIA-03 preserved signature count.'
    Assert-ResolutionContract ($actual.coverage.sourceConditionalCount -eq 112) 'Unexpected DIA-03 conditional signature count.'
    Assert-ResolutionContract ($actual.coverage.resolvedStaticByRuleCount -eq 112) 'Not all conditional signatures were resolved by rules.'
    Assert-ResolutionContract ($actual.coverage.preservedStaticCount -eq 214) 'Resolved DIA-03 signatures were not preserved.'
    Assert-ResolutionContract ($actual.coverage.unresolvedCount -eq 0) 'DIA-04 left unresolved instruction signatures.'
    Assert-ResolutionContract ($actual.coverage.catalogRuleCount -eq 19) 'Unexpected signature resolution rule count.'
    Assert-ResolutionContract ($actual.catalogHash -match '^[0-9a-f]{64}$' -and $actual.resolutionSetHash -match '^[0-9a-f]{64}$') 'DIA-04 canonical hashes are invalid.'
    Assert-ResolutionContract ((Test-Path -LiteralPath $outputPath -PathType Leaf)) 'DIA-04 report was not written.'
    Assert-ResolutionContract (@($actual.instructions | Where-Object behaviorFixtureStatus -ne 'Uncovered').Count -eq 0) 'Static rules incorrectly upgraded behavior fixture coverage.'

    $printV = @($actual.instructions | Where-Object publicKey -eq 'PRINTV')[0]
    $printForm = @($actual.instructions | Where-Object publicKey -eq 'PRINTFORM')[0]
    $call = @($actual.instructions | Where-Object publicKey -eq 'CALL')[0]
    $callForm = @($actual.instructions | Where-Object publicKey -eq 'CALLFORM')[0]
    $returnForm = @($actual.instructions | Where-Object publicKey -eq 'RETURNFORM')[0]
    Assert-ResolutionContract ($printV.resolvedArgumentSchema -eq 'FunctionArgType.SP_PRINTV') 'PRINTV was resolved to the wrong signature.'
    Assert-ResolutionContract ($printForm.resolvedArgumentSchema -eq 'FunctionArgType.FORM_STR_NULLABLE') 'PRINTFORM was resolved to the wrong signature.'
    Assert-ResolutionContract ($call.resolvedArgumentSchema -eq 'FunctionArgType.SP_CALL' -and $callForm.resolvedArgumentSchema -eq 'FunctionArgType.SP_CALLFORM') 'CALL constructor variants were flattened incorrectly.'
    Assert-ResolutionContract ($returnForm.resolvedArgumentSchema -eq 'FunctionArgType.FORM_STR') 'RETURNFORM active signature was not selected.'

    $repeat = New-DialectSignatureResolutionReport -SignatureInventory $signatures -Catalog $catalog
    Assert-ResolutionContract ($repeat.catalogHash -eq $actual.catalogHash -and $repeat.resolutionSetHash -eq $actual.resolutionSetHash) 'DIA-04 hashes changed between identical resolutions.'

    $syntheticSignatures = [pscustomobject]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-DIA-03'
        descriptorSetHash = ('d' * 64)
        instructions = @(
            [pscustomobject]@{ publicKey='A'; targetModule='test'; handlerType='SharedInstruction'; argumentSchemaStatus='Conditional'; argumentSchemaCandidates=@('Arg.ONE','Arg.TWO'); evidenceStatus='StaticCandidate'; behaviorFixtureStatus='Uncovered' },
            [pscustomobject]@{ publicKey='B'; targetModule='test'; handlerType='SharedInstruction'; argumentSchemaStatus='Conditional'; argumentSchemaCandidates=@('Arg.ONE','Arg.TWO'); evidenceStatus='StaticCandidate'; behaviorFixtureStatus='Uncovered' },
            [pscustomobject]@{ publicKey='C'; targetModule='test'; handlerType='DirectInstruction'; argumentSchemaStatus='Resolved'; argumentSchemaCandidates=@('Arg.FIXED'); evidenceStatus='StaticCandidate'; behaviorFixtureStatus='Uncovered' }
        )
    }
    $syntheticCatalog = [pscustomobject]@{
        schemaVersion = '1.0.0'
        catalogId = 'gemuera.test.instruction-signature-resolution'
        catalogVersion = '1.0.0'
        sourceWorkPackage = 'M0-DIA-03'
        sourceDescriptorSetHash = ('d' * 64)
        rules = @(
            [pscustomobject]@{ ruleId='test.a'; handlerType='SharedInstruction'; matchKind='Exact'; pattern='A'; selectedArgumentSchema='Arg.ONE'; expectedMatchCount=1; rationale='synthetic A' },
            [pscustomobject]@{ ruleId='test.b'; handlerType='SharedInstruction'; matchKind='Exact'; pattern='B'; selectedArgumentSchema='Arg.TWO'; expectedMatchCount=1; rationale='synthetic B' }
        )
    }
    $synthetic = New-DialectSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $syntheticCatalog
    Assert-ResolutionContract ($synthetic.coverage.resolvedStaticByRuleCount -eq 2 -and $synthetic.coverage.preservedStaticCount -eq 1) 'Synthetic resolution counts are incorrect.'

    $staleSource = Copy-ResolutionObject $syntheticCatalog
    $staleSource.sourceDescriptorSetHash = ('e' * 64)
    Assert-ResolutionThrows { New-DialectSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $staleSource } 'sourceDescriptorSetHash' 'A stale source hash was accepted.'

    $unmatched = Copy-ResolutionObject $syntheticCatalog
    $unmatched.rules = @($unmatched.rules | Where-Object ruleId -ne 'test.b')
    Assert-ResolutionThrows { New-DialectSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $unmatched } 'exactly one rule' 'An unmatched Conditional descriptor was accepted.'

    $ambiguous = Copy-ResolutionObject $syntheticCatalog
    $ambiguous.rules += [pscustomobject]@{ ruleId='test.a.regex'; handlerType='SharedInstruction'; matchKind='PublicKeyRegex'; pattern='^A$'; selectedArgumentSchema='Arg.ONE'; expectedMatchCount=1; rationale='ambiguous synthetic A' }
    Assert-ResolutionThrows { New-DialectSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $ambiguous } 'exactly one rule' 'Ambiguous rule matches were accepted.'

    $duplicateId = Copy-ResolutionObject $syntheticCatalog
    $duplicateId.rules[1].ruleId = 'test.a'
    Assert-ResolutionThrows { New-DialectSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $duplicateId } 'Duplicate ruleId' 'A duplicate rule ID was accepted.'

    $duplicateSelector = Copy-ResolutionObject $syntheticCatalog
    $duplicateSelector.rules[1].pattern = 'A'
    Assert-ResolutionThrows { New-DialectSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $duplicateSelector } 'Duplicate rule selector' 'A duplicate handler selector was accepted.'

    $staleCount = Copy-ResolutionObject $syntheticCatalog
    $staleCount.rules[0].expectedMatchCount = 2
    Assert-ResolutionThrows { New-DialectSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $staleCount } 'expectedMatchCount' 'A stale rule match count was accepted.'

    $outsideCandidate = Copy-ResolutionObject $syntheticCatalog
    $outsideCandidate.rules[0].selectedArgumentSchema = 'Arg.UNKNOWN'
    Assert-ResolutionThrows { New-DialectSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $outsideCandidate } 'not a DIA-03 candidate' 'A rule selected a value outside the DIA-03 candidates.'

    $resolvedMatch = Copy-ResolutionObject $syntheticCatalog
    $resolvedMatch.rules += [pscustomobject]@{ ruleId='test.resolved'; handlerType='DirectInstruction'; matchKind='Exact'; pattern='C'; selectedArgumentSchema='Arg.FIXED'; expectedMatchCount=1; rationale='must not target already resolved descriptors' }
    Assert-ResolutionThrows { New-DialectSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $resolvedMatch } 'already-Resolved' 'A rule targeting an already resolved descriptor was accepted.'

    Write-Output 'M0 dialect signature resolution contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-signature-resolution-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
