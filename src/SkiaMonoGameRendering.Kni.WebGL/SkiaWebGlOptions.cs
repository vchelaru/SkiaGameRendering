namespace SkiaMonoGameRendering.Kni.WebGL;

public enum WebGlUploadMode
{
    DirectCanvasTexSubImage2D,
    DiagnosticTexImage2D,
}

public sealed class SkiaWebGlOptions
{
    public WebGlUploadMode UploadMode { get; set; } = WebGlUploadMode.DirectCanvasTexSubImage2D;
    public bool RequireWebGl2 { get; set; } = true;
    public bool EnableDiagnostics { get; set; }
    public bool FlipY { get; set; }
    public bool PremultiplyAlpha { get; set; } = true;
    public bool DisableColorSpaceConversion { get; set; } = true;
    public int ResourceCacheBytes { get; set; } = 256 * 1024 * 1024;
}

public sealed class SkiaWebGlDiagnostics
{
    public string UploadPath { get; internal set; } = "texSubImage2D(canvas)";
    public double LastRenderCpuMilliseconds { get; internal set; }
    public double LastUploadCpuMilliseconds { get; internal set; }
    public double? LastUploadGpuMilliseconds { get; internal set; }
    public long UploadCount { get; internal set; }
    public long ResizeCount { get; internal set; }
    public long TargetCreationCount { get; internal set; }
    public long SurfaceRecreationCount { get; internal set; }
    public long ContextLossCount { get; internal set; }
    public long BytesUploaded { get; internal set; }
    public long DroppedFrameCount { get; internal set; }
}
