using System.Runtime.Versioning;
using System.Text.Json;
using Microsoft.JSInterop;

namespace Performance
{
    /// <summary>The page-side half shared by the Raw and Skia browser apps: machine details from the page, results back to it.</summary>
    [SupportedOSPlatform("browser")]
    public static class PerfPage
    {
        /// <summary>Reads the machine description from the page and routes the results to window.__perfResults. Returns the browser's name and the game's arguments.</summary>
        public static (string Browser, string[] Args) Configure(IJSRuntime js)
        {
            var runtime = (IJSInProcessRuntime)js;
            using var info = JsonDocument.Parse(runtime.Invoke<string>("perfLoop.info"));
            var root = info.RootElement;

            PerfEnvironment.Gpu = root.GetProperty("gpu").GetString();
            var machine = new MachineDescription(
                root.GetProperty("os").GetString()!,
                root.GetProperty("cpu").GetString()!,
                root.GetProperty("power").GetString()!,
                root.GetProperty("commit").GetString()!);
            PerfEnvironment.Machine = () => machine;
            PerfEnvironment.Screenshots = false;
            PerfEnvironment.Publish = (name, markdown) => runtime.InvokeVoid("perfLoop.publish", name, markdown);

            return (root.GetProperty("browser").GetString()!, runtime.Invoke<string[]>("perfLoop.args"));
        }
    }
}
