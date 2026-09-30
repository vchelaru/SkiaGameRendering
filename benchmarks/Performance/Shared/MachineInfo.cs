using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Performance
{
    /// <summary>
    /// Hardware and build details recorded with each result, so rows from different machines can be
    /// compared. Power matters on laptops: running on battery can throttle the GPU substantially.
    /// </summary>
    public static class MachineInfo
    {
        public static string Os => RuntimeInformation.OSDescription;

        // The .NET SDK appends the git commit to the informational version ("1.0.0+<sha>").
        public static string Commit
        {
            get
            {
                string? version = Assembly.GetEntryAssembly()?
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                int plus = version?.IndexOf('+') ?? -1;
                return plus >= 0 ? version![(plus + 1)..][..Math.Min(7, version.Length - plus - 1)] : "unknown";
            }
        }

        public static string Cpu
        {
            get
            {
                if (OperatingSystem.IsWindows())
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                    if (key?.GetValue("ProcessorNameString") is string name)
                        return $"{name.Trim()} ({Environment.ProcessorCount} threads)";
                }
                return $"{RuntimeInformation.ProcessArchitecture} ({Environment.ProcessorCount} threads)";
            }
        }

        public static string Power
        {
            get
            {
                if (OperatingSystem.IsWindows() && GetSystemPowerStatus(out var status))
                {
                    return status.ACLineStatus switch
                    {
                        0 => "battery",
                        1 => "plugged in",
                        _ => "unknown",
                    };
                }
                return "unknown";
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SystemPowerStatus
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);
    }
}
