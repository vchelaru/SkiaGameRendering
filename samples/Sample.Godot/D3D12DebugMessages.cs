using System.Runtime.InteropServices;
using Godot;

/// <summary>
/// Prints the errors the D3D12 debug layer has stored for Godot's device, for tests/Tests.Godot to
/// check. Godot's <c>--gpu-validation</c> turns the layer on but does not print its messages to
/// stdout or stderr, so the sample reads the layer's own <c>ID3D12InfoQueue</c> instead.
/// </summary>
static unsafe class D3D12DebugMessages
{
    /// <summary>The line prefix tests/Tests.Godot looks for.</summary>
    const string Prefix = "D3D12 debug layer";

    /// <summary>
    /// Skia asks every descriptor heap it creates for its GPU start handle, shader-visible or not;
    /// seen once per heap when the Skia context is created, and harmless.
    /// </summary>
    const int GetGpuDescriptorHandleForHeapStartInvalid = 1315;

    static readonly Guid IID_ID3D12InfoQueue = new("0742a90b-c387-483f-b946-30a7e4e61458");

    [StructLayout(LayoutKind.Sequential)]
    struct D3D12_MESSAGE
    {
        public int Category, Severity, ID;
        public byte* Description;
        public nuint DescriptionByteLength;
    }

    /// <summary>
    /// Prints each stored ERROR or CORRUPTION message, then "D3D12 debug layer: N error(s)", or
    /// "D3D12 debug layer: off" when the device has no info queue (no <c>--gpu-validation</c>).
    /// Does nothing on other drivers.
    /// </summary>
    public static void Print()
    {
        if (RenderingServer.GetCurrentRenderingDriverName() != "d3d12")
            return;
        var device = (IntPtr)RenderingServer.GetRenderingDevice().GetDriverResource(RenderingDevice.DriverResource.LogicalDevice, default, 0);

        // IUnknown::QueryInterface, slot 0.
        void* queue;
        var iid = IID_ID3D12InfoQueue;
        if (((delegate* unmanaged[MemberFunction]<IntPtr, Guid*, void**, int>)(*(void***)device)[0])(device, &iid, &queue) < 0)
        {
            GD.Print($"{Prefix}: off");
            return;
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
                    GD.Print($"{Prefix} error {message->ID}: {Marshal.PtrToStringUTF8((IntPtr)message->Description)}");
                }
                finally
                {
                    NativeMemory.Free(message);
                }
            }
            GD.Print($"{Prefix}: {errors} error(s)");
        }
        finally
        {
            // IUnknown::Release, slot 2.
            ((delegate* unmanaged[MemberFunction]<void*, uint>)(*(void***)queue)[2])(queue);
        }
    }
}
