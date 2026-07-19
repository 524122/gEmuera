[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [Parameter(Mandatory = $true)][string]$UpstreamProjectRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path $ProjectRoot 'tools\dialect-inventory'
$generatedRoot = Join-Path $ProjectRoot 'NewFrameworkDesign\generated'
$modulePath = Join-Path $toolRoot 'DialectOwnershipEvidence.psm1'
$catalogPath = Join-Path $toolRoot 'dialect-ownership-evidence.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-ownership-test-' + [Guid]::NewGuid().ToString('N'))
$utf8NoBom = New-Object Text.UTF8Encoding($false)

function Assert-OwnershipContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Copy-OwnershipObject {
    param([object]$Value)
    return ($Value | ConvertTo-Json -Depth 60 | ConvertFrom-Json)
}

function Assert-OwnershipThrows {
    param([scriptblock]$Action, [string]$Pattern, [string]$Failure)
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) {
        throw "$Failure Actual: $($caught.Exception.Message)"
    }
}

function Read-OwnershipJson {
    param([string]$Path)
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing DIA-07 module: $modulePath"
    }
    Import-Module $modulePath -Force
    $catalog = Read-OwnershipJson $catalogPath
    $inventory = Read-OwnershipJson (Join-Path $generatedRoot 'dialect-inventory.json')
    $snapshots = Read-OwnershipJson (Join-Path $generatedRoot 'dialect-registry-snapshots.json')
    $signatures = Read-OwnershipJson (Join-Path $generatedRoot 'dialect-signature-inventory.json')
    $arguments = Read-OwnershipJson (Join-Path $generatedRoot 'dialect-signature-resolution.json')
    $functions = Read-OwnershipJson (Join-Path $generatedRoot 'dialect-function-signature-resolution.json')
    $flags = Read-OwnershipJson (Join-Path $generatedRoot 'dialect-instruction-flag-resolution.json')

    $actual = New-DialectOwnershipEvidenceReport -Inventory $inventory -RegistrySnapshot $snapshots -SignatureInventory $signatures -InstructionSignatureResolution $arguments -FunctionSignatureResolution $functions -InstructionFlagResolution $flags -Catalog $catalog -UpstreamProjectRoot $UpstreamProjectRoot
    Assert-OwnershipContract ($actual.workPackage -eq 'M0-DIA-07') 'Unexpected ownership work package.'
    Assert-OwnershipContract ($actual.instructionCount -eq 326) 'Real instruction ownership count drifted.'
    Assert-OwnershipContract ($actual.expressionFunctionCount -eq 360) 'Real expression ownership count drifted.'
    Assert-OwnershipContract ($actual.currentRuntimeIsolation.status -eq 'Failed') 'Legacy runtime isolation was not kept Failed.'
    Assert-OwnershipContract (@($actual.instructions | Where-Object behaviorCompatibilityStatus -ne 'Uncovered').Count -eq 0) 'Instruction behavior was overstated.'
    Assert-OwnershipContract (@($actual.expressionFunctions | Where-Object behaviorCompatibilityStatus -ne 'Uncovered').Count -eq 0) 'Function behavior was overstated.'
    Assert-OwnershipContract (@($actual.instructions | Where-Object completionEffectStatus -ne 'Uncovered').Count -eq 0) 'Instruction completion/effect was overstated.'
    Assert-OwnershipContract (@($actual.expressionFunctions | Where-Object completionEffectStatus -ne 'Uncovered').Count -eq 0) 'Function completion/effect was overstated.'
    Assert-OwnershipContract ($actual.coverage.upstreamInstructionNameMatchCount -eq 284) 'Upstream instruction provenance count drifted.'
    Assert-OwnershipContract ($actual.coverage.upstreamExpressionNameMatchCount -eq 243) 'Upstream expression provenance count drifted.'
    Assert-OwnershipContract (@($actual.instructions | Where-Object { $_.upstreamPresence -eq 'Present' -and $_.stableModuleCandidate -ne 'emuera.upstream' }).Count -eq 0) 'Upstream instruction name match did not select the stable upstream candidate.'

    $syntheticUpstream = Join-Path $testRoot 'upstream'
    $instructionPath = Join-Path $syntheticUpstream 'nested\Emuera\GameProc\Function\FunctionIdentifier.cs'
    $functionPath = Join-Path $syntheticUpstream 'nested\Emuera\GameData\Function\Creator.cs'
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($instructionPath)) | Out-Null
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($functionPath)) | Out-Null
    [IO.File]::WriteAllText($instructionPath, "addFunction(FunctionCode.COMMON, argb[x]);`naddFunction(FunctionCode.V24_NAME_WITHOUT_V24_HANDLER, argb[x]);`n// addFunction(FunctionCode.COMMENTED, argb[x]);`n", $utf8NoBom)
    [IO.File]::WriteAllText($functionPath, "[`"COMMON_FN`"] = new CommonMethod(),`n[`"BASE_ONLY`"] = new BaseMethod(),`n// [`"COMMENTED_FN`"] = new NopeMethod(),`n", $utf8NoBom)

    $syntheticCatalog = Copy-OwnershipObject $catalog
    $syntheticCatalog.upstream.expectedInstructionCount = 2
    $syntheticCatalog.upstream.expectedExpressionFunctionCount = 2
    $syntheticCatalog.upstream.instructionSourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $instructionPath).Hash.ToLowerInvariant()
    $syntheticCatalog.upstream.expressionSourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $functionPath).Hash.ToLowerInvariant()

    $syntheticInventory = [pscustomobject]@{
        workPackage = 'M0-DIA-01'; canonicalHash = ('1' * 64)
        instructionRegistrations = @(
            [pscustomobject]@{ publicKey='COMMON'; targetModule='legacy.common.unresolved'; handler='CommonHandler'; sourceFile='current/FunctionIdentifier.cs'; sourceLine=1 },
            [pscustomobject]@{ publicKey='V24_NAME_WITHOUT_V24_HANDLER'; targetModule='gemuera.v24'; handler='PlainHandler'; sourceFile='current/FunctionIdentifier.cs'; sourceLine=2 },
            [pscustomobject]@{ publicKey='SNAKE_CLASS_IS_NOT_PROOF'; targetModule='legacy.common.unresolved'; handler='SNAKE_MisleadingHandler'; sourceFile='current/FunctionIdentifier.cs'; sourceLine=3 }
        )
        expressionRegistrations = @(
            [pscustomobject]@{ publicKey='COMMON_FN'; targetModule='legacy.expression.unresolved'; handler='CommonMethod'; sourceFile='current/Creator.cs'; sourceLine=1 },
            [pscustomobject]@{ publicKey='BASE_ONLY'; targetModule='game.snake'; handler='PlainMethod'; sourceFile='current/Creator.cs'; sourceLine=2 },
            [pscustomobject]@{ publicKey='SNAKE_FN_NAME_IS_NOT_PROOF'; targetModule='game.snake.candidate'; handler='SnakeMethod'; sourceFile='current/Creator.cs'; sourceLine=3 }
        )
    }
    $syntheticSnapshots = [pscustomobject]@{ workPackage='M0-DIA-02'; sourceInventoryHash=('1' * 64); snapshotSetHash=('2' * 64); currentRuntimeIsolation=[pscustomobject]@{status='Failed'} }
    $syntheticSignatures = [pscustomobject]@{ workPackage='M0-DIA-03'; sourceInventoryHash=('1' * 64); descriptorSetHash=('3' * 64); instructions=@($syntheticInventory.instructionRegistrations); expressionFunctions=@($syntheticInventory.expressionRegistrations) }
    $syntheticArguments = [pscustomobject]@{ workPackage='M0-DIA-04'; sourceDescriptorSetHash=('3' * 64); resolutionSetHash=('4' * 64); instructions=@($syntheticInventory.instructionRegistrations) }
    $syntheticFunctions = [pscustomobject]@{ workPackage='M0-DIA-05'; sourceDescriptorSetHash=('3' * 64); resolutionSetHash=('5' * 64); expressionFunctions=@($syntheticInventory.expressionRegistrations) }
    $syntheticFlags = [pscustomobject]@{ workPackage='M0-DIA-06'; sourceDescriptorSetHash=('3' * 64); sourceInstructionSignatureResolutionHash=('4' * 64); resolutionSetHash=('6' * 64); instructions=@($syntheticInventory.instructionRegistrations) }

    $synthetic = New-DialectOwnershipEvidenceReport -Inventory $syntheticInventory -RegistrySnapshot $syntheticSnapshots -SignatureInventory $syntheticSignatures -InstructionSignatureResolution $syntheticArguments -FunctionSignatureResolution $syntheticFunctions -InstructionFlagResolution $syntheticFlags -Catalog $syntheticCatalog -UpstreamProjectRoot $syntheticUpstream
    Assert-OwnershipContract ($synthetic.coverage.upstreamInstructionNameMatchCount -eq 2) 'Synthetic upstream instruction matches drifted.'
    Assert-OwnershipContract ($synthetic.coverage.upstreamExpressionNameMatchCount -eq 2) 'Synthetic upstream function matches drifted.'
    Assert-OwnershipContract (($synthetic.instructions | Where-Object publicKey -eq 'V24_NAME_WITHOUT_V24_HANDLER').stableModuleCandidate -eq 'emuera.upstream') 'Upstream provenance did not take precedence over current naming.'
    Assert-OwnershipContract (($synthetic.instructions | Where-Object publicKey -eq 'SNAKE_CLASS_IS_NOT_PROOF').stableModuleCandidate -eq 'Unresolved') 'Snake handler name was incorrectly treated as ownership proof.'
    Assert-OwnershipContract (($synthetic.expressionFunctions | Where-Object publicKey -eq 'SNAKE_FN_NAME_IS_NOT_PROOF').stableModuleCandidate -eq 'Unresolved') 'Snake candidate bucket was incorrectly treated as stable ownership.'
    Assert-OwnershipContract (($synthetic.expressionFunctions | Where-Object publicKey -eq 'BASE_ONLY').stableModuleCandidate -eq 'emuera.upstream') 'Upstream provenance did not override a conflicting current target candidate.'
    Assert-OwnershipContract (@($synthetic.instructions | Where-Object nameComparerStatus -ne 'Unresolved').Count -eq 0) 'Comparer status was overstated.'
    Assert-OwnershipContract (@($synthetic.instructions | Where-Object aliasStatus -ne 'Unresolved').Count -eq 0) 'Alias status was overstated.'
    Assert-OwnershipContract (@($synthetic.instructions | Where-Object replacementStatus -ne 'Unresolved').Count -eq 0) 'Replacement status was overstated.'

    $stale = Copy-OwnershipObject $syntheticArguments
    $stale.sourceDescriptorSetHash = ('9' * 64)
    Assert-OwnershipThrows { New-DialectOwnershipEvidenceReport $syntheticInventory $syntheticSnapshots $syntheticSignatures $stale $syntheticFunctions $syntheticFlags $syntheticCatalog $syntheticUpstream } 'DIA-04.*DIA-03' 'Stale DIA chain was accepted.'
    Assert-OwnershipThrows { New-DialectOwnershipEvidenceReport $syntheticInventory $syntheticSnapshots $syntheticSignatures $syntheticArguments $syntheticFunctions $syntheticFlags $syntheticCatalog (Join-Path $testRoot 'missing') } 'UpstreamProjectRoot' 'Missing upstream root was accepted.'

    [IO.File]::AppendAllText($instructionPath, "addFunction(FunctionCode.COMMON, argb[x]);`n", $utf8NoBom)
    $duplicateCatalog = Copy-OwnershipObject $syntheticCatalog
    $duplicateCatalog.upstream.instructionSourceSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $instructionPath).Hash.ToLowerInvariant()
    Assert-OwnershipThrows { New-DialectOwnershipEvidenceReport $syntheticInventory $syntheticSnapshots $syntheticSignatures $syntheticArguments $syntheticFunctions $syntheticFlags $duplicateCatalog $syntheticUpstream } 'Duplicate upstream instruction key' 'Duplicate upstream key was accepted.'

    Write-Output 'M0 dialect ownership evidence contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-dia-ownership-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
