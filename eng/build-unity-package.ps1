# Fills unity/com.vchelaru.skiagamerendering/Plugins/ with the binaries the Unity package needs:
# Core.ANGLE's netstandard2.1 build, SkiaSharp's managed and native libraries, and ANGLE. Unity
# doesn't consume NuGet, so these are copied out of the NuGet cache instead. Windows x64 only.
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$plugins = Join-Path $repo 'unity/com.vchelaru.skiagamerendering/Plugins'
$native = Join-Path $plugins 'x86_64'
$angleProj = Join-Path $repo 'src/SkiaGameRendering.Core.ANGLE/SkiaGameRendering.Core.ANGLE.csproj'

[xml]$versions = Get-Content (Join-Path $repo 'eng/Versions.props')
$skiaVersion = $versions.Project.PropertyGroup.SkiaSharpVersion
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }

dotnet build $angleProj -c Release -f netstandard2.1
if ($LASTEXITCODE -ne 0) { throw "Core.ANGLE build failed." }

New-Item -ItemType Directory -Force $native | Out-Null

$angleOut = Join-Path $repo 'src/SkiaGameRendering.Core.ANGLE/bin/Release/netstandard2.1'
Copy-Item (Join-Path $angleOut 'SkiaGameRendering.Core.ANGLE.dll') $plugins
Copy-Item (Join-Path $nuget "skiasharp/$skiaVersion/lib/netstandard2.1/SkiaSharp.dll") $plugins
Copy-Item (Join-Path $nuget "skiasharp.nativeassets.win32/$skiaVersion/runtimes/win-x64/native/libSkiaSharp.dll") $native

$angleNative = Join-Path $repo 'src/SkiaGameRendering.Core.ANGLE/runtimes/win-x64/native'
foreach ($dll in 'libEGL.dll', 'libGLESv2.dll', 'z.dll') {
    Copy-Item (Join-Path $angleNative $dll) $native
}

Write-Host "Unity package binaries written to $plugins"
