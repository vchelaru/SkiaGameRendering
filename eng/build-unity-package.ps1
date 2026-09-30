# Fills unity/com.vchelaru.skiagamerendering/Plugins/ with the binaries the Unity package needs:
# Core.ANGLE's netstandard2.1 build, SkiaSharp's managed and native libraries, and ANGLE. Unity
# doesn't consume NuGet, so these are copied out of the NuGet cache instead. Windows x64 only.
# Unless -PackageOnly is passed, also fills samples/Sample.Unity/Assets/Plugins/Scene (see the end).
param([switch]$PackageOnly)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$package = Join-Path $repo 'unity/com.vchelaru.skiagamerendering'
$plugins = Join-Path $package 'Plugins'
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

# A package installed from git is read-only, so Unity can't write .meta files for it and ignores any
# file without one. BuildMetas~ (hidden from Unity by its trailing ~) holds the committed .meta for
# every file this script adds, with the plugin import settings from
# samples/Sample.Unity/Assets/Editor/PackagePluginSettings.cs.
Copy-Item (Join-Path $package 'BuildMetas~/*') $package -Recurse -Force
Copy-Item (Join-Path $repo 'LICENSE.md') $package

# The binaries carry their own licenses, which redistribution has to include.
$notices = Join-Path $package 'ThirdPartyNotices~'
New-Item -ItemType Directory -Force $notices | Out-Null
Copy-Item (Join-Path $repo 'src/SkiaGameRendering.Core.ANGLE/ANGLE-LICENSE.txt') $notices
Copy-Item (Join-Path $nuget "skiasharp/$skiaVersion/LICENSE.txt") (Join-Path $notices 'SkiaSharp-LICENSE.txt')
Copy-Item (Join-Path $nuget "skiasharp.nativeassets.win32/$skiaVersion/THIRD-PARTY-NOTICES.txt") (Join-Path $notices 'SkiaSharp-THIRD-PARTY-NOTICES.txt')

Write-Host "Unity package binaries written to $plugins"
if ($PackageOnly) { return }

# The sample's shared Scene, prebuilt with Svg.Skia and its dependencies into Assets/Plugins/Scene.
# SkiaSharp.dll comes from the package above, and Unity already ships
# System.Runtime.CompilerServices.Unsafe, so a second copy of either would clash.
$sceneProj = Join-Path $repo 'samples/Sample.Unity/Scene/Sample.Unity.Scene.csproj'
$scenePlugins = Join-Path $repo 'samples/Sample.Unity/Assets/Plugins/Scene'
$sceneNative = Join-Path $scenePlugins 'x86_64'

dotnet build $sceneProj -c Release
if ($LASTEXITCODE -ne 0) { throw "Sample.Unity.Scene build failed." }

New-Item -ItemType Directory -Force $sceneNative | Out-Null
$sceneOut = Join-Path $repo 'samples/Sample.Unity/Scene/bin/Release/netstandard2.1'
Get-ChildItem $sceneOut -Filter *.dll |
    Where-Object { $_.Name -notin 'SkiaSharp.dll', 'System.Runtime.CompilerServices.Unsafe.dll' } |
    Copy-Item -Destination $scenePlugins

# Svg.Skia's text support pulls in HarfBuzzSharp, whose native library NuGet would normally supply.
$assets = Get-Content (Join-Path $repo 'samples/Sample.Unity/Scene/obj/project.assets.json') -Raw | ConvertFrom-Json
$harfBuzz = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'HarfBuzzSharp.NativeAssets.Win32/*' }
Copy-Item (Join-Path $nuget "$($harfBuzz.ToLower())/runtimes/win-x64/native/libHarfBuzzSharp.dll") $sceneNative

Write-Host "Unity sample scene binaries written to $scenePlugins"
