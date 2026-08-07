[CmdletBinding()]
param([string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Governance {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

$toolRoot = Join-Path $ProjectRoot 'tools\governance'
$schemaPath = Join-Path $toolRoot 'work-package.schema.json'
Assert-Governance (Test-Path -LiteralPath $schemaPath -PathType Leaf) 'M3-M7 work-package schema is missing.'
$schema = Get-Content -LiteralPath $schemaPath -Raw -Encoding utf8 | ConvertFrom-Json
Assert-Governance ($schema.properties.executionStatus.enum -contains 'Passed') 'Execution status contract lost Passed.'
Assert-Governance ($schema.properties.gateStatus.enum -contains 'ReadyForReview') 'Gate status contract lost ReadyForReview.'

$temp = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-governance-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $record = [ordered]@{
        schemaVersion = '1.0.0'; workPackageId = 'M3-CORE-01'; phase = 'M3'
        executionStatus = 'InProgress'; gateStatus = 'Blocked'; blockerCode = 'PreviousGate:M2'
        predecessors = @('M2 gate decision: missing'); identity = [ordered]@{source='tree';toolchain='dotnet';runtime='none';fixture='none';profile='v24pure';flags=@('core.extracted=false')}
        allowedPaths = @('src/Core'); forbiddenPaths = @('Scripts/Emuera'); owner = [ordered]@{core='core';bridge='bridge';app='app';tests='tests'}
        commands = @('dotnet build src/Core/GEmuera.Core.csproj -c Release --no-restore'); fixtures = @(); comparators = @(); reports = @('core-build.json'); uncovered = @('Godot and device evidence')
        rollback = [ordered]@{flag='core.extracted';artifact='legacy';restoreCommand='set core.extracted=false';verified=$false}
        decision = [ordered]@{preparedBy='local';reviewedBy='unassigned';approvedBy='unassigned';conclusion='NeedsEvidence';reportHashes=@()}
    }
    $recordPath = Join-Path $temp 'record.json'
    $record | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $recordPath -Encoding utf8
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $toolRoot 'Test-WorkPackageRecord.ps1') -RecordPath $recordPath 2>&1 | Out-Null
    Assert-Governance ($LASTEXITCODE -eq 0) 'Valid blocked work-package record was rejected.'

    $record.gateStatus = 'Passed'
    $record.executionStatus = 'Passed'
    $record | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $recordPath -Encoding utf8
    $previousErrorAction = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $toolRoot 'Test-WorkPackageRecord.ps1') -RecordPath $recordPath 2>$null | Out-Null
    $invalidExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorAction
    Assert-Governance ($invalidExitCode -ne 0) 'Incomplete Passed work-package record was accepted.'
} finally {
    Remove-Item -LiteralPath $temp -Recurse -Force
}

Write-Output 'M3-M7 governance contract passed.'
