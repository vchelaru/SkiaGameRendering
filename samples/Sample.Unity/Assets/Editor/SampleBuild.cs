using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

/// <summary>
/// Batch-mode entry point that builds the sample as a Windows x64 player:
/// <c>Unity.exe -batchmode -quit -projectPath samples/Sample.Unity -executeMethod SampleBuild.Build [-il2cpp]</c>.
/// Output goes to Build/Mono or Build/IL2CPP.
/// </summary>
public static class SampleBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";

    public static void Build()
    {
        bool il2cpp = Array.IndexOf(Environment.GetCommandLineArgs(), "-il2cpp") >= 0;
        var target = NamedBuildTarget.Standalone;

        // The adapter only supports D3D11 so far, so pin it rather than rely on Unity's default list.
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
        PlayerSettings.SetScriptingBackend(target, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);

        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects), ScenePath);
        }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = $"Build/{(il2cpp ? "IL2CPP" : "Mono")}/Sample.Unity.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });

        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception($"Build {report.summary.result}: {report.summary.totalErrors} error(s).");
    }
}
