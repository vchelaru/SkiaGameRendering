using SkiaGameRendering.Core.OGL;
using static SkiaGameRendering.Core.OGL.GlConstants;

namespace Tests.CoreOgl;

/// <summary>
/// An <see cref="IGlFunctionLoader"/> that hands back delegates which record the call instead of
/// entering a driver, so <see cref="GlSkiaSurfaceFactory"/>'s GL call sequence can be driven with no
/// GL context, no window and no GPU.
///
/// Arguments are recorded numerically rather than by enum name: several <c>GlConstants</c> members
/// alias one value (Renderbuffer / RenderbufferExt, and so on), so an enum's <c>ToString</c> does not
/// identify which member the caller actually passed.
/// </summary>
internal sealed class RecordingGlFunctionLoader : IGlFunctionLoader
{
    public const int GeneratedRenderbufferId = 11;
    public const int GeneratedFramebufferId = 22;
    public const int SampleCount = 4;

    /// <summary>Every GL call made through this loader, in order, as <see cref="Call"/> strings.</summary>
    public List<string> Calls { get; } = new();

    /// <summary>The framebuffer the last glBindFramebuffer left bound. 0 is the default framebuffer.</summary>
    public int BoundFramebuffer { get; private set; }

    /// <summary>What glCheckFramebufferStatus reports. Set before the call to test the failure path.</summary>
    public FramebufferErrorCode Status { get; set; } = FramebufferErrorCode.FramebufferComplete;

    public static string Call(string name, params int[] args) =>
        $"{name}({string.Join(", ", args.Select(FormatArgument))})";

    public unsafe T Load<T>(string nativeName) where T : Delegate
    {
        switch (nativeName)
        {
            case "glGetIntegerv":
                return (T)(Delegate)new GlFunctions.GetIntegerDelegate((int param, int* data) =>
                {
                    Record("glGetIntegerv", param);
                    *data = SampleCount;
                });

            case "glGenRenderbuffers":
                return (T)(Delegate)new GlFunctions.GenRenderbuffersDelegate((int count, out int buffer) =>
                {
                    Record("glGenRenderbuffers", count);
                    buffer = GeneratedRenderbufferId;
                });

            case "glBindRenderbuffer":
                return (T)(Delegate)new GlFunctions.BindRenderbufferDelegate((target, buffer) =>
                    Record("glBindRenderbuffer", (int)target, buffer));

            case "glDeleteRenderbuffers":
                return (T)(Delegate)new GlFunctions.DeleteRenderbuffersDelegate((int count, ref int buffer) =>
                    Record("glDeleteRenderbuffers", count, buffer));

            case "glRenderbufferStorage":
                return (T)(Delegate)new GlFunctions.RenderbufferStorageDelegate((target, storage, width, height) =>
                    Record("glRenderbufferStorage", (int)target, (int)storage, width, height));

            case "glGenFramebuffers":
                return (T)(Delegate)new GlFunctions.GenFramebuffersDelegate((int count, out int buffer) =>
                {
                    Record("glGenFramebuffers", count);
                    buffer = GeneratedFramebufferId;
                });

            case "glBindFramebuffer":
                return (T)(Delegate)new GlFunctions.BindFramebufferDelegate((target, buffer) =>
                {
                    Record("glBindFramebuffer", (int)target, buffer);
                    BoundFramebuffer = buffer;
                });

            case "glDeleteFramebuffers":
                return (T)(Delegate)new GlFunctions.DeleteFramebuffersDelegate((int count, ref int buffer) =>
                    Record("glDeleteFramebuffers", count, buffer));

            case "glInvalidateFramebuffer":
                return (T)(Delegate)new GlFunctions.InvalidateFramebufferDelegate((target, numAttachments, attachments) =>
                {
                    var args = new List<int> { (int)target, numAttachments };
                    args.AddRange(attachments.Select(a => (int)a));
                    Record("glInvalidateFramebuffer", args.ToArray());
                });

            case "glFramebufferTexture2D":
                return (T)(Delegate)new GlFunctions.FramebufferTexture2DDelegate(
                    (target, attachment, textureTarget, texture, level) =>
                        Record("glFramebufferTexture2D", (int)target, (int)attachment, (int)textureTarget, texture, level));

            case "glFramebufferRenderbuffer":
                return (T)(Delegate)new GlFunctions.FramebufferRenderbufferDelegate(
                    (target, attachment, renderbufferTarget, buffer) =>
                        Record("glFramebufferRenderbuffer", (int)target, (int)attachment, (int)renderbufferTarget, buffer));

            case "glCheckFramebufferStatus":
                return (T)(Delegate)new GlFunctions.CheckFramebufferStatusDelegate(target =>
                {
                    Record("glCheckFramebufferStatus", (int)target);
                    return Status;
                });

            default:
                throw new NotSupportedException(
                    $"{nameof(RecordingGlFunctionLoader)} has no fake for {nativeName}. " +
                    "GlFunctions.Load asked for an entry point this loader does not know about.");
        }
    }

    private void Record(string name, params int[] args) => Calls.Add(Call(name, args));

    private static string FormatArgument(int value) =>
        value >= 0x100 ? "0x" + value.ToString("X4") : value.ToString();
}
