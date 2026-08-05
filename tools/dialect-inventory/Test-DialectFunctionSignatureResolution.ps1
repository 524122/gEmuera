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
$catalogPath = Join-Path $ProjectRoot 'tools\dialect-inventory\function-return-resolution.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-function-resolution-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-FunctionResolutionContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Copy-FunctionResolutionObject {
    param([Parameter(Mandatory = $true)][object]$Value)
    return ($Value | ConvertTo-Json -Depth 30 | ConvertFrom-Json)
}

function Assert-FunctionResolutionThrows {
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
    if (-not (Test-Path -LiteralPath $resolutionModulePath -PathType Leaf)) { throw "Dialect signature resolution module is missing: $resolutionModulePath" }
    if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) { throw "Dialect function return resolution catalog is missing: $catalogPath" }

    Import-Module $inventoryModulePath -Force
    Import-Module $signatureModulePath -Force
    Import-Module $resolutionModulePath -Force

    $inventory = New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath $classificationPath
    $signatures = New-DialectSignatureInventory -ProjectRoot $ProjectRoot -Inventory $inventory
    $catalog = Get-Content -LiteralPath $catalogPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $outputPath = Join-Path $testRoot 'actual-function-resolution.json'
    $actual = New-DialectFunctionSignatureResolutionReport -SignatureInventory $signatures -Catalog $catalog -OutputPath $outputPath

    Assert-FunctionResolutionContract ($actual.workPackage -eq 'M0-DIA-05') 'Unexpected function signature resolution work package.'
    Assert-FunctionResolutionContract ($actual.executionStatus -eq 'InProgress' -and $actual.gateStatus -eq 'Blocked' -and $actual.blockerCode -eq 'EvidenceMissing') 'M0 gate status was incorrectly advanced.'
    Assert-FunctionResolutionContract ($actual.result -eq 'Partial') 'Static function signature resolution must not claim behavior completion.'
    Assert-FunctionResolutionContract ($actual.sourceDescriptorSetHash -eq $signatures.descriptorSetHash) 'DIA-05 source descriptor hash mismatch.'
    Assert-FunctionResolutionContract ($actual.expressionFunctionCount -eq 362) "Unexpected function count: $($actual.expressionFunctionCount)."
    Assert-FunctionResolutionContract ($actual.coverage.sourceReturnResolvedCount -eq 360 -and $actual.coverage.sourceReturnConditionalCount -eq 2 -and $actual.coverage.sourceReturnUnresolvedCount -eq 0) 'Unexpected DIA-03 return coverage.'
    Assert-FunctionResolutionContract ($actual.coverage.preservedStaticCount -eq 360 -and $actual.coverage.resolvedStaticByRuleCount -eq 2 -and $actual.coverage.unresolvedCount -eq 0) 'DIA-05 return resolution coverage is incorrect.'
    Assert-FunctionResolutionContract ($actual.coverage.argumentSchemaResolvedCount -eq 362 -and $actual.coverage.completeStaticSignatureCount -eq 362) 'DIA-05 did not produce 362 complete static signatures.'
    Assert-FunctionResolutionContract ($actual.coverage.catalogRuleCount -eq 2) 'Unexpected function return rule count.'
    Assert-FunctionResolutionContract ($actual.catalogHash -match '^[0-9a-f]{64}$' -and $actual.resolutionSetHash -match '^[0-9a-f]{64}$') 'DIA-05 canonical hashes are invalid.'
    Assert-FunctionResolutionContract ((Test-Path -LiteralPath $outputPath -PathType Leaf)) 'DIA-05 report was not written.'
    Assert-FunctionResolutionContract (@($actual.expressionFunctions | Where-Object behaviorFixtureStatus -ne 'Uncovered').Count -eq 0) 'Static rules incorrectly upgraded function behavior coverage.'

    $getConfig = @($actual.expressionFunctions | Where-Object publicKey -eq 'GETCONFIG')[0]
    $getConfigs = @($actual.expressionFunctions | Where-Object publicKey -eq 'GETCONFIGS')[0]
    Assert-FunctionResolutionContract ($getConfig.resolvedReturnType -eq 'EraType.Integer' -and $getConfig.returnResolutionStatus -eq 'ResolvedStaticByRule') 'GETCONFIG return type is incorrect.'
    Assert-FunctionResolutionContract ($getConfigs.resolvedReturnType -eq 'EraType.String' -and $getConfigs.returnResolutionStatus -eq 'ResolvedStaticByRule') 'GETCONFIGS return type is incorrect.'
    Assert-FunctionResolutionContract ($getConfig.resolvedArgumentSchema -eq 'new EraType[] { EraType.String }' -and $getConfigs.staticSignatureStatus -eq 'CompleteStatic') 'GETCONFIG argument/static signature projection is incomplete.'

    $repeat = New-DialectFunctionSignatureResolutionReport -SignatureInventory $signatures -Catalog $catalog
    Assert-FunctionResolutionContract ($repeat.catalogHash -eq $actual.catalogHash -and $repeat.resolutionSetHash -eq $actual.resolutionSetHash) 'DIA-05 hashes changed between identical resolutions.'

    $syntheticSignatures = [pscustomobject]@{
        schemaVersion='1.0.0'; workPackage='M0-DIA-03'; descriptorSetHash=('f' * 64)
        expressionFunctions=@(
            [pscustomobject]@{ publicKey='A'; targetModule='test'; handlerType='SharedMethod'; returnTypeStatus='Conditional'; returnTypeCandidates=@('EraType.Integer','EraType.String'); argumentSchemaStatus='Resolved'; argumentSchemaCandidates=@('new EraType[] { }'); overridesArgumentCheck=$false; restructureCandidates=@('false'); completionModeCandidate='SynchronousBodyCandidate'; effectCandidates=@('ValueOrUnclassifiedCandidate'); evidenceStatus='StaticCandidate'; behaviorFixtureStatus='Uncovered' },
            [pscustomobject]@{ publicKey='B'; targetModule='test'; handlerType='SharedMethod'; returnTypeStatus='Conditional'; returnTypeCandidates=@('EraType.Integer','EraType.String'); argumentSchemaStatus='Resolved'; argumentSchemaCandidates=@('new EraType[] { }'); overridesArgumentCheck=$false; restructureCandidates=@('false'); completionModeCandidate='SynchronousBodyCandidate'; effectCandidates=@('ValueOrUnclassifiedCandidate'); evidenceStatus='StaticCandidate'; behaviorFixtureStatus='Uncovered' },
            [pscustomobject]@{ publicKey='C'; targetModule='test'; handlerType='DirectMethod'; returnTypeStatus='Resolved'; returnTypeCandidates=@('EraType.Float'); argumentSchemaStatus='Resolved'; argumentSchemaCandidates=@('null'); overridesArgumentCheck=$true; restructureCandidates=@('true'); completionModeCandidate='SynchronousBodyCandidate'; effectCandidates=@('ValueOrUnclassifiedCandidate'); evidenceStatus='StaticCandidate'; behaviorFixtureStatus='Uncovered' }
        )
    }
    $syntheticCatalog = [pscustomobject]@{
        schemaVersion='1.0.0'; catalogId='gemuera.test.function-return-resolution'; catalogVersion='1.0.0'; sourceWorkPackage='M0-DIA-03'; sourceDescriptorSetHash=('f' * 64)
        rules=@(
            [pscustomobject]@{ ruleId='test.a'; handlerType='SharedMethod'; matchKind='Exact'; pattern='A'; selectedReturnType='EraType.Integer'; expectedMatchCount=1; rationale='synthetic A' },
            [pscustomobject]@{ ruleId='test.b'; handlerType='SharedMethod'; matchKind='Exact'; pattern='B'; selectedReturnType='EraType.String'; expectedMatchCount=1; rationale='synthetic B' }
        )
    }
    $synthetic = New-DialectFunctionSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $syntheticCatalog
    Assert-FunctionResolutionContract ($synthetic.coverage.completeStaticSignatureCount -eq 3 -and $synthetic.coverage.resolvedStaticByRuleCount -eq 2) 'Synthetic function signatures were not resolved.'

    $staleSource = Copy-FunctionResolutionObject $syntheticCatalog
    $staleSource.sourceDescriptorSetHash = ('e' * 64)
    Assert-FunctionResolutionThrows { New-DialectFunctionSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $staleSource } 'sourceDescriptorSetHash' 'A stale source hash was accepted.'

    $unmatched = Copy-FunctionResolutionObject $syntheticCatalog
    $unmatched.rules = @($unmatched.rules | Where-Object ruleId -ne 'test.b')
    Assert-FunctionResolutionThrows { New-DialectFunctionSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $unmatched } 'exactly one rule' 'An unmatched Conditional return was accepted.'

    $ambiguous = Copy-FunctionResolutionObject $syntheticCatalog
    $ambiguous.rules += [pscustomobject]@{ ruleId='test.a.regex'; handlerType='SharedMethod'; matchKind='PublicKeyRegex'; pattern='^A$'; selectedReturnType='EraType.Integer'; expectedMatchCount=1; rationale='ambiguous A' }
    Assert-FunctionResolutionThrows { New-DialectFunctionSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $ambiguous } 'exactly one rule' 'Ambiguous function rules were accepted.'

    $duplicateId = Copy-FunctionResolutionObject $syntheticCatalog
    $duplicateId.rules[1].ruleId = 'test.a'
    Assert-FunctionResolutionThrows { New-DialectFunctionSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $duplicateId } 'Duplicate ruleId' 'A duplicate function rule ID was accepted.'

    $duplicateSelector = Copy-FunctionResolutionObject $syntheticCatalog
    $duplicateSelector.rules[1].pattern = 'A'
    Assert-FunctionResolutionThrows { New-DialectFunctionSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $duplicateSelector } 'Duplicate rule selector' 'A duplicate function rule selector was accepted.'

    $staleCount = Copy-FunctionResolutionObject $syntheticCatalog
    $staleCount.rules[0].expectedMatchCount = 2
    Assert-FunctionResolutionThrows { New-DialectFunctionSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $staleCount } 'expectedMatchCount' 'A stale function rule count was accepted.'

    $outsideCandidate = Copy-FunctionResolutionObject $syntheticCatalog
    $outsideCandidate.rules[0].selectedReturnType = 'EraType.Unknown'
    Assert-FunctionResolutionThrows { New-DialectFunctionSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $outsideCandidate } 'not a DIA-03 candidate' 'A function rule selected a return type outside DIA-03 candidates.'

    $resolvedMatch = Copy-FunctionResolutionObject $syntheticCatalog
    $resolvedMatch.rules += [pscustomobject]@{ ruleId='test.resolved'; handlerType='DirectMethod'; matchKind='Exact'; pattern='C'; selectedReturnType='EraType.Float'; expectedMatchCount=1; rationale='must not target resolved function' }
    Assert-FunctionResolutionThrows { New-DialectFunctionSignatureResolutionReport -SignatureInventory $syntheticSignatures -Catalog $resolvedMatch } 'already-Resolved' 'A function rule targeted an already resolved descriptor.'

    Write-Output 'M0 dialect function signature resolution contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-function-resolution-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
