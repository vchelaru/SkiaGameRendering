using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Batch-mode entry point that builds the sample as a player for the machine it runs on, Windows x64
/// (Direct3D 11, Direct3D 12 with -d3d12, Vulkan with -vulkan, or OpenGL with -gl) or macOS (Metal), or,
/// with -android, an Android arm64 IL2CPP player (OpenGL ES 3; Unity 6000.6 no longer builds x86_64):
/// <c>Unity -batchmode -quit -projectPath samples/Sample.Unity -executeMethod SampleBuild.Build [-il2cpp] [-linear] [-d3d12] [-vulkan] [-gl] [-android] [-srp urp|hdrp]</c>.
/// Output goes to Build/Mono or Build/IL2CPP, with a -D3D12, -Vulkan, -GL and/or -Linear suffix for those builds:
/// <c>Sample.Unity.exe</c> on Windows, <c>Sample.Unity.app</c> on macOS, Build/Android/Sample.Unity.apk.
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
        bool vulkan = Array.IndexOf(args, "-vulkan") >= 0;
        bool gl = Array.IndexOf(args, "-gl") >= 0;
        bool android = Array.IndexOf(args, "-android") >= 0;
        bool mac = Application.platform == RuntimePlatform.OSXEditor;
        var buildTarget = android ? BuildTarget.Android : mac ? BuildTarget.StandaloneOSX : BuildTarget.StandaloneWindows64;
        var target = android ? NamedBuildTarget.Android : NamedBuildTarget.Standalone;
        if (android)
        {
            // Android builds need IL2CPP, and Unity 6000.6 marks AndroidArchitecture.X86_64 as no longer supported.
            // The Android player settings below only take effect once Android is the active platform.
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            PlayerSettings.SetUseDefaultGraphicsAPIs(buildTarget, false);
            PlayerSettings.SetGraphicsAPIs(buildTarget, new[] { GraphicsDeviceType.OpenGLES3 });
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.colorSpace = linear ? ColorSpace.Linear : ColorSpace.Gamma;
            // The sample's copies are laid out for a window wider than tall.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            BuildAndroid(linear);
            return;
        }

        // Pin the one API the adapter supports on each platform rather than rely on Unity's default list.
        PlayerSettings.SetUseDefaultGraphicsAPIs(buildTarget, false);
        PlayerSettings.SetGraphicsAPIs(buildTarget, new[] { mac ? GraphicsDeviceType.Metal : d3d12 ? GraphicsDeviceType.Direct3D12 : vulkan ? GraphicsDeviceType.Vulkan : gl ? GraphicsDeviceType.OpenGLCore : GraphicsDeviceType.Direct3D11 });
        PlayerSettings.SetScriptingBackend(target, il2cpp ? ScriptingImplementation.IL2CPP : ScriptingImplementation.Mono2x);
        PlayerSettings.colorSpace = linear ? ColorSpace.Linear : ColorSpace.Gamma;
        var srpIndex = Array.IndexOf(args, "-srp");
        var srp = srpIndex >= 0 && srpIndex + 1 < args.Length ? args[srpIndex + 1].ToLowerInvariant() : null;
        if (srp != null)
            AssignRenderPipeline(srp);

        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects), ScenePath);
        }

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = $"Build/{(il2cpp ? "IL2CPP" : "Mono")}{(d3d12 ? "-D3D12" : "")}{(vulkan ? "-Vulkan" : "")}{(gl ? "-GL" : "")}{(srp != null ? "-" + srp.ToUpperInvariant() : "")}{(linear ? "-Linear" : "")}/Sample.Unity.{(mac ? "app" : "exe")}",
            target = buildTarget,
            options = BuildOptions.None,
        });

        CheckResult(report);
    }

    // Creates a render pipeline asset (reflection, so this file compiles without the package) under
    // Assets/SrpTemp and makes it the pipeline for every quality level. eng/build-unity-srp.ps1 adds the
    // package to the manifest beforehand and reverts the manifest, ProjectSettings and Assets/SrpTemp after.
    static void AssignRenderPipeline(string srp)
    {
        const string Dir = "Assets/SrpTemp";
        Directory.CreateDirectory(Dir);
        RenderPipelineAsset asset;
        if (srp == "urp")
        {
            var assetType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset, Unity.RenderPipelines.Universal.Runtime")
                ?? throw new Exception("com.unity.render-pipelines.universal is not in Packages/manifest.json.");
            var rendererType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRendererData, Unity.RenderPipelines.Universal.Runtime");
            var renderer = ScriptableObject.CreateInstance(rendererType);
            AssetDatabase.CreateAsset(renderer, Dir + "/Renderer.asset");
            asset = (RenderPipelineAsset)assetType.GetMethod("Create", new[] { rendererType.BaseType }).Invoke(null, new object[] { renderer });
        }
        else if (srp == "hdrp")
        {
            var assetType = Type.GetType("UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset, Unity.RenderPipelines.HighDefinition.Runtime")
                ?? throw new Exception("com.unity.render-pipelines.high-definition is not in Packages/manifest.json.");
            asset = (RenderPipelineAsset)ScriptableObject.CreateInstance(assetType);
            // HDRP multisamples only in Forward lit shader mode, and compiles the MSAA shader variants only when the asset allows a
            // sample count; SkiaSample turns the count on or off at run time, and turns it on per camera.
            var settingsProperty = assetType.GetProperty("currentPlatformRenderPipelineSettings");
            var settings = settingsProperty.GetValue(asset);
            var lit = settings.GetType().GetField("supportedLitShaderMode");
            lit.SetValue(settings, Enum.Parse(lit.FieldType, "ForwardOnly"));
            var samples = settings.GetType().GetField("msaaSampleCount");
            samples.SetValue(settings, Enum.Parse(samples.FieldType, "MSAA4x"));
            settingsProperty.SetValue(asset, settings);
        }
        else
            throw new Exception("-srp takes urp or hdrp.");

        AssetDatabase.CreateAsset(asset, Dir + "/Pipeline.asset");
        GraphicsSettings.defaultRenderPipeline = asset;
        var level = QualitySettings.GetQualityLevel();
        for (var i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
        QualitySettings.SetQualityLevel(level, false);
        AssetDatabase.SaveAssets();
    }

    static void BuildAndroid(bool linear)
    {
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = $"Build/Android{(linear ? "-Linear" : "")}/Sample.Unity.apk",
            target = BuildTarget.Android,
            options = BuildOptions.None,
        });
        CheckResult(report);
    }

    static void CheckResult(UnityEditor.Build.Reporting.BuildReport report)
    {
        if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            throw new Exception($"Build {report.summary.result}: {report.summary.totalErrors} error(s).");
    }
}
