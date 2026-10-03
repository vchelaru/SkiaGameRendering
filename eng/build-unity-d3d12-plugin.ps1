# Builds the Unity package's Windows D3D12 plugin (unity/native/SkiaUnityD3D12) as an x64 DLL.
# Needs Visual Studio or its Build Tools with the C++ workload. The output is committed, so only
# rerun this after changing the source; eng/build-unity-package.ps1 copies it into the package.
$ErrorActionPreference = 'Stop'

$repo = Split-Path $PSScriptRoot -Parent
$src = Join-Path $repo 'unity/native/SkiaUnityD3D12'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "No Visual Studio with the C++ tools found." }

$out = Join-Path $src 'x86_64'
New-Item -ItemType Directory -Force $out | Out-Null
$vcvars = Join-Path $vs 'VC/Auxiliary/Build/vcvars64.bat'
# /MT links the C runtime in, so the DLL needs no redistributable next to it.
cmd /c "`"$vcvars`" >nul && cd /d `"$src`" && cl /nologo /LD /O2 /MT /W4 /WX SkiaUnityD3D12.cpp /Fe:x86_64\SkiaUnityD3D12.dll /Fo:x86_64\ /link d3d12.lib dxgi.lib"
if ($LASTEXITCODE -ne 0) { throw "SkiaUnityD3D12 build failed." }
Remove-Item (Join-Path $out '*.obj'), (Join-Path $out '*.lib'), (Join-Path $out '*.exp') -ErrorAction SilentlyContinue
Write-Host "Wrote $out/SkiaUnityD3D12.dll"
