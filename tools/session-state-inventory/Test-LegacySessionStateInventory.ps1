[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$toolRoot = Join-Path $ProjectRoot 'tools\session-state-inventory'
$modulePath = Join-Path $toolRoot 'LegacySessionStateInventory.psm1'
$catalogPath = Join-Path $toolRoot 'legacy-session-state-classification.json'
$schemaPath = Join-Path $toolRoot 'legacy-session-state-inventory.schema.json'
$catalogSchemaPath = Join-Path $toolRoot 'legacy-session-state-classification.schema.json'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('gemuera-m0-ses-test-' + [Guid]::NewGuid().ToString('N'))

function Assert-SessionInventoryContract {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-SessionInventoryThrows {
    param([scriptblock]$Action, [string]$Pattern, [string]$Failure)

    $caught = $null
    try { & $Action } catch { $caught = $_ }
    if ($null -eq $caught) { throw $Failure }
    if ($caught.Exception.Message -notmatch $Pattern) {
        throw "$Failure Actual: $($caught.Exception.Message)"
    }
}

function Read-SessionInventoryJson {
    param([string]$Path)
    return (Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json)
}

function Copy-SessionInventoryObject {
    param([object]$Value)
    return ($Value | ConvertTo-Json -Depth 80 | ConvertFrom-Json)
}

function New-SyntheticCatalog {
    return [pscustomobject][ordered]@{
        schemaVersion = '1.0.0'
        workPackage = 'M0-SES-01'
        catalogId = 'synthetic.session-root'
        catalogVersion = '1.0.0'
        entries = @(
            [pscustomobject][ordered]@{
                scope = 'GlobalStatic'; symbol = 'SessionRoot'; legacyOwner = 'legacy-core'; candidateM1Owner = 'LegacySessionFacade';
                crossSessionExpectation = 'MustReset'; expectedReset = 'AssignNull'; fixtureIds = @('SYN-ROOT'); migrationStage = 'M1'; notes = 'Synthetic root.'
            },
            [pscustomobject][ordered]@{
                scope = 'GlobalStatic'; symbol = 'Cache'; legacyOwner = 'legacy-core'; candidateM1Owner = 'LegacySessionFacade';
                crossSessionExpectation = 'MustReset'; expectedReset = 'Clear'; fixtureIds = @('SYN-CACHE'); migrationStage = 'M1'; notes = 'Synthetic cache.'
            },
            [pscustomobject][ordered]@{
                scope = 'Program'; symbol = 'ExeDir'; legacyOwner = 'legacy-startup'; candidateM1Owner = 'LegacySessionFacade';
                crossSessionExpectation = 'CandidateScoped'; expectedReset = 'CandidateOverwrite'; fixtureIds = @('SYN-PATH'); migrationStage = 'M1'; notes = 'Synthetic path.'
            },
            [pscustomobject][ordered]@{
                scope = 'Program'; symbol = 'CoreProfile'; legacyOwner = 'legacy-startup'; candidateM1Owner = 'LegacySessionFacade';
                crossSessionExpectation = 'CandidateScoped'; expectedReset = 'CandidateOverwrite'; fixtureIds = @('SYN-PROFILE'); migrationStage = 'M1'; notes = 'Synthetic profile.'
            }
        )
    }
}

try {
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Missing M0-SES-01 module: $modulePath"
    }

    Import-Module $modulePath -Force
    $catalog = Read-SessionInventoryJson $catalogPath
    $schema = Read-SessionInventoryJson $schemaPath
    $catalogSchema = Read-SessionInventoryJson $catalogSchemaPath

    $actual = New-LegacySessionRootInventory -ProjectRoot $ProjectRoot -Catalog $catalog
    Assert-SessionInventoryContract ($actual.workPackage -eq 'M0-SES-01') 'Unexpected session inventory work package.'
    Assert-SessionInventoryContract ($schema.properties.workPackage.const -eq 'M0-SES-01') 'Session inventory schema work package drifted.'
    Assert-SessionInventoryContract ($catalogSchema.properties.workPackage.const -eq 'M0-SES-01') 'Session inventory catalog schema work package drifted.'
    Assert-SessionInventoryContract ($actual.executionStatus -eq 'InProgress' -and $actual.gateStatus -eq 'Blocked' -and $actual.blockerCode -eq 'EvidenceMissing') 'Session inventory gate status was incorrectly advanced.'
    Assert-SessionInventoryContract ($actual.result -eq 'Partial') 'Session inventory must not claim M1 completion.'
    Assert-SessionInventoryContract ($actual.coverage.globalStaticStateCount -eq 14) 'Unexpected GlobalStatic root state count.'
    Assert-SessionInventoryContract ($actual.coverage.programStateCount -eq 17) 'Unexpected Program state count.'
    Assert-SessionInventoryContract ($actual.coverage.catalogMappedCount -eq 31 -and $actual.coverage.unmappedCount -eq 0) 'Every observed root state must have one catalog entry.'
    Assert-SessionInventoryContract ($actual.currentRuntimeIsolation.status -eq 'Failed') 'Legacy static isolation was incorrectly advanced.'
    Assert-SessionInventoryContract ($actual.parserVmConsumption.status -eq 'NotConsumed') 'Inventory was incorrectly wired into Parser/VM.'
    Assert-SessionInventoryContract ($actual.m1Eligibility.status -eq 'Blocked') 'M1 eligibility was incorrectly advanced.'

    $console = @($actual.entries | Where-Object { $_.scope -eq 'GlobalStatic' -and $_.symbol -eq 'Console' })
    Assert-SessionInventoryContract ($console.Count -eq 1 -and $console[0].reset.status -eq 'ExplicitlyCleared') 'GlobalStatic.Console reset coverage drifted.'
    $ctrlZ = @($actual.entries | Where-Object { $_.scope -eq 'GlobalStatic' -and $_.symbol -eq 'ctrlZ' })
    Assert-SessionInventoryContract ($ctrlZ.Count -eq 1 -and $ctrlZ[0].reset.status -eq 'ExplicitlyCleared') 'GlobalStatic.ctrlZ canary reset coverage drifted.'
    Assert-SessionInventoryContract (@($ctrlZ[0].reset.sites | Where-Object { $_.operation -eq 'Canary:ClearOrClose' }).Count -eq 1) 'GlobalStatic.ctrlZ canary reset site was not classified.'
    $stackList = @($actual.entries | Where-Object { $_.scope -eq 'GlobalStatic' -and $_.symbol -eq 'StackList' })
    Assert-SessionInventoryContract ($stackList.Count -eq 1 -and $stackList[0].reset.status -eq 'ExplicitlyCleared') 'GlobalStatic.StackList canary reset coverage drifted.'
    Assert-SessionInventoryContract (@($stackList[0].reset.sites | Where-Object { $_.operation -eq 'Canary:ClearOrClose' }).Count -eq 1) 'GlobalStatic.StackList canary reset site was not classified.'
    $profile = @($actual.entries | Where-Object { $_.scope -eq 'Program' -and $_.symbol -eq 'CoreProfile' })
    Assert-SessionInventoryContract ($profile.Count -eq 1 -and $profile[0].writeSites.Count -ge 1) 'Program.CoreProfile writer evidence is missing.'
    Assert-SessionInventoryContract ($profile[0].reset.status -eq 'NoLegacyProgramReset') 'Program.CoreProfile reset semantics were incorrectly inferred.'
    Assert-SessionInventoryContract ($actual.inventorySetHash -match '^[0-9a-f]{64}$') 'Session inventory hash is invalid.'

    $syntheticScripts = Join-Path $testRoot 'Scripts\Emuera'
    New-Item -ItemType Directory -Path $syntheticScripts -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $syntheticScripts 'GlobalStatic.cs'), @'
namespace MinorShift.Emuera
{
    internal static class GlobalStatic
    {
        public static object SessionRoot;
        public static readonly System.Collections.Generic.Dictionary<string, string> Cache = new System.Collections.Generic.Dictionary<string, string>();
        public static void Reset()
        {
            SessionRoot = null;
            Cache.Clear();
        }
    }
}
'@, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $syntheticScripts 'Program.cs'), @'
namespace MinorShift.Emuera
{
    public enum EmueraCoreProfile { V24Pure, Snake }
    public static class Program
    {
        public static string ExeDir { get; private set; }
        public static EmueraCoreProfile CoreProfile { get; private set; } = EmueraCoreProfile.V24Pure;
        public static void Main()
        {
            ExeDir = "game";
            CoreProfile = EmueraCoreProfile.Snake;
        }
    }
}
'@, [Text.UTF8Encoding]::new($false))

    $syntheticCatalog = New-SyntheticCatalog
    $synthetic = New-LegacySessionRootInventory -ProjectRoot $testRoot -Catalog $syntheticCatalog
    Assert-SessionInventoryContract ($synthetic.coverage.globalStaticStateCount -eq 2) 'Synthetic GlobalStatic state count drifted.'
    Assert-SessionInventoryContract ($synthetic.coverage.programStateCount -eq 2) 'Synthetic Program state count drifted.'
    Assert-SessionInventoryContract ((@($synthetic.entries | Where-Object { $_.symbol -eq 'SessionRoot' })[0].reset.status -eq 'ExplicitlyCleared')) 'Synthetic assigned reset was not detected.'
    Assert-SessionInventoryContract ((@($synthetic.entries | Where-Object { $_.symbol -eq 'Cache' })[0].reset.status -eq 'ExplicitlyCleared')) 'Synthetic collection clear reset was not detected.'

    $reorderedCatalog = Copy-SessionInventoryObject $syntheticCatalog
    [array]::Reverse($reorderedCatalog.entries)
    $reordered = New-LegacySessionRootInventory -ProjectRoot $testRoot -Catalog $reorderedCatalog
    Assert-SessionInventoryContract ($synthetic.inventorySetHash -eq $reordered.inventorySetHash) 'Catalog enumeration order changed the inventory hash.'

    $missingCatalog = Copy-SessionInventoryObject $syntheticCatalog
    $missingCatalog.entries = @($missingCatalog.entries | Where-Object { $_.symbol -ne 'CoreProfile' })
    Assert-SessionInventoryThrows { New-LegacySessionRootInventory $testRoot $missingCatalog } 'does not classify observed root state' 'Missing catalog mapping was accepted.'

    $duplicateCatalog = Copy-SessionInventoryObject $syntheticCatalog
    $duplicateCatalog.entries += Copy-SessionInventoryObject $duplicateCatalog.entries[0]
    Assert-SessionInventoryThrows { New-LegacySessionRootInventory $testRoot $duplicateCatalog } 'Duplicate catalog mapping' 'Duplicate catalog mapping was accepted.'

    $globalSource = Join-Path $syntheticScripts 'GlobalStatic.cs'
    $beforeMutation = New-LegacySessionRootInventory -ProjectRoot $testRoot -Catalog $syntheticCatalog
    [IO.File]::AppendAllText($globalSource, "`n// source identity mutation`n", [Text.UTF8Encoding]::new($false))
    $afterMutation = New-LegacySessionRootInventory -ProjectRoot $testRoot -Catalog $syntheticCatalog
    Assert-SessionInventoryContract ($beforeMutation.inventorySetHash -ne $afterMutation.inventorySetHash) 'Source identity mutation did not change inventory hash.'

    $outputPath = Join-Path $testRoot 'legacy-session-state-inventory.json'
    $written = New-LegacySessionRootInventory -ProjectRoot $testRoot -Catalog $syntheticCatalog -OutputPath $outputPath
    Assert-SessionInventoryContract (Test-Path -LiteralPath $outputPath -PathType Leaf) 'Session inventory report was not written.'
    Assert-SessionInventoryContract ((Read-SessionInventoryJson $outputPath).inventorySetHash -eq $written.inventorySetHash) 'Written session inventory hash drifted.'

    $newRootSource = Get-Content -Raw -Encoding UTF8 -LiteralPath $globalSource
    $newRootSource = $newRootSource.Replace('public static void Reset()', "public static bool Unmapped;`n        public static void Reset()")
    [IO.File]::WriteAllText($globalSource, $newRootSource, [Text.UTF8Encoding]::new($false))
    Assert-SessionInventoryThrows { New-LegacySessionRootInventory $testRoot $syntheticCatalog } 'does not classify observed root state: GlobalStatic\.Unmapped' 'New GlobalStatic root state was accepted without a catalog mapping.'

    Write-Output 'M0 legacy session root inventory contract tests passed.'
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
        $resolvedTest.Contains('gemuera-m0-ses-test-') -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
