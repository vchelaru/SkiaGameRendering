<#
.SYNOPSIS
    Rebuilds ANGLE from source via vcpkg and refreshes the binaries checked into Core.ANGLE's
    runtimes/<rid>/native layout (issue #36).

.DESCRIPTION
    Core.ANGLE's NuGet package vendors ANGLE's libEGL.dll/libGLESv2.dll so every consumer's
    AngleEgl resolver hits its "runtimes/<rid>/native" tier instead of falling back to Edge
    WebView's private, non-redistributable copy.

    These binaries are built from ANGLE's own source via vcpkg's "angle" port, not extracted from
    a browser build. Chromium's own snapshot builds and Electron's current releases were both
    tried first; both produce libEGL.dll/libGLESv2.dll with zero exports (verified by PE
    export-table inspection - not even eglGetProcAddress), because modern Chromium links ANGLE
    privately into the browser process rather than shipping it as a standalone redistributable
    DLL. vcpkg's post-build policy checks would reject an export-less DLL, so its output is
    verified-real by construction.

    This is a real compile (~9 minutes per architecture) requiring the MSVC C++ toolchain
    (Microsoft.VisualStudio.Component.VC.Tools.x86.x64 for win-x64,
    Microsoft.VisualStudio.Component.VC.Tools.ARM64 for win-arm64), so unlike the old
    download-and-verify approach this is NOT run automatically at pack time. Run it by hand when
    updating ANGLE, then commit the refreshed DLLs and eng/angle-provenance.json together - the
    output (~12 MB total across both RIDs) is small enough to check in directly rather than
    re-derive on every build.

.PARAMETER VcpkgRoot
    Path to a vcpkg checkout (cloned and bootstrapped if it doesn't exist yet). Defaults to a
    "vcpkg" folder next to this script's parent (i.e. <repo>/../vcpkg), kept out of the repo itself
    since it's tooling, not source.

.EXAMPLE
    ./eng/vendor-angle.ps1
    Rebuilds both win-x64 and win-arm64, updates runtimes/<rid>/native, and rewrites
    eng/angle-provenance.json with the new vcpkg commit/version and file hashes.
#>
[CmdletBinding()]
param(
    [string]$VcpkgRoot
)

$ErrorActionPreference = "Stop"

$scriptDir = $PSScriptRoot
if ([string]::IsNullOrEmpty($scriptDir)) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}
$repoRoot = Resolve-Path (Join-Path $scriptDir "..")
$angleProjectDir = Join-Path $repoRoot "src\SkiaGameRendering.Core.ANGLE"
$provenancePath = Join-Path $scriptDir "angle-provenance.json"

if ([string]::IsNullOrEmpty($VcpkgRoot)) {
    $VcpkgRoot = Join-Path $repoRoot "..\vcpkg"
}

if (-not (Test-Path (Join-Path $VcpkgRoot "vcpkg.exe"))) {
    Write-Host "Cloning and bootstrapping vcpkg into $VcpkgRoot ..."
    git clone --depth 1 https://github.com/microsoft/vcpkg.git $VcpkgRoot
    & (Join-Path $VcpkgRoot "bootstrap-vcpkg.bat") -disableMetrics
}

$vcpkgExe = Join-Path $VcpkgRoot "vcpkg.exe"

# rid -> vcpkg triplet. Only these two are vendored - see AngleEgl.GetRuntimeIdentifier, which
# throws PlatformNotSupportedException for anything else rather than guessing.
$targets = @{
    "win-x64"   = "x64-windows"
    "win-arm64" = "arm64-windows"
}

foreach ($rid in $targets.Keys) {
    $triplet = $targets[$rid]
    Write-Host "[$rid] building angle:$triplet via vcpkg (this compiles from source, expect several minutes) ..."
    & $vcpkgExe install "angle:$triplet" --clean-after-build
    if ($LASTEXITCODE -ne 0) {
        throw "[$rid] vcpkg install angle:$triplet failed (exit $LASTEXITCODE)."
    }

    $destDir = Join-Path $angleProjectDir "runtimes\$rid\native"
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null

    foreach ($fileName in @("libEGL.dll", "libGLESv2.dll")) {
        $srcPath = Join-Path $VcpkgRoot "installed\$triplet\bin\$fileName"
        Copy-Item -Path $srcPath -Destination (Join-Path $destDir $fileName) -Force
    }
}

# Resolve the exact angle port version vcpkg just built, straight from its versions database, so
# the provenance record can't drift from what actually landed in runtimes/.
$vcpkgCommit = (git -C $VcpkgRoot rev-parse HEAD).Trim()
$angleVersions = Get-Content (Join-Path $VcpkgRoot "versions\a-\angle.json") -Raw | ConvertFrom-Json
$latestAngleVersion = $angleVersions.versions[0]

$runtimes = [ordered]@{}
foreach ($rid in $targets.Keys) {
    $triplet = $targets[$rid]
    $files = [ordered]@{}
    foreach ($fileName in @("libEGL.dll", "libGLESv2.dll")) {
        $hash = (Get-FileHash -Path (Join-Path $angleProjectDir "runtimes\$rid\native\$fileName") -Algorithm SHA256).Hash.ToLowerInvariant()
        $files[$fileName] = "sha256:$hash"
    }
    $runtimes[$rid] = [ordered]@{
        triplet = $triplet
        files   = $files
    }
}

$provenance = [ordered]@{
    _comment = "Pinned ANGLE binaries vendored into Core.ANGLE (issue #36), checked in directly under src/SkiaGameRendering.Core.ANGLE/runtimes/<rid>/native. Built from ANGLE's own source via vcpkg's 'angle' port, not extracted from a browser/embedder build - Chromium's own snapshot builds and Electron's current releases were tried first and both produce libEGL.dll/libGLESv2.dll with zero exports (verified by PE export-table inspection), because modern Chromium links ANGLE privately into the browser process instead of shipping it as a standalone redistributable DLL. vcpkg builds ANGLE as its own standalone target, and vcpkg's post-build policy checks would reject an export-less DLL, so a real export surface is structural, not incidental. See eng/vendor-angle.ps1 to reproduce or update this."
    vcpkg    = [ordered]@{
        commit            = $vcpkgCommit
        portVersion       = $latestAngleVersion.'version-string'
        portVersionNumber = $latestAngleVersion.'port-version'
    }
    runtimes = $runtimes
}

$provenance | ConvertTo-Json -Depth 10 | Set-Content -Path $provenancePath -Encoding utf8

Write-Host "ANGLE vendoring complete: $angleProjectDir\runtimes. Review and commit the updated DLLs plus $provenancePath together."
