[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Join-Path $ProjectRoot 'tools\dialect-inventory'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-dia-flag-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-FlagContract { param([bool]$Condition,[string]$Message) if(-not $Condition){throw $Message} }
function Copy-FlagObject { param([object]$Value) return ($Value|ConvertTo-Json -Depth 40|ConvertFrom-Json) }
function Assert-FlagThrows {
    param([scriptblock]$Action,[string]$Pattern,[string]$Failure)
    $caught=$null; try{& $Action}catch{$caught=$_}
    if($null -eq $caught){throw $Failure}
    if([string]$caught.Exception.Message -notmatch $Pattern){throw "$Failure Unexpected error: $($caught.Exception.Message)"}
}
function Assert-FlagSet {
    param([object]$Descriptor,[string[]]$Expected,[string]$Message)
    $actual=@($Descriptor.effectiveFlags);$expectedSorted=@($Expected)
    [Array]::Sort($expectedSorted,[StringComparer]::Ordinal)
    if(($actual -join '|') -cne ($expectedSorted -join '|')){throw "$Message actual=$($actual -join ',')"}
}

try {
    $modulePath=Join-Path $root 'DialectInstructionFlagResolution.psm1'
    $catalogPath=Join-Path $root 'instruction-flag-resolution.json'
    if(-not(Test-Path $modulePath -PathType Leaf)){throw "Dialect instruction flag resolution module is missing: $modulePath"}
    if(-not(Test-Path $catalogPath -PathType Leaf)){throw "Dialect instruction flag resolution catalog is missing: $catalogPath"}
    Import-Module (Join-Path $root 'DialectInventory.psm1') -Force
    Import-Module (Join-Path $root 'DialectSignatureInventory.psm1') -Force
    Import-Module (Join-Path $root 'DialectSignatureResolution.psm1') -Force
    Import-Module $modulePath -Force

    $inventory=New-DialectInventory -ProjectRoot $ProjectRoot -ClassificationPath (Join-Path $root 'dialect-classification.json')
    $signatures=New-DialectSignatureInventory -ProjectRoot $ProjectRoot -Inventory $inventory
    $argCatalog=Get-Content (Join-Path $root 'instruction-signature-resolution.json') -Raw -Encoding UTF8|ConvertFrom-Json
    $arguments=New-DialectSignatureResolutionReport -SignatureInventory $signatures -Catalog $argCatalog
    $catalog=Get-Content $catalogPath -Raw -Encoding UTF8|ConvertFrom-Json
    $output=Join-Path $testRoot 'flags.json'
    $actual=New-DialectInstructionFlagResolutionReport -SignatureInventory $signatures -InstructionSignatureResolution $arguments -Catalog $catalog -OutputPath $output

    Assert-FlagContract ($actual.workPackage -eq 'M0-DIA-06') 'Unexpected flag work package.'
    Assert-FlagContract ($actual.executionStatus -eq 'InProgress' -and $actual.gateStatus -eq 'Blocked' -and $actual.blockerCode -eq 'EvidenceMissing' -and $actual.result -eq 'Partial') 'M0 gate was incorrectly advanced.'
    Assert-FlagContract ($actual.instructionCount -eq 326) 'Unexpected descriptor count.'
    Assert-FlagContract ($actual.coverage.directRegistrationStaticCount -eq 67 -and $actual.coverage.handlerSingleStaticCount -eq 136 -and $actual.coverage.handlerRulesStaticCount -eq 123) 'Flag resolution partition is incorrect.'
    Assert-FlagContract ($actual.coverage.unresolvedCount -eq 0 -and $actual.coverage.catalogRuleCount -eq 35 -and $actual.coverage.knownFlagCount -eq 17) 'Flag resolution coverage is incomplete.'
    Assert-FlagContract ($actual.sourceDescriptorSetHash -eq $signatures.descriptorSetHash -and $actual.sourceInstructionSignatureResolutionHash -eq $arguments.resolutionSetHash) 'Flag report source identity mismatch.'
    Assert-FlagContract ($actual.catalogHash -match '^[0-9a-f]{64}$' -and $actual.resolutionSetHash -match '^[0-9a-f]{64}$' -and (Test-Path $output -PathType Leaf)) 'Flag report/hash output is invalid.'
    Assert-FlagContract (@($actual.instructions|Where-Object behaviorFixtureStatus -ne 'Uncovered').Count -eq 0) 'Flags incorrectly upgraded behavior coverage.'

    $byKey=@{}; foreach($d in $actual.instructions){$byKey[$d.publicKey]=$d}
    Assert-FlagSet $byKey.PRINT @('IS_PRINT','METHOD_SAFE') 'PRINT flags are incorrect.'
    Assert-FlagSet $byKey.PRINTW @('IS_PRINT','PRINT_NEWLINE','PRINT_WAITINPUT') 'PRINTW flags are incorrect.'
    Assert-FlagSet $byKey.PRINTSINGLEVK @('EXTENDED','ISPRINTKFUNC','IS_PRINT','METHOD_SAFE','PRINT_SINGLE') 'PRINTSINGLEVK flags are incorrect.'
    Assert-FlagSet $byKey.TRYCCALLFORM @('EXTENDED','FLOW_CONTROL','FORCE_SETARG','IS_TRY','IS_TRYC','PARTIAL') 'TRYCCALLFORM flags are incorrect.'
    Assert-FlagSet $byKey.TRYCJUMPFORM @('EXTENDED','FLOW_CONTROL','FORCE_SETARG','IS_JUMP','IS_TRY','IS_TRYC','PARTIAL') 'TRYCJUMPFORM flags are incorrect.'
    Assert-FlagSet $byKey.AWAIT @('EXTENDED') 'AWAIT comment noise leaked into flags.'
    Assert-FlagSet $byKey.INPUTMOUSEKEY @('EXTENDED') 'INPUTMOUSEKEY comment noise leaked into flags.'
    Assert-FlagSet $byKey.TWAIT @('EXTENDED','IS_PRINT') 'TWAIT local variable leaked into flags.'
    Assert-FlagSet $byKey.PRINTBUTTON @('EXTENDED','METHOD_SAFE') 'Direct registration flags are incorrect.'
    Assert-FlagContract ($byKey.CALL.resolvedArgumentSchema -eq 'FunctionArgType.SP_CALL') 'DIA-04 argument projection was not preserved.'

    $repeat=New-DialectInstructionFlagResolutionReport -SignatureInventory $signatures -InstructionSignatureResolution $arguments -Catalog $catalog
    Assert-FlagContract ($repeat.catalogHash -eq $actual.catalogHash -and $repeat.resolutionSetHash -eq $actual.resolutionSetHash) 'Flag hashes are not deterministic.'

    $sigs=[pscustomobject]@{workPackage='M0-DIA-03';descriptorSetHash=('a'*64);instructions=@(
        [pscustomobject]@{publicKey='DIRECT';targetModule='test';handlerType='';argumentSchemaStatus='Resolved';argumentSchemaCandidates=@('Arg.Direct');registrationFlagExpression='METHOD_SAFE | EXTENDED';handlerFlagCandidates=@();completionModeCandidate='SynchronousCandidate';effectCandidates=@('ValueOrUnclassifiedCandidate')},
        [pscustomobject]@{publicKey='SINGLE';targetModule='test';handlerType='SingleHandler';argumentSchemaStatus='Resolved';argumentSchemaCandidates=@('Arg.Single');registrationFlagExpression='EXTENDED';handlerFlagCandidates=@('FLOW_CONTROL | FORCE_SETARG');completionModeCandidate='ControlFlowCandidate';effectCandidates=@('SessionStateCandidate')},
        [pscustomobject]@{publicKey='A';targetModule='test';handlerType='SharedHandler';argumentSchemaStatus='Resolved';argumentSchemaCandidates=@('Arg.Shared');registrationFlagExpression='0';handlerFlagCandidates=@('FLOW_CONTROL','IS_TRY');completionModeCandidate='ControlFlowCandidate';effectCandidates=@('SessionStateCandidate')},
        [pscustomobject]@{publicKey='B';targetModule='test';handlerType='SharedHandler';argumentSchemaStatus='Resolved';argumentSchemaCandidates=@('Arg.Shared');registrationFlagExpression='0';handlerFlagCandidates=@('FLOW_CONTROL','IS_TRY');completionModeCandidate='ControlFlowCandidate';effectCandidates=@('SessionStateCandidate')}
    )}
    $argResolution=[pscustomobject]@{workPackage='M0-DIA-04';sourceDescriptorSetHash=('a'*64);resolutionSetHash=('b'*64);instructions=@(
        [pscustomobject]@{publicKey='DIRECT';targetModule='test';handlerType='';resolvedArgumentSchema='Arg.Direct';resolutionStatus='PreservedStatic'},
        [pscustomobject]@{publicKey='SINGLE';targetModule='test';handlerType='SingleHandler';resolvedArgumentSchema='Arg.Single';resolutionStatus='PreservedStatic'},
        [pscustomobject]@{publicKey='A';targetModule='test';handlerType='SharedHandler';resolvedArgumentSchema='Arg.Shared';resolutionStatus='PreservedStatic'},
        [pscustomobject]@{publicKey='B';targetModule='test';handlerType='SharedHandler';resolvedArgumentSchema='Arg.Shared';resolutionStatus='PreservedStatic'}
    )}
    $cat=[pscustomobject]@{schemaVersion='1.0.0';catalogId='test.flags';catalogVersion='1.0.0';sourceWorkPackage='M0-DIA-03';sourceDescriptorSetHash=('a'*64);sourceInstructionSignatureResolutionHash=('b'*64);knownFlags=@('EXTENDED','FLOW_CONTROL','FORCE_SETARG','IS_TRY','METHOD_SAFE');rules=@(
        [pscustomobject]@{ruleId='shared.base';handlerType='SharedHandler';matchKind='PublicKeyRegex';pattern='^[AB]$';selectedHandlerFlags=@('FLOW_CONTROL');expectedMatchCount=2;rationale='base'},
        [pscustomobject]@{ruleId='shared.a';handlerType='SharedHandler';matchKind='Exact';pattern='A';selectedHandlerFlags=@('IS_TRY');expectedMatchCount=1;rationale='A try'}
    )}
    $syn=New-DialectInstructionFlagResolutionReport -SignatureInventory $sigs -InstructionSignatureResolution $argResolution -Catalog $cat
    Assert-FlagContract ($syn.coverage.directRegistrationStaticCount -eq 1 -and $syn.coverage.handlerSingleStaticCount -eq 1 -and $syn.coverage.handlerRulesStaticCount -eq 2) 'Synthetic flag partition failed.'

    $x=Copy-FlagObject $cat; $x.sourceDescriptorSetHash=('c'*64)
    Assert-FlagThrows {New-DialectInstructionFlagResolutionReport $sigs $argResolution $x} 'sourceDescriptorSetHash' 'Stale DIA-03 hash was accepted.'
    $x=Copy-FlagObject $cat; $x.knownFlags += 'METHOD_SAFE'
    Assert-FlagThrows {New-DialectInstructionFlagResolutionReport $sigs $argResolution $x} 'Duplicate known flag' 'Duplicate known flag was accepted.'
    $x=Copy-FlagObject $cat; $x.rules[0].expectedMatchCount=1
    Assert-FlagThrows {New-DialectInstructionFlagResolutionReport $sigs $argResolution $x} 'expectedMatchCount' 'Stale rule count was accepted.'
    $x=Copy-FlagObject $cat; $x.rules=@($x.rules|Where-Object ruleId -ne 'shared.base')
    Assert-FlagThrows {New-DialectInstructionFlagResolutionReport $sigs $argResolution $x} 'at least one rule' 'Uncovered multi-candidate descriptor was accepted.'
    $x=Copy-FlagObject $cat; $x.rules[0].selectedHandlerFlags=@('METHOD_SAFE')
    Assert-FlagThrows {New-DialectInstructionFlagResolutionReport $sigs $argResolution $x} 'not present in DIA-03 handler candidates' 'Candidate-external flag contribution was accepted.'
    $x=Copy-FlagObject $cat; $x.rules += [pscustomobject]@{ruleId='single.illegal';handlerType='SingleHandler';matchKind='Exact';pattern='SINGLE';selectedHandlerFlags=@('FLOW_CONTROL');expectedMatchCount=1;rationale='illegal'}
    Assert-FlagThrows {New-DialectInstructionFlagResolutionReport $sigs $argResolution $x} 'non-multi-candidate' 'Rule targeting single candidate was accepted.'
    $x=Copy-FlagObject $cat; $x.rules[1].ruleId='shared.base'
    Assert-FlagThrows {New-DialectInstructionFlagResolutionReport $sigs $argResolution $x} 'Duplicate ruleId' 'Duplicate flag rule ID was accepted.'
    $x=Copy-FlagObject $cat; $x.rules[1].matchKind='PublicKeyRegex';$x.rules[1].pattern='A'
    Assert-FlagThrows {New-DialectInstructionFlagResolutionReport $sigs $argResolution $x} 'anchored' 'Unanchored flag regex was accepted.'
    $x=Copy-FlagObject $argResolution; $x.instructions[3].publicKey='MISSING'
    Assert-FlagThrows {New-DialectInstructionFlagResolutionReport $sigs $x $cat} 'key set' 'DIA-03/DIA-04 key drift was accepted.'

    Write-Output 'M0 dialect instruction flag resolution contract tests passed.'
    exit 0
}
catch{[Console]::Error.WriteLine($_.Exception.Message);[Console]::Error.WriteLine($_.ScriptStackTrace);exit 1}
finally{
    $tmp=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')+'\';$resolved=[IO.Path]::GetFullPath($testRoot).TrimEnd('\')+'\'
    if($resolved.StartsWith($tmp,[StringComparison]::OrdinalIgnoreCase)-and $resolved.Contains('gemuera-m0-dia-flag-test-')-and(Test-Path $testRoot)){Remove-Item $testRoot -Recurse -Force}
}
