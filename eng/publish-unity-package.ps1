# Packs the Unity package, binaries included, into .artifacts/unity/ as both a folder and an
# npm-style .tgz ("Add package from tarball" in Unity). With -Push, also commits that folder as the
# only content of the upm branch and tags it upm/v<Version>, which is what users install:
#   https://github.com/vchelaru/SkiaGameRendering.git#upm  (or #upm/v<Version>)
# Run eng/build-unity-package.ps1 first.
param(
    [Parameter(Mandatory)][string]$Version,
    [switch]$Push
)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$package = Join-Path $repo 'unity/com.vchelaru.skiagamerendering'
$out = Join-Path $repo '.artifacts/unity'
$staging = Join-Path $out 'package'

if (-not (Test-Path (Join-Path $package 'Plugins/x86_64/libSkiaSharp.dll'))) {
    throw "Plugins/ is empty. Run eng/build-unity-package.ps1 first."
}

if (Test-Path $out) { Remove-Item -LiteralPath $out -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null
Get-ChildItem $package -Force | Where-Object { $_.Name -ne 'BuildMetas~' } |
    Copy-Item -Destination $staging -Recurse

$manifestPath = Join-Path $staging 'package.json'
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$manifest.version = $Version
$manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath

$tgz = Join-Path $out "com.vchelaru.skiagamerendering-$Version.tgz"
tar -czf $tgz -C $out package
if ($LASTEXITCODE -ne 0) { throw "tar failed." }
Write-Host "Packed $tgz"

if (-not $Push) { return }

$branch = 'upm'
$tag = "upm/v$Version"
$tree = Join-Path $out 'upm-branch'
git -C $repo fetch origin $branch 2>$null
if ($LASTEXITCODE -eq 0) {
    git -C $repo worktree add -B $branch $tree FETCH_HEAD
} else {
    git -C $repo worktree add --orphan -b $branch $tree
}
if ($LASTEXITCODE -ne 0) { throw "Could not check out the $branch branch." }

try {
    Get-ChildItem $tree -Force | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
    Get-ChildItem $staging -Force | Copy-Item -Destination $tree -Recurse
    git -C $tree add -A
    git -C $tree commit -m "com.vchelaru.skiagamerendering $Version"
    if ($LASTEXITCODE -ne 0) { throw "Commit failed." }
    git -C $tree tag $tag
    git -C $tree push origin $branch $tag
    if ($LASTEXITCODE -ne 0) { throw "Push failed." }
} finally {
    git -C $repo worktree remove --force $tree
}
