# Fills unity/com.vchelaru.skiagamerendering/Plugins/ with the binaries the Unity package needs:
# Core.ANGLE's, Core.Metal's, Core.D3D12's, Core.VK's and Core.OGL's netstandard2.1 builds, SkiaSharp's managed and native libraries,
# ANGLE (Windows x64), and the SkiaUnityD3D12 and SkiaUnityVulkan (Windows) and SkiaUnityMetal (macOS) plugins. Unity doesn't consume NuGet, so these
# are copied out of the NuGet cache instead.
# Unless -PackageOnly is passed, also fills samples/Sample.Unity/Assets/Plugins/Scene (see the end).
param([switch]$PackageOnly)
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$package = Join-Path $repo 'unity/com.vchelaru.skiagamerendering'
$plugins = Join-Path $package 'Plugins'
$native = Join-Path $plugins 'x86_64'
$macOS = Join-Path $plugins 'macOS'
$androidX64 = Join-Path $plugins 'Android/x86_64'
$androidArm64 = Join-Path $plugins 'Android/arm64-v8a'
$angleProj = Join-Path $repo 'src/SkiaGameRendering.Core.ANGLE/SkiaGameRendering.Core.ANGLE.csproj'
$metalProj = Join-Path $repo 'src/SkiaGameRendering.Core.Metal/SkiaGameRendering.Core.Metal.csproj'
$d3d12Proj = Join-Path $repo 'src/SkiaGameRendering.Core.D3D12/SkiaGameRendering.Core.D3D12.csproj'
$vkProj = Join-Path $repo 'src/SkiaGameRendering.Core.VK/SkiaGameRendering.Core.VK.csproj'
$oglProj = Join-Path $repo 'src/SkiaGameRendering.Core.OGL/SkiaGameRendering.Core.OGL.csproj'

[xml]$versions = Get-Content (Join-Path $repo 'eng/Versions.props')
$skiaVersion = $versions.Project.PropertyGroup.SkiaSharpVersion
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $HOME '.nuget/packages' }

dotnet build $angleProj -c Release -f netstandard2.1
if ($LASTEXITCODE -ne 0) { throw "Core.ANGLE build failed." }
dotnet build $metalProj -c Release -f netstandard2.1
if ($LASTEXITCODE -ne 0) { throw "Core.Metal build failed." }
dotnet build $d3d12Proj -c Release -f netstandard2.1
if ($LASTEXITCODE -ne 0) { throw "Core.D3D12 build failed." }
dotnet build $oglProj -c Release -f netstandard2.1
if ($LASTEXITCODE -ne 0) { throw "Core.OGL build failed." }
dotnet build $vkProj -c Release -f netstandard2.1
if ($LASTEXITCODE -ne 0) { throw "Core.VK build failed." }
# SkiaSharp's macOS and Android natives, which the main build never restores on Windows.
dotnet restore (Join-Path $repo 'eng/unity-natives/UnityNatives.csproj')
if ($LASTEXITCODE -ne 0) { throw "eng/unity-natives restore failed." }

New-Item -ItemType Directory -Force $native, $macOS, $androidX64, $androidArm64 | Out-Null

$angleOut = Join-Path $repo 'src/SkiaGameRendering.Core.ANGLE/bin/Release/netstandard2.1'
Copy-Item (Join-Path $angleOut 'SkiaGameRendering.Core.ANGLE.dll') $plugins
Copy-Item (Join-Path $repo 'src/SkiaGameRendering.Core.Metal/bin/Release/netstandard2.1/SkiaGameRendering.Core.Metal.dll') $plugins
Copy-Item (Join-Path $repo 'src/SkiaGameRendering.Core.D3D12/bin/Release/netstandard2.1/SkiaGameRendering.Core.D3D12.dll') $plugins
Copy-Item (Join-Path $repo 'src/SkiaGameRendering.Core.OGL/bin/Release/netstandard2.1/SkiaGameRendering.Core.OGL.dll') $plugins
Copy-Item (Join-Path $repo 'src/SkiaGameRendering.Core.VK/bin/Release/netstandard2.1/SkiaGameRendering.Core.VK.dll') $plugins
Copy-Item (Join-Path $nuget "skiasharp/$skiaVersion/lib/netstandard2.1/SkiaSharp.dll") $plugins
Copy-Item (Join-Path $nuget "skiasharp.nativeassets.win32/$skiaVersion/runtimes/win-x64/native/libSkiaSharp.dll") $native

# Built and committed by eng/build-unity-d3d12-plugin.ps1 and eng/build-unity-vulkan-plugin.ps1.
Copy-Item (Join-Path $repo 'unity/native/SkiaUnityD3D12/x86_64/SkiaUnityD3D12.dll') $native
Copy-Item (Join-Path $repo 'unity/native/SkiaUnityVulkan/x86_64/SkiaUnityVulkan.dll') $native

$angleNative = Join-Path $repo 'src/SkiaGameRendering.Core.ANGLE/runtimes/win-x64/native'
foreach ($dll in 'libEGL.dll', 'libGLESv2.dll', 'z.dll') {
    Copy-Item (Join-Path $angleNative $dll) $native
}

Copy-Item (Join-Path $nuget "skiasharp.nativeassets.macos/$skiaVersion/runtimes/osx/native/libSkiaSharp.dylib") $macOS
# Built and committed by eng/build-unity-metal-plugin.sh.
Copy-Item (Join-Path $repo 'unity/native/SkiaUnityMetal/macOS/libSkiaUnityMetal.dylib') $macOS

# Android needs no plugin of its own: Skia draws on an EGL context that shares Unity's.
Copy-Item (Join-Path $nuget "skiasharp.nativeassets.android/$skiaVersion/runtimes/android-x64/native/libSkiaSharp.so") $androidX64
Copy-Item (Join-Path $nuget "skiasharp.nativeassets.android/$skiaVersion/runtimes/android-arm64/native/libSkiaSharp.so") $androidArm64

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
$harfBuzzMac = $assets.libraries.PSObject.Properties.Name | Where-Object { $_ -like 'HarfBuzzSharp.NativeAssets.macOS/*' }
$sceneMac = Join-Path $scenePlugins 'macOS'
New-Item -ItemType Directory -Force $sceneMac | Out-Null
Copy-Item (Join-Path $nuget "$($harfBuzzMac.ToLower())/runtimes/osx/native/libHarfBuzzSharp.dylib") $sceneMac
# The Android HarfBuzz natives, of the version the macOS ones are (restored by eng/unity-natives), go where Unity
# picks up an app's own Android natives by ABI folder.
$harfBuzzAndroid = Join-Path $nuget "harfbuzzsharp.nativeassets.android/$(($harfBuzzMac -split '/')[1])/runtimes"
foreach ($abi in @(@('android-x64', 'x86_64'), @('android-arm64', 'arm64-v8a'))) {
    $dest = Join-Path (Split-Path $scenePlugins -Parent) "Android/libs/$($abi[1])"
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $harfBuzzAndroid "$($abi[0])/native/libHarfBuzzSharp.so") $dest
}

Write-Host "Unity sample scene binaries written to $scenePlugins"
