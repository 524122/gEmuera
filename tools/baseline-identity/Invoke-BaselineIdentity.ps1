[CmdletBinding()]
param(
    [string]$ProjectRoot = (Get-Location).Path,
    [string]$OutputDirectory,
    [string]$GameRoot,
    [string[]]$ArtifactPath = @(),
    [string]$GodotExecutable,
    [string]$JavaExecutable,
    [string]$AndroidSdkRoot,
    [string]$AndroidNdkRoot,
    [string]$ExportTemplatesRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

try {
    Import-Module (Join-Path $PSScriptRoot 'BaselineIdentity.psm1') -Force
    $result = Invoke-BaselineIdentity @PSBoundParameters
    $result | ConvertTo-Json -Depth 10
    exit 0
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
