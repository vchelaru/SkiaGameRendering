using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Batch-mode entry point that builds the sample as a player for the machine it runs on, Windows x64
/// (Direct3D 11, or Direct3D 12 with -d3d12) or macOS (Metal):
/// <c>Unity -batchmode -quit -projectPath samples/Sample.Unity -executeMethod SampleBuild.Build [-il2cpp] [-linear] [-d3d12]</c>.
/// Output goes to Build/Mono or Build/IL2CPP, with a -D3D12 and/or -Linear suffix for those builds:
/// <c>Sample.Unity.exe</c> on Windows, <c>Sample.Unity.app</c> on macOS.
/// Unity saves the color space into ProjectSettings.asset, which is committed in Gamma, so revert it after a -linear build.
/// </summary>
public static class SampleBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    public static void Build()
    {
        var args = Environment.GetCommandLineArgs();
        bool il2cpp = Array.IndexOf(args, "-il2cpp") >= 0;
        bool linear = Array.IndexOf(args, "-linear") >= 0;
        bool d3d12 = Array.IndexOf(args, "-d3d12") >= 0;
        bool mac = Application.platform == RuntimePlatform.OSXEditor;
        var buildTarget = mac ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneWindows64;
        var target = NamedBuildTarget.Standalone;

        // Pin the one API the adapter supports on each platform rather than rely on Unity's default list.
        PlayerSettings.SetUseDefaultGraphicsAPIs(buildTarget, false);
        PlayerSettings.SetGraphicsAPIs(buildTarget, new[] { mac ? GraphicsDeviceType.Metal : d3d12 ? GraphicsDeviceType.Direct3D12 : GraphicsDeviceType.Direct3D11 });
        PlayerSettings.SetScriptingBackend(target, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        PlayerSettings.colorSpace = linear ? ColorSpace.Linear : ColorSpace.Gamma;

        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects), ScenePath);
        }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = $"Build/{(il2cpp ? "IL2CPP" : "Mono")}{(d3d12 ? "-D3D12" : "")}{(linear ? "-Linear" : "")}/Sample.Unity.{(mac ? "app" : "exe")}",
            target = buildTarget,
            options = BuildOptions.None,
        });

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception($"Build {report.summary.result}: {report.summary.totalErrors} error(s).");
    }
}
