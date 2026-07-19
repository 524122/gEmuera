[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RecordPath,
    [string]$SchemaPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-WorkPackage {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

if ([string]::IsNullOrWhiteSpace($SchemaPath)) {
    $SchemaPath = Join-Path (Split-Path -Parent $PSCommandPath) 'work-package.schema.json'
}

Assert-WorkPackage (Test-Path -LiteralPath $RecordPath -PathType Leaf) "Record not found: $RecordPath"
Assert-WorkPackage (Test-Path -LiteralPath $SchemaPath -PathType Leaf) "Schema not found: $SchemaPath"
$record = Get-Content -LiteralPath $RecordPath -Raw -Encoding utf8 | ConvertFrom-Json
$schema = Get-Content -LiteralPath $SchemaPath -Raw -Encoding utf8 | ConvertFrom-Json
Assert-WorkPackage ($schema.properties.schemaVersion.const -eq '1.0.0') 'Unsupported work-package schema.'

$required = @('schemaVersion','workPackageId','phase','executionStatus','gateStatus','blockerCode','predecessors','identity','allowedPaths','forbiddenPaths','owner','commands','fixtures','comparators','reports','uncovered','rollback','decision')
foreach ($name in $required) {
    Assert-WorkPackage ($null -ne $record.PSObject.Properties[$name]) "Missing work-package field: $name"
}

Assert-WorkPackage ($record.schemaVersion -eq '1.0.0') 'Work-package schemaVersion drifted.'
Assert-WorkPackage ($record.workPackageId -match '^M[3-7]-[A-Z0-9-]+$') 'Invalid workPackageId.'
Assert-WorkPackage (@('M3','M4','M5','M6','M7') -contains $record.phase) 'Invalid phase.'
Assert-WorkPackage (@('NotStarted','InProgress','Passed') -contains $record.executionStatus) 'Invalid executionStatus.'
Assert-WorkPackage (@('Blocked','ReadyForReview','Passed') -contains $record.gateStatus) 'Invalid gateStatus.'
Assert-WorkPackage ($record.phase -eq $record.workPackageId.Substring(0, 2)) 'workPackageId phase does not match phase.'
Assert-WorkPackage (@($record.allowedPaths).Count -gt 0) 'At least one allowed path is required.'
Assert-WorkPackage (-not (@($record.allowedPaths) | Where-Object { @($record.forbiddenPaths) -contains $_ })) 'A path is both allowed and forbidden.'
Assert-WorkPackage ($null -ne $record.rollback.flag -and $null -ne $record.rollback.artifact -and $null -ne $record.rollback.restoreCommand) 'Rollback plan is incomplete.'
Assert-WorkPackage ($null -ne $record.decision.preparedBy -and $null -ne $record.decision.reviewedBy -and $null -ne $record.decision.approvedBy) 'Three-person decision fields are required, even when the same person is recorded.'

if ($record.executionStatus -eq 'Passed' -or $record.gateStatus -eq 'Passed') {
    Assert-WorkPackage ($record.executionStatus -eq 'Passed' -and $record.gateStatus -eq 'Passed') 'Passed execution and gate statuses must move together.'
    Assert-WorkPackage ($record.blockerCode -eq 'None') 'Passed work package cannot retain a blocker.'
    Assert-WorkPackage ($record.rollback.verified -eq $true) 'Passed work package requires verified rollback.'
    Assert-WorkPackage ($record.decision.conclusion -eq 'Approved') 'Passed work package requires an Approved decision.'
    Assert-WorkPackage (@($record.decision.reportHashes).Count -gt 0) 'Passed work package requires report hashes.'
} else {
    Assert-WorkPackage ($record.decision.conclusion -ne 'Approved' -or $record.gateStatus -eq 'ReadyForReview') 'An Approved decision cannot exist while the record is blocked or not started.'
}

Write-Output "Work-package record contract passed: $($record.workPackageId) [$($record.executionStatus)/$($record.gateStatus)/$($record.blockerCode)]."
