[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$toolRoot = Join-Path $ProjectRoot 'tools\save-baseline'
$modulePath = Join-Path $toolRoot 'LegacySaveRoundTripEvidence.psm1'
$schemaPath = Join-Path $toolRoot 'legacy-save-roundtrip-evidence.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-save-roundtrip-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-RoundTrip([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-TestSha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return $sha.ComputeHash([IO.File]::ReadAllBytes($Path)) }
    finally { $sha.Dispose() }
}
function Assert-RoundTripThrows([scriptblock]$Action, [string]$Pattern, [string]$Message) {
    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught -or $caught.Exception.Message -notmatch $Pattern) { throw $Message }
}

try {
    New-Item -ItemType Directory -Path (Join-Path $testRoot 'sav') -Force | Out-Null
    $sourceRoot = (Resolve-Path $testRoot).Path
    $ordinary = [byte[]](0x89,0x45,0x52,0x41,0x0D,0x0A,0x1A,0x0A,0x10,0x07,0,0,0,0,0,0,0x20,0x00,0xAA,0xBB)
    $unknown = [byte[]](0x01,0x02,0x03,0x04,0x05,0x06,0x07,0x08,0x09,0x0A,0x0B,0x0C,0x0D,0x0E,0x0F,0x10)
    [IO.File]::WriteAllBytes((Join-Path $testRoot 'sav/save00.sav'), $ordinary)
    [IO.File]::WriteAllBytes((Join-Path $testRoot 'sav/var_1.dat'), $unknown)
    $sourceHashBefore = Get-TestSha256 (Join-Path $testRoot 'sav/save00.sav')
    $schema = Get-Content -Raw -Encoding UTF8 -LiteralPath $schemaPath | ConvertFrom-Json
    Assert-RoundTrip ($schema.properties.workPackage.const -ceq 'M0-SAV-01' -and $schema.properties.sourceReadOnly.const -eq $true -and $schema.properties.semanticRoundTripStatus.const -ceq 'Uncovered') 'M0-SAV round-trip evidence schema contract drifted.'
    Import-Module $modulePath -Force
    $isolated = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-save-roundtrip-isolated-' + [Guid]::NewGuid().ToString('N'))
    $report = New-LegacySaveRoundTripEvidence -RootPath $sourceRoot -IsolatedCopyRoot $isolated -ProfileId Upstream1808
    Assert-RoundTrip ($report.workPackage -ceq 'M0-SAV-01' -and $report.gateStatus -ceq 'Blocked') 'M0-SAV round-trip evidence advanced the gate.'
    Assert-RoundTrip ($report.sourceReadOnly -eq $true -and $report.profileBindingStatus -ceq 'BoundExplicit') 'M0-SAV round-trip read-only/profile contract failed.'
    Assert-RoundTrip ($report.copyRoundTripStatus -ceq 'Passed' -and $report.semanticRoundTripStatus -ceq 'Uncovered') 'M0-SAV copy round-trip boundary failed.'
    Assert-RoundTrip (@($report.fixtures).Count -eq 2 -and @($report.fixtures | Where-Object copyRoundTripStatus -ceq 'Passed').Count -eq 2) 'M0-SAV isolated fixture results are incomplete.'
    Assert-RoundTrip (([BitConverter]::ToString((Get-TestSha256 (Join-Path $testRoot 'sav/save00.sav')))).Replace('-', '').ToLowerInvariant() -ceq ([BitConverter]::ToString($sourceHashBefore)).Replace('-', '').ToLowerInvariant()) 'M0-SAV source fixture was modified.'
    Assert-RoundTripThrows { New-LegacySaveRoundTripEvidence -RootPath $sourceRoot -IsolatedCopyRoot (Join-Path $sourceRoot 'inside') -ProfileId Upstream1808 } 'outside the source root' 'M0-SAV accepted an in-root isolated copy.'
    Assert-RoundTripThrows { New-LegacySaveRoundTripEvidence -RootPath $sourceRoot -IsolatedCopyRoot (Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-save-roundtrip-invalid-' + [Guid]::NewGuid().ToString('N'))) -ProfileId ([string]'BadProfile') } 'Cannot validate argument' 'M0-SAV accepted an invalid profile pin.'
    Write-Output 'M0 legacy save isolated-copy round-trip evidence tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
