param(
    [string]$ForkPath = '',
    [string]$Ref = '22273e7',
    [string]$OutDir = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'dist\ipsec' }
if (-not $ForkPath) {
    $candidate = Join-Path (Split-Path -Parent $root) 'ikev2fork'
    if (Test-Path $candidate) { $ForkPath = $candidate }
}
if (-not $ForkPath -or -not (Test-Path $ForkPath)) {
    throw 'Set -ForkPath to the crimson-ipsec clone (the fork of n0madic/go-ipsec).'
}

$targets = @(
    @{ Tag = 'x64';   Arch = 'amd64'; Arm = '' },
    @{ Tag = 'arm64'; Arch = 'arm64'; Arm = '' },
    @{ Tag = 'arm';   Arch = 'arm';   Arm = '7' }
)

Push-Location $ForkPath
try {
    foreach ($t in $targets) {
        $env:CGO_ENABLED = '0'
        $env:GOOS = 'linux'
        $env:GOARCH = $t.Arch
        $env:GOARM = $t.Arm
        & go build -trimpath -ldflags "-s -w -X main.version=$Ref" -o ("bin\crimsonipsec-linux-" + $t.Tag) .\cmd\crimsonipsec
        if ($LASTEXITCODE -ne 0) { throw "go build failed for linux/$($t.Arch)" }
        $dest = Join-Path $OutDir $t.Tag
        New-Item -ItemType Directory -Force $dest | Out-Null
        $binary = Join-Path $dest 'crimsonipsec'
        Copy-Item ("bin\crimsonipsec-linux-" + $t.Tag) $binary -Force
        $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $binary).Hash.ToLowerInvariant()
        "$hash  crimsonipsec" | Set-Content -Encoding ASCII (Join-Path $dest 'crimsonipsec.sha256')
        Write-Host "built $binary (linux/$($t.Arch)$($(if ($t.Arm) { ' armv' + $t.Arm } else { '' })) from ref $Ref)"
    }
}
finally {
    Remove-Item Env:\GOOS, Env:\GOARCH, Env:\GOARM -ErrorAction SilentlyContinue
    Pop-Location
}
