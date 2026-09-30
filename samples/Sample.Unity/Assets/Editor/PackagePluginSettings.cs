using UnityEditor;

/// <summary>
/// Sets the import settings of the Windows DLLs under the package's <c>Plugins/</c>: the managed DLLs
/// to the Editor and every player platform the package supports, and the natives in <c>x86_64/</c> to
/// Editor and Windows x64. The other platforms' natives keep their hand-written metas. Run it after
/// adding a Windows DLL to <c>eng/build-unity-package.ps1</c>, then copy the resulting <c>.meta</c>
/// files into the package's <c>BuildMetas~/</c>, which the script restores next to the DLLs on every run:
/// <c>Unity.exe -batchmode -quit -projectPath samples/Sample.Unity -executeMethod PackagePluginSettings.Apply</c>.
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
            bool windowsNative = importer.assetPath.StartsWith(PluginsPath + "x86_64/");
            if (importer.isNativePlugin && !windowsNative)
                continue;

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, !windowsNative);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, !windowsNative);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, !windowsNative);
            importer.SetCompatibleWithPlatform(BuildTarget.iOS, !windowsNative);
            if (windowsNative)
            {
                importer.SetEditorData("OS", "Windows");
                importer.SetEditorData("CPU", "x86_64");
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            }
            importer.SaveAndReimport();
        }
    }
}
