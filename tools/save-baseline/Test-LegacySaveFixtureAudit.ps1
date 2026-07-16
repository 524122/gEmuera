[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $ProjectRoot 'tools\save-baseline\LegacySaveFixtureAudit.psm1'
$schemaPath = Join-Path $ProjectRoot 'tools\save-baseline\legacy-save-fixture-audit.schema.json'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-sav-fixture-audit-' + [Guid]::NewGuid().ToString('N'))

function Assert-Audit([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }

try {
    New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
    $saveDir = Join-Path $tempRoot 'sav'
    New-Item -ItemType Directory -Path $saveDir -Force | Out-Null
    $ordinary = [byte[]](0x89,0x45,0x52,0x41,0x0D,0x0A,0x1A,0x0A,0x10,0x07,0,0,0,0,0,0,0x00,0xFF)
    [IO.File]::WriteAllBytes((Join-Path $saveDir 'save00.sav'), $ordinary)
    $zip = [byte[]](0x89,0x45,0x52,0x41,0x5A,0x49,0x50,0x0A,0x10,0x07,0,0,0,0,0,0,0x1F,0x8B)
    [IO.File]::WriteAllBytes((Join-Path $saveDir 'global.sav'), $zip)
    [IO.File]::WriteAllBytes((Join-Path $saveDir 'var_1.dat'), [byte[]](1,2,3,4))
    [IO.File]::WriteAllText((Join-Path $tempRoot 'notes.sav'), "text fixture with enough bytes`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllBytes((Join-Path $tempRoot 'other.dat'), [byte[]](5,6,7))

    Assert-Audit (Test-Path -LiteralPath $modulePath -PathType Leaf) 'M0-SAV fixture audit module missing.'
    Assert-Audit (Test-Path -LiteralPath $schemaPath -PathType Leaf) 'M0-SAV fixture audit schema missing.'
    $schema = Get-Content -Raw -Encoding UTF8 -LiteralPath $schemaPath | ConvertFrom-Json
    Assert-Audit ($schema.properties.workPackage.const -ceq 'M0-SAV-01' -and $schema.properties.sourceReadOnly.const -eq $true -and $schema.properties.roundTripStatus.const -ceq 'Uncovered') 'M0-SAV fixture audit schema contract drifted.'
    Import-Module $modulePath -Force
    $report = New-LegacySaveFixtureAuditReport -RootPath $tempRoot
    $repeatReport = New-LegacySaveFixtureAuditReport -RootPath $tempRoot
    Assert-Audit ($repeatReport.fixtureSetHash -ceq $report.fixtureSetHash) 'M0-SAV fixture canonical hash is not stable across identical audits.'
    Assert-Audit ($report.workPackage -ceq 'M0-SAV-01' -and $report.gateStatus -ceq 'Blocked' -and $report.result -ceq 'Partial') 'M0-SAV fixture audit status advanced.'
    Assert-Audit ($report.sourceReadOnly -eq $true -and $report.roundTripStatus -ceq 'Uncovered' -and $report.profileBindingStatus -ceq 'Unbound') 'M0-SAV fixture audit mutability/profile contract drifted.'
    Assert-Audit ($report.sourceSaveFilesRead -eq 4 -and @($report.fixtures).Count -eq 4) 'M0-SAV fixture candidate policy drifted.'
    $normal = @($report.fixtures | Where-Object path -ceq 'sav/save00.sav')[0]
    Assert-Audit ($normal.fileTypeCandidate -ceq 'Normal' -and $normal.wire.headerKind -ceq 'Ordinary1808' -and $normal.wire.formatStatus -ceq 'HeaderOnly' -and $normal.wire.version -eq 1808 -and $normal.wire.offsets.payload.offset -eq 16) 'M0-SAV ordinary header/offset contract failed.'
    $compressed = @($report.fixtures | Where-Object path -ceq 'sav/global.sav')[0]
    Assert-Audit ($compressed.fileTypeCandidate -ceq 'Global' -and $compressed.wire.headerKind -ceq 'GZip1808') 'M0-SAV gzip header contract failed.'
    $text = @($report.fixtures | Where-Object path -ceq 'notes.sav')[0]
    Assert-Audit ($text.wire.headerKind -ceq 'Unknown' -and $text.wire.formatStatus -ceq 'Unrecognized' -and $null -eq $text.wire.offsets.payload.offset) 'M0-SAV unknown/text classification failed.'
    $outputPath = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-sav-fixture-audit-report-' + [Guid]::NewGuid().ToString('N') + '.json')
    Write-LegacySaveFixtureAuditJson -Value $report -Path $outputPath
    Assert-Audit (Test-Path -LiteralPath $outputPath -PathType Leaf) 'M0-SAV fixture audit report was not written.'
    $written = Get-Content -Raw -Encoding UTF8 -LiteralPath $outputPath | ConvertFrom-Json
    Assert-Audit ($written.fixtureSetHash -ceq $report.fixtureSetHash) 'M0-SAV fixture audit hash changed during write.'
    Remove-Item -LiteralPath $outputPath -Force
    Write-Output 'M0 legacy save fixture audit contract tests passed.'
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
    exit 1
}
finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
