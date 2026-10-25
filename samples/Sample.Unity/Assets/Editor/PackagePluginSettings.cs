using UnityEditor;

/// <summary>
/// Sets the import settings of every plugin under the package's <c>Plugins/</c>: the managed DLLs to
/// the Editor, Windows x64, macOS and Android, the natives in <c>x86_64/</c> to Windows x64, the natives in
/// <c>macOS/</c> to macOS, and those in <c>Android/x86_64/</c> and <c>Android/arm64-v8a/</c> to Android. Run it after adding a plugin to <c>eng/build-unity-package.ps1</c>, then
/// copy the resulting <c>.meta</c> files into the package's <c>BuildMetas~/</c>, which the script
/// restores next to the plugins on every run:
/// <c>Unity -batchmode -quit -projectPath samples/Sample.Unity -executeMethod PackagePluginSettings.Apply</c>.
/// </summary>
public static class PackagePluginSettings
{
    const string PluginsPath = "Packages/com.vchelaru.skiagamerendering/Plugins/";

    public static void Apply()
    {
        foreach (var importer in PluginImporter.GetAllImporters())
        {
            if (!importer.assetPath.StartsWith(PluginsPath))
                continue;

            bool windows = !importer.isNativePlugin || importer.assetPath.StartsWith(PluginsPath + "x86_64/");
            bool macOS = !importer.isNativePlugin || importer.assetPath.StartsWith(PluginsPath + "macOS/");
            bool androidX64 = importer.assetPath.StartsWith(PluginsPath + "Android/x86_64/");
            bool androidArm64 = importer.assetPath.StartsWith(PluginsPath + "Android/arm64-v8a/");
            bool android = !importer.isNativePlugin || androidX64 || androidArm64;

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, windows);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, macOS);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, android);
            if (androidX64 || androidArm64)
                importer.SetPlatformData(BuildTarget.Android, "CPU", androidX64 ? "X86_64" : "ARM64");
            if (androidX64 || androidArm64)
            {
                // Neither the Editor nor a desktop player loads these.
                importer.SetCompatibleWithEditor(false);
            }
            else if (importer.isNativePlugin && windows)
            {
                importer.SetEditorData("OS", "Windows");
                importer.SetEditorData("CPU", "x86_64");
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            }
            else if (importer.isNativePlugin && macOS)
            {
                // Universal binaries, x86_64 and arm64.
                importer.SetEditorData("OS", "OSX");
                importer.SetEditorData("CPU", "AnyCPU");
                importer.SetPlatformData(BuildTarget.StandaloneOSX, "CPU", "AnyCPU");
            }
            importer.SaveAndReimport();
        }
    }
}
