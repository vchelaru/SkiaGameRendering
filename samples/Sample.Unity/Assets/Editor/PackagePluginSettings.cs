using UnityEditor;

/// <summary>
/// Sets the import settings of every plugin under the package's <c>Plugins/</c>: the managed DLLs to
/// the Editor, Windows x64 and macOS, the natives in <c>x86_64/</c> to Windows x64, and the natives in
/// <c>macOS/</c> to macOS. Run it after adding a plugin to <c>eng/build-unity-package.ps1</c>, then
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

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, windows);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, macOS);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
            if (importer.isNativePlugin && windows)
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
