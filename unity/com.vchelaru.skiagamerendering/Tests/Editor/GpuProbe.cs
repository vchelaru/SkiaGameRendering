using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkiaGameRendering.Unity.Tests
{
    /// <summary>
    /// The numbers the leak tests watch, read the native way for Unity's graphics API: references to
    /// Unity's device (D3D11 COM references, or the MTLDevice's retain count on Metal) and the
    /// process's private memory.
    /// </summary>
    internal static class GpuProbe
    {
        internal static bool IsMetal => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;

        /// <summary>References held on Unity's device, reached through a throwaway texture.</summary>
        internal static int DeviceRefCount()
        {
            var texture = new RenderTexture(4, 4, 0);
            texture.Create();
            try
            {
                var nativeTexture = texture.GetNativeTexturePtr();
                if (IsMetal)
                    // -[MTLTexture device] doesn't retain, so the count is Unity's and anything else's.
                    return (int)SendNUInt(SendIntPtr(nativeTexture, Selector("device")), Selector("retainCount"));

                // ID3D11DeviceChild::GetDevice is vtable slot 3, and AddRefs the device, which the
                // Release below gives back.
                var vtable = Marshal.ReadIntPtr(nativeTexture);
                var getDevice = Marshal.GetDelegateForFunctionPointer<GetDeviceFn>(Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size));
                getDevice(nativeTexture, out var device);
                return Marshal.Release(device);
            }
            finally
            {
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        /// <summary>
        /// Unity's Mono reports Process.PrivateMemorySize64 as 0, so ask the OS directly: private
        /// usage on Windows, the physical footprint (what Activity Monitor shows as Memory) on macOS.
        /// </summary>
        internal static long PrivateBytes()
        {
            if (Application.platform == RuntimePlatform.OSXEditor)
            {
                // rusage_info_v0: a 16-byte uuid, then seven uint64 fields before ri_phys_footprint.
                var info = new byte[96];
                if (proc_pid_rusage(System.Diagnostics.Process.GetCurrentProcess().Id, 0, info) != 0)
                    throw new InvalidOperationException($"proc_pid_rusage failed: {Marshal.GetLastWin32Error()}");
                return (long)BitConverter.ToUInt64(info, 16 + 7 * 8);
            }

            var counters = new ProcessMemoryCountersEx { Size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx>() };
            if (!K32GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size))
                throw new InvalidOperationException($"GetProcessMemoryInfo failed: {Marshal.GetLastWin32Error()}");
            return (long)counters.PrivateUsage;
        }

        /// <summary>An Objective-C autorelease pool on Metal; nothing elsewhere.</summary>
        internal static IntPtr PushAutoreleasePool() => IsMetal ? objc_autoreleasePoolPush() : IntPtr.Zero;

        internal static void PopAutoreleasePool(IntPtr pool)
        {
            if (pool != IntPtr.Zero)
                objc_autoreleasePoolPop(pool);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void GetDeviceFn(IntPtr self, out IntPtr device);

        [StructLayout(LayoutKind.Sequential)]
        struct ProcessMemoryCountersEx
        {
            public uint Size;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage,
                QuotaPeakNonPagedPoolUsage, QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
        }

        [DllImport("kernel32.dll")]
        static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool K32GetProcessMemoryInfo(IntPtr process, ref ProcessMemoryCountersEx counters, uint size);

        [DllImport("/usr/lib/libproc.dylib", SetLastError = true)]
        static extern int proc_pid_rusage(int pid, int flavor, byte[] buffer);

        [DllImport("/usr/lib/libobjc.A.dylib")]
        static extern IntPtr objc_autoreleasePoolPush();

        [DllImport("/usr/lib/libobjc.A.dylib")]
        static extern void objc_autoreleasePoolPop(IntPtr pool);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
        static extern IntPtr Selector(string name);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        static extern IntPtr SendIntPtr(IntPtr receiver, IntPtr selector);

        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
        static extern UIntPtr SendNUInt(IntPtr receiver, IntPtr selector);
    }
}
