using System.Runtime.InteropServices;

namespace Benchmarks.DirectDraw;

/// <summary>
/// GPU time per frame from two GL_TIMESTAMP queries on the engine's context, one before the frame's first
/// draw and one after its last. GL_TIME_ELAPSED cannot be used: it only counts work on the context that
/// began it, and Skia draws on its own context. A timestamp is GPU time, so the span covers Skia's work too,
/// as long as the GPU runs the two contexts' flushed work in order (it does on one queue). Results are read
/// a few frames late so reading them never stalls the pipeline.
/// </summary>
sealed unsafe class GpuTimestamps
{
    const int GL_TIMESTAMP = 0x8E28;
    const int GL_QUERY_RESULT = 0x8866;
    const int Latency = 4;

    [DllImport("SDL2", CallingConvention = CallingConvention.Cdecl)]
    static extern IntPtr SDL_GL_GetProcAddress([MarshalAs(UnmanagedType.LPStr)] string proc);

    readonly delegate* unmanaged<int, uint*, void> _genQueries;
    readonly delegate* unmanaged<uint, int, void> _queryCounter;
    readonly delegate* unmanaged<uint, int, ulong*, void> _getQueryObjectui64v;
    readonly uint[] _queries = new uint[Latency * 2];

    internal GpuTimestamps()
    {
        _genQueries = (delegate* unmanaged<int, uint*, void>)Load("glGenQueries");
        _queryCounter = (delegate* unmanaged<uint, int, void>)Load("glQueryCounter");
        _getQueryObjectui64v = (delegate* unmanaged<uint, int, ulong*, void>)Load("glGetQueryObjectui64v");
        fixed (uint* queries = _queries)
            _genQueries(_queries.Length, queries);
    }

    /// <summary>GL_RENDERER of the current context, which says which GPU a hybrid laptop picked.</summary>
    internal static string Renderer()
    {
        var getString = (delegate* unmanaged<int, IntPtr>)Load("glGetString");
        return Marshal.PtrToStringAnsi(getString(0x1F01 /* GL_RENDERER */)) ?? "unknown";
    }

    static IntPtr Load(string name)
    {
        var address = SDL_GL_GetProcAddress(name);
        return address != IntPtr.Zero ? address : throw new EntryPointNotFoundException(name);
    }

    internal void Begin(int frame) => _queryCounter(_queries[frame % Latency * 2], GL_TIMESTAMP);

    internal void End(int frame) => _queryCounter(_queries[frame % Latency * 2 + 1], GL_TIMESTAMP);

    /// <summary>The GPU milliseconds of the frame <see cref="Latency"/> frames back, or null for the first few.</summary>
    internal double? ReadLagged(int frame)
    {
        var lagged = frame - (Latency - 1);
        if (lagged < 0)
            return null;

        ulong begin, end;
        _getQueryObjectui64v(_queries[lagged % Latency * 2], GL_QUERY_RESULT, &begin);
        _getQueryObjectui64v(_queries[lagged % Latency * 2 + 1], GL_QUERY_RESULT, &end);
        return (end - begin) / 1_000_000.0;
    }
}
