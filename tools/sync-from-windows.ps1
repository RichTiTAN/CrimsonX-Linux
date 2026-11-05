param(
    [string]$Upstream = 'E:\Desktop\CrimsonX',
    [string]$From = '',
    [string]$To = 'HEAD',
    [string]$PatchDir = '',
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$manifest = Join-Path $PSScriptRoot 'sync-manifest.txt'
$baselineFile = Join-Path $PSScriptRoot 'sync-baseline.txt'

if (-not (Test-Path $Upstream)) { throw "Upstream repository not found: $Upstream" }
if (-not (Test-Path $manifest)) { throw "Missing $manifest" }

$section = ''
$rewrite = New-Object System.Collections.Generic.List[string]
$adapted = New-Object System.Collections.Generic.List[string]
$paths = New-Object System.Collections.Generic.List[object]
foreach ($line in Get-Content $manifest) {
    $text = $line.Trim()
    if ($text.Length -eq 0 -or $text.StartsWith('#')) { continue }
    if ($text -match '^\[(.+)\]$') { $section = $Matches[1].ToLowerInvariant(); continue }
    switch ($section) {
        'rewrite' { $rewrite.Add($text) }
        'adapted' { $adapted.Add($text) }
        'paths' {
            $pair = $text.Split('=', 2)
            if ($pair.Count -eq 2) { $paths.Add([pscustomobject]@{ From = $pair[0]; To = $pair[1] }) }
        }
    }
}

if (-not $From) {
    if (-not (Test-Path $baselineFile)) { throw 'Set -From (the last ported Windows commit) or create tools/sync-baseline.txt' }
    $From = (Get-Content $baselineFile | Where-Object { $_.Trim().Length -gt 0 -and -not $_.Trim().StartsWith('#') } | Select-Object -First 1).Trim()
}
if (-not $PatchDir) { $PatchDir = Join-Path $env:TEMP ('cx-sync-' + (Get-Date -Format 'yyyyMMdd-HHmmss')) }
New-Item -ItemType Directory -Force $PatchDir | Out-Null

Write-Host "sync: $Upstream $From..$To -> $root"
git -C $root remote remove win 2>$null | Out-Null
git -C $root remote add win $Upstream
git -C $root fetch win --no-tags
if ($LASTEXITCODE -ne 0) { throw 'git fetch failed' }

$exported = & git -C $Upstream format-patch "$From..$To" -o $PatchDir --no-signature
$patches = @(Get-ChildItem (Join-Path $PatchDir '*.patch') | Sort-Object Name)
if ($patches.Count -eq 0) { Write-Host 'nothing to sync'; return }
Write-Host "$($patches.Count) patch(es) exported to $PatchDir"

$report = New-Object System.Collections.Generic.List[string]
$failed = $false
$env:GIT_EDITOR = 'true'
foreach ($patch in $patches) {
    $name = $patch.Name
    if (-not $Apply) {
        $report.Add("would apply: $name")
        continue
    }
    git -C $root am -3 $patch.FullName 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { $report.Add("applied: $name"); continue }

    $unmerged = @(& git -C $root diff --name-only --diff-filter=U)
    $blocked = @($unmerged | Where-Object { $adapted -contains $_ })
    if ($blocked.Count -gt 0) {
        $report.Add("STOPPED at $name - platform-adapted file(s) need a hand merge: " + ($blocked -join ', '))
        git -C $root am --abort 2>&1 | Out-Null
        $failed = $true
        break
    }
    foreach ($file in $unmerged) {
        $full = Join-Path $root $file
        if ($rewrite -contains $file) {
            git -C $root checkout --theirs -- $file
            $text = [IO.File]::ReadAllText($full, [Text.Encoding]::UTF8)
            foreach ($pair in $paths) { $text = $text.Replace($pair.From, $pair.To) }
            [IO.File]::WriteAllText($full, $text, (New-Object Text.UTF8Encoding($false)))
            $report.Add("resolved $file from the upstream text, paths rewritten")
        }
        else {
            git -C $root checkout --theirs -- $file
            $report.Add("resolved $file from the upstream text")
        }
        git -C $root add $file
    }
    git -C $root am --continue 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        $report.Add("STOPPED at $name - git am --continue failed")
        $failed = $true
        break
    }
    $report.Add("applied: $name (auto-resolved " + $unmerged.Count + " file(s))")
}

$reportPath = Join-Path $PSScriptRoot 'sync-report.md'
"# sync report $(Get-Date -Format 'yyyy-MM-dd HH:mm')`n`nfrom ``$From`` to ``$To`` (`$Apply = $Apply)`n" | Set-Content -Encoding UTF8 $reportPath
$report | Add-Content -Encoding UTF8 $reportPath
$report | ForEach-Object { Write-Host $_ }
Write-Host "report: $reportPath"
if ($failed) { exit 1 }
