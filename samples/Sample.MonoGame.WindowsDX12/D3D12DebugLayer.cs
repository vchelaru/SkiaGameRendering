using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;

namespace Sample;

/// <summary>
/// Turns on the D3D12 debug layer before MonoGame creates its device, and afterward reads the errors
/// the layer stored in the device's <c>ID3D12InfoQueue</c> (the layer only writes to a debugger's
/// output otherwise). WARP renders correct pixels with wrong resource states, so this is the check
/// that catches a broken state handoff in <c>SkiaDx12Backend</c>. Opt in with SKIAGAMERENDERING_D3D12_DEBUG=1.
/// </summary>
static unsafe class D3D12DebugLayer
{
    /// <summary>
    /// Skia asks every descriptor heap it creates for its GPU start handle, shader-visible or not;
    /// seen once per heap when the Skia context is created, and harmless.
    /// </summary>
    const int GetGpuDescriptorHandleForHeapStartInvalid = 1315;

    static readonly Guid IID_ID3D12Debug = new("344488b7-6846-474b-b989-f027448245e0");
    static readonly Guid IID_ID3D12InfoQueue = new("0742a90b-c387-483f-b946-30a7e4e61458");

    [StructLayout(LayoutKind.Sequential)]
    struct D3D12_MESSAGE
    {
        public int Category, Severity, ID;
        public byte* Description;
        public nuint DescriptionByteLength;
    }

    [DllImport("d3d12.dll")]
    static extern int D3D12GetDebugInterface(Guid* riid, void** debug);

    static long? DIAG_firstError;

    public static bool Requested => Environment.GetEnvironmentVariable("SKIAGAMERENDERING_D3D12_DEBUG") == "1";

    /// <summary>Must run before the <c>GraphicsDevice</c> exists.</summary>
    public static void Enable()
    {
        void* debug;
        var iid = IID_ID3D12Debug;
        Marshal.ThrowExceptionForHR(D3D12GetDebugInterface(&iid, &debug));
        // ID3D12Debug::EnableDebugLayer, slot 3, then IUnknown::Release, slot 2.
        ((delegate* unmanaged[MemberFunction]<void*, void>)(*(void***)debug)[3])(debug);
        ((delegate* unmanaged[MemberFunction]<void*, uint>)(*(void***)debug)[2])(debug);
    }

    /// <summary>
    /// Prints each stored ERROR or CORRUPTION message; true when there were none and the layer was on.
    /// </summary>
    public static bool Passed(GraphicsDevice graphicsDevice)
    {
        var device = graphicsDevice.GetNativeHandles().LogicalDevice;

        // IUnknown::QueryInterface, slot 0.
        void* queue;
        var iid = IID_ID3D12InfoQueue;
        if (((delegate* unmanaged[MemberFunction]<IntPtr, Guid*, void**, int>)(*(void***)device)[0])(device, &iid, &queue) < 0)
        {
            Console.WriteLine("D3D12 debug layer FAILED: the device has no info queue, so the layer is not on.");
            return false;
        }

        try
        {
            var vtable = *(void***)queue;
            // ID3D12InfoQueue::GetNumStoredMessages (slot 8) and GetMessage (slot 5).
            var count = ((delegate* unmanaged[MemberFunction]<void*, ulong>)vtable[8])(queue);
            var getMessage = (delegate* unmanaged[MemberFunction]<void*, ulong, D3D12_MESSAGE*, nuint*, int>)vtable[5];
            int errors = 0;
            for (ulong i = 0; i < count; i++)
            {
                nuint length = 0;
                getMessage(queue, i, null, &length);
                var message = (D3D12_MESSAGE*)NativeMemory.Alloc(length);
                try
                {
                    // Severity 0 is CORRUPTION, 1 is ERROR.
                    if (getMessage(queue, i, message, &length) < 0 || message->Severity > 1 || message->ID == GetGpuDescriptorHandleForHeapStartInvalid)
                        continue;
                    errors++;
                    Console.WriteLine($"D3D12 debug layer error #{i} {message->ID}: {Marshal.PtrToStringUTF8((IntPtr)message->Description)}");
                    DIAG_firstError ??= (long)i;
                }
                finally
                {
                    NativeMemory.Free(message);
                }
            }
            if (DIAG_firstError is { } first)
                for (var j = Math.Max(0, first - 4); j < Math.Min((long)count, first + 4); j++)
                {
                    nuint len = 0;
                    getMessage(queue, (ulong)j, null, &len);
                    var m = (D3D12_MESSAGE*)NativeMemory.Alloc(len);
                    getMessage(queue, (ulong)j, m, &len);
                    var text = Marshal.PtrToStringUTF8((IntPtr)m->Description) ?? "";
                    Console.WriteLine($"  DIAG #{j} sev{m->Severity} id{m->ID}: {text[..Math.Min(260, text.Length)]}");
                    NativeMemory.Free(m);
                }
            Console.WriteLine($"D3D12 debug layer: {errors} error(s) of {count} message(s)");
            return errors == 0;
        }
        finally
        {
            // IUnknown::Release, slot 2.
            ((delegate* unmanaged[MemberFunction]<void*, uint>)(*(void***)queue)[2])(queue);
        }
    }
}
