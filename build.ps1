param(
    [switch]$Quick,
    [switch]$SkipTests,
    [switch]$SkipPack,
    [string]$OutDir = 'E:\Desktop\CrimsonXAvalonia\Linux'
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$packing = (-not $SkipPack) -and (-not $Quick)
$testing = (-not $SkipTests) -and (-not $Quick)

$projVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $root 'CrimsonX.csproj') -Raw),
    '<Version>\s*([^<]+?)\s*</Version>').Groups[1].Value
$appVersion = [regex]::Match((Get-Content -LiteralPath (Join-Path $root 'Services\UpdateService.cs') -Raw),
    'AppVersion\s*=\s*"([^"]+)"').Groups[1].Value

$publishedVersion = ''
$versionFile = Join-Path $root 'version.json'
if (Test-Path -LiteralPath $versionFile) {
    try { $publishedVersion = [string]((Get-Content -LiteralPath $versionFile -Raw | ConvertFrom-Json).version) } catch { }
}

Write-Host 'CrimsonX Linux build'
Write-Host "  csproj Version           : $projVersion"
Write-Host "  UpdateService.AppVersion : $appVersion"
Write-Host ("  version.json             : " + $(if ($publishedVersion) { $publishedVersion } else { 'not in this tree, read from GitHub' }))

if ($appVersion -ne $projVersion) {
    Write-Host ''
    Write-Host "  ! AppVersion ($appVersion) and csproj Version ($projVersion) disagree" -ForegroundColor Yellow
    throw 'version numbers disagree'
}
if ($publishedVersion -and $publishedVersion -ne $appVersion) {
    Write-Host "  note: version.json stands at $publishedVersion, this build reports $appVersion" -ForegroundColor DarkYellow
}

Write-Host ''
Write-Host '== build =='
$sw = [Diagnostics.Stopwatch]::StartNew()
& dotnet build (Join-Path $root 'CrimsonX.slnx') -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
Write-Host ("   ok  ({0:N1}s)" -f $sw.Elapsed.TotalSeconds)

if ($testing) {
    Write-Host ''
    Write-Host '== test =='
    $sw.Restart()
    & dotnet test (Join-Path $root 'CrimsonX.Tests\CrimsonX.Tests.csproj') -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
    Write-Host ("   ok  ({0:N1}s)" -f $sw.Elapsed.TotalSeconds)
}

if ($packing) {
    Write-Host ''
    Write-Host '== package =='
    $sw.Restart()
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'pack-linux.ps1') -OutDir $OutDir
    if ($LASTEXITCODE -ne 0) { throw 'packaging failed' }
    Write-Host ("   ok  ({0:N0}s)" -f $sw.Elapsed.TotalSeconds)

    Write-Host ''
    Write-Host 'Upload these six files to the release:'
    Get-ChildItem -LiteralPath (Join-Path $OutDir 'dist') -File | Sort-Object Name | ForEach-Object {
        Write-Host ("   {0,-34} {1,7:N1} MB" -f $_.Name, ($_.Length / 1MB))
    }
    Write-Host ''
    Write-Host "Then set version.json on GitHub to $appVersion."
}
