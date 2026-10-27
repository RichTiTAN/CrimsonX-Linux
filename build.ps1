# ---------------------------------------------------------------------------
# CrimsonX Linux - one command for the whole release.
#
#   .\build.ps1               verify and package   (build + tests + all 3 arches, ~6 min)
#   .\build.ps1 -SkipPack     build + tests only   (~40 s, the usual loop while coding)
#   .\build.ps1 -SkipTests    build + package      (when the tests already passed)
#   .\build.ps1 -Quick        compile only         (~15 s, just "does it still compile")
#
# Packages into the same place pack-linux.ps1 does:
#   <OutDir>\x64\CrimsonX\  <OutDir>\arm64\CrimsonX\  <OutDir>\arm\CrimsonX\
#   <OutDir>\dist\CrimsonX-linux-<arch>.tar.gz  (+ .sha256 sidecar)
#
# The version numbers are checked before anything is built. The app reports the
# constant in UpdateService, version.json tells everyone else what exists, and a
# release where the two disagree either offers an update nobody receives or, worse,
# loops forever offering a build that still reports the old number.
# ---------------------------------------------------------------------------
param(
    [switch]$Quick,
    [switch]$SkipTests,
    [switch]$SkipPack,
    [string]$OutDir = 'E:\Desktop\CrimsonXAvalonia\Linux'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$packing = (-not $SkipPack) -and (-not $Quick)
$testing = (-not $SkipTests) -and (-not $Quick)

function Read-Versions {
    $csproj = Get-Content -LiteralPath (Join-Path $root 'CrimsonX.csproj') -Raw
    $proj = [regex]::Match($csproj, '<Version>\s*([^<]+?)\s*</Version>').Groups[1].Value
    $svc = Get-Content -LiteralPath (Join-Path $root 'Services\UpdateService.cs') -Raw
    $app = [regex]::Match($svc, 'AppVersion\s*=\s*"([^"]+)"').Groups[1].Value
    $json = Get-Content -LiteralPath (Join-Path $root 'version.json') -Raw | ConvertFrom-Json
    [pscustomobject]@{
        Project   = $proj
        App       = $app
        Published = [string]$json.version
        MinAuto   = [string]$json.minAutoUpdateVersion
    }
}

$v = Read-Versions
Write-Host 'CrimsonX Linux build'
Write-Host "  csproj  <Version>          : $($v.Project)"
Write-Host "  UpdateService.AppVersion   : $($v.App)"
Write-Host "  version.json version       : $($v.Published)"
Write-Host "  version.json minAutoUpdate : $($v.MinAuto)"

$problems = @()
if (-not $v.Project) { $problems += 'CrimsonX.csproj has no <Version>' }
if (-not $v.App) { $problems += 'UpdateService.AppVersion could not be read' }
if ($v.App -ne $v.Project) { $problems += "UpdateService.AppVersion ($($v.App)) and the csproj <Version> ($($v.Project)) disagree" }
if ($v.Published -ne $v.App) { $problems += "version.json says $($v.Published) but this build reports $($v.App)" }

if ($problems.Count -gt 0) {
    Write-Host ''
    foreach ($p in $problems) { Write-Host "  ! $p" -ForegroundColor Yellow }
    Write-Host ''
    Write-Host '  Bump all three together before releasing:' -ForegroundColor Yellow
    Write-Host '    CrimsonX.csproj                      <Version> and <AssemblyVersion>/<FileVersion>'
    Write-Host '    Services/UpdateService.cs            AppVersion'
    Write-Host '    version.json                         version'
    if ($packing) { throw 'refusing to package with inconsistent version numbers' }
    Write-Host ''
}

# ── compile ──────────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '== build =='
$sw = [Diagnostics.Stopwatch]::StartNew()
& dotnet build (Join-Path $root 'CrimsonX.slnx') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Write-Host ("   ok  ({0:N1}s)" -f $sw.Elapsed.TotalSeconds)

# ── test ─────────────────────────────────────────────────────────────────────
if ($testing) {
    Write-Host ''
    Write-Host '== test =='
    $sw.Restart()
    & dotnet test (Join-Path $root 'CrimsonX.Tests\CrimsonX.Tests.csproj') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
    Write-Host ("   ok  ({0:N1}s)" -f $sw.Elapsed.TotalSeconds)
}

# ── package ──────────────────────────────────────────────────────────────────
if ($packing) {
    Write-Host ''
    Write-Host '== package =='
    $sw.Restart()
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'pack-linux.ps1') -OutDir $OutDir
    if ($LASTEXITCODE -ne 0) { throw 'packaging failed' }
    Write-Host ("   ok  ({0:N0}s)" -f $sw.Elapsed.TotalSeconds)
}

# ── what now ─────────────────────────────────────────────────────────────────
if ($packing) {
    Write-Host ''
    Write-Host 'Upload these six files to the release:'
    Get-ChildItem -LiteralPath (Join-Path $OutDir 'dist') -File |
        Sort-Object Name |
        ForEach-Object { Write-Host ("   {0,-34} {1,7:N1} MB" -f $_.Name, ($_.Length / 1MB)) }

    Write-Host ''
    Write-Host 'The three folders next to dist\ (x64, arm64, arm) are the same build,'
    Write-Host 'for anyone who would rather download and run install.sh by hand.'
    Write-Host ''
    Write-Host 'Before publishing:'
    Write-Host '   - publish it as a normal release, NOT a draft or a pre-release,'
    Write-Host '     because clients read releases/latest/download/'
    Write-Host "   - version.json must be on main and say $($v.App)"
}
else {
    Write-Host ''
    Write-Host "Nothing was packaged (-SkipPack or -Quick). Run .\build.ps1 with no flags for the release."
}
