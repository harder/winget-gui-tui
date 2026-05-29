#requires -Version 7.0
<#
.SYNOPSIS
    Publishes the ComBackendSpike as a NativeAOT win-x64 binary and runs it.

.DESCRIPTION
    Verifies that the WinGet COM API (Microsoft.WindowsPackageManager.ComInterop)
    survives Native AOT codegen end-to-end, then smoke-tests the produced .exe
    against the locally-installed winget catalog.

    Runs from any directory — all paths are resolved relative to this script's
    location via $PSScriptRoot.

.PARAMETER Query
    Search term to pass to the spike. Defaults to "powertoys".

.PARAMETER Rid
    Runtime identifier to publish for. Defaults to "win-x64".
    Use "win-arm64" on Windows on ARM hosts.

.PARAMETER SkipPublish
    Skip the publish step and just run the previously-built .exe.

.EXAMPLE
    .\Run-AotSpike.ps1
    Publish + run with the default "powertoys" query.

.EXAMPLE
    .\Run-AotSpike.ps1 -Query "visual studio code"
    Publish + run with a custom query.

.EXAMPLE
    pwsh C:\src\winget-tui-sharp\spikes\ComBackendSpike\Run-AotSpike.ps1 -Rid win-arm64
    Invoke from any directory by absolute path.
#>

[CmdletBinding ()]
param (
    [string] $Query = 'powertoys',
    [ValidateSet ('win-x64', 'win-arm64')]
    [string] $Rid = 'win-x64',
    [switch] $SkipPublish
)

$ErrorActionPreference = 'Stop'

# Anchor every path on the script's own directory so cwd doesn't matter.
$projectDir = $PSScriptRoot
$projectFile = Join-Path $projectDir 'ComBackendSpike.csproj'
$tfm = 'net10.0-windows10.0.26100.0'
$publishDir = Join-Path $projectDir "bin\Release\$tfm\$Rid\publish"
$spikeExe = Join-Path $publishDir 'com-backend-spike.exe'

if (-not (Test-Path -LiteralPath $projectFile))
{
    throw "Project file not found: $projectFile"
}

Write-Host "==> spike project : $projectFile"
Write-Host "==> publish dir   : $publishDir"
Write-Host "==> RID           : $Rid"
Write-Host "==> query         : $Query"
Write-Host ''

if (-not $SkipPublish)
{
    Write-Host '==> dotnet publish (Native AOT)…'
    & dotnet publish $projectFile -r $Rid -c Release -p:PublishAot=true
    if ($LASTEXITCODE -ne 0)
    {
        throw "dotnet publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path -LiteralPath $spikeExe))
{
    throw "Spike binary not found at $spikeExe — publish step may have failed silently."
}

$exeSize = (Get-Item -LiteralPath $spikeExe).Length / 1MB
Write-Host ''
Write-Host ("==> binary size   : {0:N2} MB" -f $exeSize)
Write-Host "==> running       : $spikeExe '$Query'"
Write-Host ''

& $spikeExe $Query
$spikeExit = $LASTEXITCODE

Write-Host ''
Write-Host "==> spike exit code: $spikeExit"
exit $spikeExit
