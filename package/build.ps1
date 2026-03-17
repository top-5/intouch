<#
.SYNOPSIS
  Build InTouch and create an NSIS installer.

.DESCRIPTION
  1. dotnet publish  → self-contained single-file executable
  2. makensis        → NSIS setup .exe
  Run this script from any directory — it resolves paths relative to the repo root.

.PARAMETER Arch
  Target architecture: x64 (default) or arm64.

.PARAMETER Version
  Semantic version for the build. Defaults to 0.1.0.

.PARAMETER SkipPublish
  Skip dotnet publish (reuse existing publish output).

.PARAMETER SkipInstaller
  Skip NSIS packaging (publish only, no installer).

.EXAMPLE
  .\build.ps1                          # x64 build + installer, version 0.1.0
  .\build.ps1 -Arch arm64 -Version 1.0.0
  .\build.ps1 -SkipInstaller           # publish only, no NSIS
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'arm64')]
    [string]$Arch = 'x64',

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '0.1.0',

    [switch]$SkipPublish,
    [switch]$SkipInstaller
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Paths ---
$repoRoot     = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$projectFile  = Join-Path $repoRoot 'src\InTouch\InTouch.csproj'
$publishDir   = Join-Path $repoRoot "artifacts\publish\$Arch"
$installerDir = Join-Path $repoRoot 'artifacts\installer'

Write-Host ''
Write-Host '=== InTouch Build ===' -ForegroundColor Cyan
Write-Host "  Arch:    $Arch"
Write-Host "  Version: $Version"
Write-Host "  Repo:    $repoRoot"
Write-Host ''

# -------------------------------------------------------
# 1. dotnet publish
# -------------------------------------------------------
if (-not $SkipPublish) {
    Write-Host '--- dotnet publish ---' -ForegroundColor Yellow

    $rid = "win-$Arch"

    if (Test-Path $publishDir) {
        Remove-Item $publishDir -Recurse -Force
    }

    # Build informational version with short commit hash when in a git repo
    $infoVersion = $Version
    $gitCmd = Get-Command git -ErrorAction SilentlyContinue
    if ($gitCmd) {
        $sha = & git -C $repoRoot rev-parse --short HEAD 2>$null
        if ($sha) { $infoVersion = "$Version+$sha" }
    }
    Write-Host "  InformationalVersion: $infoVersion"

    & dotnet publish $projectFile `
        -c Release `
        -r $rid `
        --self-contained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -p:Version=$Version `
        -p:InformationalVersion=$infoVersion `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        throw 'dotnet publish failed with exit code {0}' -f $LASTEXITCODE
    }

    # Remove leftover PDB files (DebugType=none should prevent them, belt-and-suspenders)
    Get-ChildItem $publishDir -Filter '*.pdb' -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Host ''
    Write-Host "Published to: $publishDir" -ForegroundColor Green
    Get-ChildItem $publishDir | Format-Table Name, @{N='Size (MB)';E={'{0:N1}' -f ($_.Length/1MB)}} -AutoSize
}

# -------------------------------------------------------
# 2. NSIS installer
# -------------------------------------------------------
if (-not $SkipInstaller) {
    Write-Host '--- NSIS installer ---' -ForegroundColor Yellow

    # Locate makensis
    $cmd = Get-Command makensis -ErrorAction SilentlyContinue
    $makensis = if ($cmd) { $cmd.Source } else { $null }
    if (-not $makensis) {
        $defaultPaths = @(
            "${env:ProgramFiles(x86)}\NSIS\makensis.exe",
            "$env:ProgramFiles\NSIS\makensis.exe"
        )
        foreach ($p in $defaultPaths) {
            if (Test-Path $p) { $makensis = $p; break }
        }
    }
    if (-not $makensis) {
        throw 'makensis not found. Install NSIS: https://nsis.sourceforge.io/  or  choco install nsis -y'
    }

    Write-Host "  Using: $makensis"

    if (-not (Test-Path $publishDir)) {
        throw "Publish directory not found: $publishDir  (run without -SkipPublish first)"
    }
    if (-not (Test-Path $installerDir)) {
        New-Item $installerDir -ItemType Directory -Force | Out-Null
    }

    $nsiScript = Join-Path $PSScriptRoot 'intouch.nsi'

    & $makensis `
        /DVERSION=$Version `
        /DARCH=$Arch `
        "/DPUBLISH_DIR=$publishDir" `
        "/DOUTPUT_DIR=$installerDir" `
        $nsiScript

    if ($LASTEXITCODE -ne 0) {
        throw 'makensis failed with exit code {0}' -f $LASTEXITCODE
    }

    Write-Host ''
    Write-Host 'Installer created:' -ForegroundColor Green
    Get-ChildItem $installerDir -Filter "*$Arch*" |
        Format-Table Name, @{N='Size (MB)';E={'{0:N1}' -f ($_.Length/1MB)}} -AutoSize
}

Write-Host '=== Done ===' -ForegroundColor Cyan
