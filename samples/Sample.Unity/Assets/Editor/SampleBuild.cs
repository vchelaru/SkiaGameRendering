using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Batch-mode entry point that builds the sample as a Windows x64 player:
/// <c>Unity.exe -batchmode -quit -projectPath samples/Sample.Unity -executeMethod SampleBuild.Build [-il2cpp] [-linear]</c>.
/// Output goes to Build/Mono or Build/IL2CPP, with a -Linear suffix for a Linear color space build.
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
        var target = NamedBuildTarget.Standalone;

        // The adapter only supports D3D11 so far, so pin it rather than rely on Unity's default list.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
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
            locationPathName = $"Build/{(il2cpp ? "IL2CPP" : "Mono")}{(linear ? "-Linear" : "")}/Sample.Unity.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception($"Build {report.summary.result}: {report.summary.totalErrors} error(s).");
    }
}
