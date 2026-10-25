# Builds samples/Sample.Unity as a URP or HDRP player, Build/Mono[-D3D12|-Vulkan|-GL]-URP|-HDRP[-Linear] (Sample.Unity.app on macOS, Metal).
# The committed project stays built-in RP and Gamma: this adds the pipeline package to a temporary
# Packages/manifest.json, has SampleBuild assign a pipeline asset (-srp), then restores the manifest,
# ProjectSettings and removes Assets/SrpTemp. Run eng/build-unity-package.ps1 first.
#   eng/build-unity-srp.ps1 -Pipeline urp -Linear
#   Build/Mono-URP-Linear/Sample.Unity.exe --smoke-test
# Extra SampleBuild flags (-d3d12, -vulkan, -gl, -il2cpp) go in -Extra.
param(
    [Parameter(Mandatory)][ValidateSet('urp', 'hdrp')][string]$Pipeline,
    [switch]$Linear,
    [string[]]$Extra = @()
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'samples/Sample.Unity'
$version = ((Get-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1) -split ' ')[1]
$unity = if ($env:UNITY_EXE) { $env:UNITY_EXE }
elseif ($IsMacOS) { "/Applications/Unity/Hub/Editor/$version/Unity.app/Contents/MacOS/Unity" }
else { "C:/Program Files/Unity/Hub/Editor/$version/Editor/Unity.exe" }
$builtInPackages = if ($IsMacOS) { Join-Path (Split-Path (Split-Path $unity)) 'Resources/PackageManager/BuiltInPackages' } else { Join-Path (Split-Path $unity) 'Data/Resources/PackageManager/BuiltInPackages' }
$packageId = if ($Pipeline -eq 'urp') { 'com.unity.render-pipelines.universal' } else { 'com.unity.render-pipelines.high-definition' }
# The editor ships both packages, so the manifest entry resolves offline.
$packageVersion = (Get-Content (Join-Path $builtInPackages "$packageId/package.json") -Raw | ConvertFrom-Json).version

$backup = Join-Path ([IO.Path]::GetTempPath()) "sgr-srp-$([guid]::NewGuid().ToString('N').Substring(0, 8))"
New-Item -ItemType Directory $backup | Out-Null
Copy-Item (Join-Path $project 'Packages') (Join-Path $backup 'Packages') -Recurse
Copy-Item (Join-Path $project 'ProjectSettings') (Join-Path $backup 'ProjectSettings') -Recurse
$assetsBefore = Get-ChildItem (Join-Path $project 'Assets') -Recurse -Force | ForEach-Object FullName
try {
    $manifestPath = Join-Path $project 'Packages/manifest.json'
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    $manifest.dependencies | Add-Member -NotePropertyName $packageId -NotePropertyValue $packageVersion -Force
    $manifest | ConvertTo-Json -Depth 10 | Set-Content $manifestPath

    $log = Join-Path $project 'Logs/srp-build.log'
    $flags = @('-batchmode', '-quit', '-projectPath', $project, '-logFile', $log, '-executeMethod', 'SampleBuild.Build', '-srp', $Pipeline) + $(if ($Linear) { '-linear' }) + $Extra
    $p = Start-Process $unity $flags -Wait -PassThru
    if ($p.ExitCode -ne 0) {
        Get-Content $log -Tail 60
        throw "Unity exited with $($p.ExitCode)."
    }
}
finally {
    foreach ($dir in 'Packages', 'ProjectSettings') {
        Remove-Item (Join-Path $project $dir) -Recurse -Force
        Copy-Item (Join-Path $backup $dir) (Join-Path $project $dir) -Recurse
    }
    foreach ($f in Get-ChildItem (Join-Path $project 'Assets') -Recurse -Force | Where-Object { $_.FullName -notin $assetsBefore } | Sort-Object { $_.FullName.Length } -Descending) { Remove-Item $f.FullName -Recurse -Force -ErrorAction SilentlyContinue }
    Remove-Item $backup -Recurse -Force -ErrorAction SilentlyContinue
}
