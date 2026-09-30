using UnityEditor;

/// <summary>
/// Sets the import settings of every DLL under the package's <c>Plugins/</c> to Editor and Windows
/// x64 only, since the adapter only runs on Windows D3D11. Run it after adding a DLL to
/// <c>eng/build-unity-package.ps1</c>, then copy the resulting <c>.meta</c> files into the package's
/// <c>PluginMetas~/</c>, which the script restores next to the DLLs on every run:
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

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows64, true);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneWindows, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneLinux64, false);
            if (importer.isNativePlugin)
            {
                importer.SetEditorData("OS", "Windows");
                importer.SetEditorData("CPU", "x86_64");
                importer.SetPlatformData(BuildTarget.StandaloneWindows64, "CPU", "x86_64");
            }
            importer.SaveAndReimport();
        }
    }
}
