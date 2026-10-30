# ---------------------------------------------------------------------------
# Builds the CrimsonX Linux release, one folder per architecture.
#
#   .\pack-linux.ps1                      -> E:\Desktop\CrimsonXAvalonia\Linux
#   .\pack-linux.ps1 -OutDir D:\somewhere
#
# Output layout (zip these yourself whenever you want):
#
#   Linux\x64\CrimsonX\        single-file executable + engines + fonts
#   Linux\arm64\CrimsonX\
#   Linux\arm\CrimsonX\
#
#   Linux\dist\CrimsonX-linux-x64.tar.gz        + .sha256, for the one-line installer
#   Linux\dist\CrimsonX-linux-arm64.tar.gz      + .sha256
#   Linux\dist\CrimsonX-linux-arm.tar.gz        + .sha256
#
# Every folder is complete and self-contained: the app, matching xray and
# sing-box binaries, the xray geo data, the bundled fonts and install.sh. The
# dist archives hold the same contents and are what a release should attach,
# because install/get-crimsonx.sh downloads them by name.
# ---------------------------------------------------------------------------
param(
    [string]$OutDir = 'E:\Desktop\CrimsonXAvalonia\Linux',
    [string]$Work   = 'D:\cache\pack',
    [string]$AwgDir = 'D:\cache\pack\awg'
)

$ErrorActionPreference = 'Stop'
$env:NUGET_PACKAGES = 'D:\cache\nuget'
$env:TEMP = 'D:\cache\tmp'
$env:TMP  = 'D:\cache\tmp'

$ProjectRoot = 'E:\Desktop\CrimsonXLinux'
$Project     = Join-Path $ProjectRoot 'CrimsonX.csproj'
$XrayVersion = 'v26.10.21'
$SbVersion   = 'v1.14.4'

$Targets = @(
    @{ Rid = 'linux-x64';   Tag = 'x64';   Awg = 'amd64'; Xray = 'Xray-linux-64.zip';        Sb = 'sing-box-1.14.4-linux-amd64.tar.gz' },
    @{ Rid = 'linux-arm64'; Tag = 'arm64'; Awg = 'arm64'; Xray = 'Xray-linux-arm64-v8a.zip'; Sb = 'sing-box-1.14.4-linux-arm64.tar.gz' },
    @{ Rid = 'linux-arm';   Tag = 'arm';   Awg = 'arm';   Xray = 'Xray-linux-arm32-v7a.zip'; Sb = 'sing-box-1.14.4-linux-armv7.tar.gz' }
)

$GeoFiles = [ordered]@{
    'geoip.dat'   = 'https://github.com/v2fly/geoip/releases/latest/download/geoip.dat'
    'geosite.dat' = 'https://github.com/v2fly/domain-list-community/releases/latest/download/dlc.dat'
}

$DownloadDir = Join-Path $Work 'dl'
New-Item -ItemType Directory -Force -Path $DownloadDir, $OutDir | Out-Null

function Fetch([string]$Url, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination) { return }
    Write-Host "  download $(Split-Path -Leaf $Destination)"
    # Timeouts matter: without them a throttled connection hangs the whole build.
    & curl.exe -fsSL --retry 5 --retry-all-errors --retry-delay 2 `
        --connect-timeout 20 --max-time 900 `
        -o $Destination $Url
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $Destination)) {
        Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
        throw "download failed: $Url"
    }
}

Write-Host 'CrimsonX Linux release build'
Write-Host "  output: $OutDir"

Write-Host 'pre-fetching shared data'
foreach ($name in $GeoFiles.Keys) {
    Fetch $GeoFiles[$name] (Join-Path $DownloadDir $name)
}

$Results = New-Object System.Collections.ArrayList

foreach ($t in $Targets) {
    Write-Host ''
    Write-Host "=== $($t.Rid) ==="

    $Package = Join-Path $OutDir (Join-Path $t.Tag 'CrimsonX')
    if (Test-Path -LiteralPath $Package) { Remove-Item -LiteralPath $Package -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $Package | Out-Null

    Write-Host '  publish (single file, self-contained)'
    & dotnet publish $Project -c Release -r $t.Rid --self-contained true -o $Package --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "publish failed for $($t.Rid)" }

    # ── xray + geo data ─────────────────────────────────────────────────────
    $xrayZip = Join-Path $DownloadDir $t.Xray
    Fetch ("https://github.com/XTLS/Xray-core/releases/download/$XrayVersion/" + $t.Xray) $xrayZip

    $xrayDir = Join-Path $Package 'Data\xray'
    New-Item -ItemType Directory -Force -Path $xrayDir | Out-Null
    $xrayTmp = Join-Path $Work ($t.Tag + '-xraytmp')
    if (Test-Path -LiteralPath $xrayTmp) { Remove-Item -LiteralPath $xrayTmp -Recurse -Force }
    Expand-Archive -LiteralPath $xrayZip -DestinationPath $xrayTmp -Force
    $xrayExe = Get-ChildItem -LiteralPath $xrayTmp -Recurse -File -Filter 'xray' | Select-Object -First 1
    if (-not $xrayExe) { throw "xray binary missing inside $($t.Xray)" }
    Copy-Item -LiteralPath $xrayExe.FullName -Destination (Join-Path $xrayDir 'xray') -Force
    foreach ($name in $GeoFiles.Keys) {
        Copy-Item -LiteralPath (Join-Path $DownloadDir $name) -Destination (Join-Path $xrayDir $name) -Force
    }

    # ── sing-box ────────────────────────────────────────────────────────────
    $sbTar = Join-Path $DownloadDir $t.Sb
    Fetch ("https://github.com/SagerNet/sing-box/releases/download/$SbVersion/" + $t.Sb) $sbTar

    $sbDir = Join-Path $Package 'Data\sing_box'
    New-Item -ItemType Directory -Force -Path $sbDir | Out-Null
    $sbTmp = Join-Path $Work ($t.Tag + '-sbtmp')
    if (Test-Path -LiteralPath $sbTmp) { Remove-Item -LiteralPath $sbTmp -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $sbTmp | Out-Null
    & tar.exe -xzf $sbTar -C $sbTmp
    if ($LASTEXITCODE -ne 0) { throw "could not extract $($t.Sb)" }
    $sbExe = Get-ChildItem -LiteralPath $sbTmp -Recurse -File -Filter 'sing-box' | Select-Object -First 1
    if (-not $sbExe) { throw "sing-box binary missing inside $($t.Sb)" }
    Copy-Item -LiteralPath $sbExe.FullName -Destination (Join-Path $sbDir 'sing-box') -Force

    # ── amnezia-box (AmneziaWG engine) ──────────────────────────────────────
    $awgSrc = Join-Path $AwgDir $t.Awg
    $awgExe = Join-Path $awgSrc 'amnezia-box'
    if (-not (Test-Path -LiteralPath $awgExe)) {
        $awgSrc = Join-Path $AwgDir $t.Tag
        $awgExe = Join-Path $awgSrc 'amnezia-box'
    }
    if (-not (Test-Path -LiteralPath $awgExe)) {
        throw "amnezia-box for $($t.Rid) is missing: expected $awgExe (build it with GOOS=linux GOARCH=$($t.Awg) and the with_awg tag set)"
    }
    $awgOut = Join-Path $Package 'Data\awg'
    New-Item -ItemType Directory -Force -Path $awgOut | Out-Null
    Copy-Item -LiteralPath $awgExe -Destination (Join-Path $awgOut 'amnezia-box') -Force

    # ── launcher files (exec permissions are applied by install.sh) ─────────
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'install\install.sh')       -Destination $Package -Force
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'install\crimsonx.desktop') -Destination $Package -Force
    Copy-Item -LiteralPath (Join-Path $ProjectRoot 'LICENSE.txt')              -Destination $Package -Force
    $fontDir = Join-Path $Package 'fonts'
    New-Item -ItemType Directory -Force -Path $fontDir | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $ProjectRoot 'Assets\Fonts') -Filter '*.ttf' |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $fontDir -Force }

    $files = Get-ChildItem -LiteralPath $Package -Recurse -File
    $mb = [math]::Round((($files | Measure-Object Length -Sum).Sum) / 1MB, 1)
    $rootFiles = (Get-ChildItem -LiteralPath $Package -File).Count
    Write-Host "  -> $Package  ($mb MB, $($files.Count) files, $rootFiles at the top level)"

    # ── release archive + checksum ──────────────────────────────────────────
    # tar.gz, not zip: unzip is missing on SteamOS and on minimal images, while
    # tar is always there, and the one-line installer must assume the least. The
    # archive holds the *contents* of the folder, so it unpacks to ./CrimsonX
    # with install.sh next to it - which is what get-crimsonx.sh expects.
    $DistDir = Join-Path $OutDir 'dist'
    New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    $archiveName = "CrimsonX-linux-$($t.Tag).tar.gz"
    $archivePath = Join-Path $DistDir $archiveName
    Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$archivePath.sha256" -Force -ErrorAction SilentlyContinue
    & tar.exe -czf $archivePath -C $Package .
    if ($LASTEXITCODE -ne 0) { throw "could not create $archiveName" }
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $archiveName" | Set-Content -LiteralPath "$archivePath.sha256" -NoNewline -Encoding ascii
    $archiveMB = [math]::Round((Get-Item -LiteralPath $archivePath).Length / 1MB, 1)
    Write-Host "  archive $archiveName ($archiveMB MB)"

    [void]$Results.Add([pscustomobject]@{
        Arch = $t.Tag; Rid = $t.Rid; SizeMB = $mb; Files = $files.Count; Path = $Package
    })
}

Write-Host ''
Write-Host 'Done. Folders are ready to zip whenever you like.'
$Results | Format-Table -AutoSize
    