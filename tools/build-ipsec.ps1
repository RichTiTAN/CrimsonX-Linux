param(
    [string]$ForkPath = '',
    [string]$Ref = '22273e7',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'Data\ipsec' }
if (-not $ForkPath) {
    $candidate = Join-Path (Split-Path -Parent $root) 'ikev2fork'
    if (Test-Path $candidate) { $ForkPath = $candidate }
}
if (-not $ForkPath -or -not (Test-Path $ForkPath)) {
    throw 'Set -ForkPath to the crimson-ipsec clone (the fork of n0madic/go-ipsec).'
}

Push-Location $ForkPath
try {
    $env:CGO_ENABLED = '0'
    $env:GOOS = 'windows'
    $env:GOARCH = 'amd64'
    & go build -trimpath -ldflags "-s -w -X main.version=$Ref" -o bin\crimsonipsec.exe .\cmd\crimsonipsec
    if ($LASTEXITCODE -ne 0) { throw 'go build failed' }

    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $target = Join-Path $OutDir 'crimsonipsec.exe'
    Copy-Item 'bin\crimsonipsec.exe' $target -Force
    $version = (& $target -version) -join ''
    Write-Host "installed $target ($version from ref $Ref)"
} finally {
    Pop-Location
}
