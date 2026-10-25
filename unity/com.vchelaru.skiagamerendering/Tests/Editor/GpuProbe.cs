using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace SkiaGameRendering.Tests
{
    /// <summary>
    /// The numbers the leak tests watch, read the native way for Unity's graphics API: references to
    /// Unity's device (D3D11 COM references, or the MTLDevice's retain count on Metal) and the
    /// process's private memory. OpenGL has no device object, so <see cref="FrameLeakTests"/> counts the
    /// live GL objects there instead (see <see cref="GlObjectCount"/>). Unity's own play mode adds GL
    /// objects on every cycle, so <see cref="DomainReloadTests"/> does not use that count.
    /// </summary>
    internal static class GpuProbe
    {
        internal static bool IsMetal => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal;
        internal static bool IsOpenGl => SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore;
        internal static bool IsVulkan => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Vulkan;
        internal static bool IsD3D12 => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Direct3D12;

        /// <summary>References held on Unity's device, reached through a throwaway texture. Zero on OpenGL and Vulkan, which have no COM device to count.</summary>
        internal static int DeviceRefCount()
        {
            if (IsOpenGl || IsVulkan)
                return 0;
            var texture = new RenderTexture(4, 4, 0);
            texture.Create();
            try
            {
                var nativeTexture = texture.GetNativeTexturePtr();
                if (IsMetal)
                    // -[MTLTexture device] doesn't retain, so the count is Unity's and anything else's.
                    return (int)SendNUInt(SendIntPtr(nativeTexture, Selector("device")), Selector("retainCount"));

                if (IsD3D12)
                {
                    // ID3D12DeviceChild::GetDevice(riid, ppvDevice) is vtable slot 7 (IUnknown 0-2, ID3D12Object 3-6), and AddRefs too.
                    var vtable12 = Marshal.ReadIntPtr(nativeTexture);
                    var getDevice12 = Marshal.GetDelegateForFunctionPointer<GetDevice12Fn>(Marshal.ReadIntPtr(vtable12, 7 * IntPtr.Size));
                    var iid = new Guid("189819f1-1db6-4b57-be54-1821339b85f7");
                    getDevice12(nativeTexture, ref iid, out var device12);
                    return Marshal.Release(device12);
                }

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

        // Names are handed out lowest-free-first, so the live ones sit far below this.
        const int MaxGlName = 1 << 17;

        /// <summary>
        /// The live textures, renderbuffers and framebuffers on Windows OpenGL, counted by asking the GL
        /// driver whether each name is an object (<c>glIs*</c>) on the Skia context, which shares Unity's
        /// textures and renderbuffers and owns the framebuffers Skia draws through. Reaches that context
        /// by reflection, and uses it from this thread, so call it only while no draw is queued.
        /// Zero before the backend exists, and off Windows.
        /// </summary>
        internal static int GlObjectCount()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                return 0;
            const BindingFlags nonPublic = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var backend = typeof(SkiaRenderTarget2D).Assembly
                .GetType("SkiaGameRendering.SkiaUnityRenderThread", throwOnError: true)
                .GetField("_backend", nonPublic).GetValue(null);
            if (backend == null)
                return 0;
            // A rename of the backend's context field fails the test with a null reference, not a pass.
            var platform = backend.GetType().GetField("_platform", nonPublic).GetValue(backend);
            var type = platform.GetType();
            type.GetMethod("MakeSkiaContextCurrent").Invoke(platform, null);
            try
            {
                var getProc = type.GetMethod("GetProcAddress", new[] { typeof(string) });
                IsObjectFn Load(string name) => Marshal.GetDelegateForFunctionPointer<IsObjectFn>(
                    (IntPtr)getProc.Invoke(platform, new object[] { name }));
                var functions = new[] { Load("glIsTexture"), Load("glIsRenderbuffer"), Load("glIsFramebuffer") };
                int count = 0;
                foreach (var isObject in functions)
                    for (uint name = 1; name <= MaxGlName; name++)
                        if (isObject(name) != 0)
                            count++;
                return count;
            }
            finally
            {
                type.GetMethod("RestoreHostContext").Invoke(platform, null);
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
        delegate byte IsObjectFn(uint name);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate void GetDeviceFn(IntPtr self, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        delegate int GetDevice12Fn(IntPtr self, ref Guid riid, out IntPtr device);

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
