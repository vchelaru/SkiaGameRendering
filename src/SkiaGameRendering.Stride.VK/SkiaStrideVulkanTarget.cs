using SkiaGameRendering.Core.VK;
using SkiaSharp;
using Stride.Graphics;

namespace SkiaGameRendering.Stride.VK
{
    /// <summary>
    /// Owns the GPU resources backing one <see cref="SkiaStrideVulkanRenderTarget2D"/>: the Stride
    /// <see cref="Texture"/>, the wrapped-image state, and the <see cref="SKSurface"/>/
    /// <see cref="GRBackendRenderTarget"/> wrapping it. Mirrors <c>SkiaGameRendering.Stride.D3D11</c>'s
    /// D3D11 <c>SkiaStrideTarget</c>, minus the ANGLE-specific bind/unbind step - Vulkan has no
    /// separate GL-style context to make current, so there is nothing to bind beyond the queue lock
    /// <see cref="SkiaStrideVulkanContext.BeginDraw"/>/<c>EndDraw</c> already handle.
    /// <para>
    /// <b>Linear-color-space compensation (repo issue: Sample.Stride.VK's crimson rectangle rendering
    /// pink).</b> Stride's <c>GraphicsDeviceManager</c> defaults <c>PreferredColorSpace</c> to
    /// <c>ColorSpace.Linear</c> (confirmed by decompiling Stride 4.4.0-beta5's actual
    /// <c>Stride.Games.dll</c>/<c>Stride.Graphics.dll</c>, not assumed), and under that default it
    /// converts the swapchain's back-buffer format to its sRGB variant at device-creation time
    /// (<c>GraphicsDeviceManager.ChangeOrCreateDevice</c>: <c>PreferredBackBufferFormat =
    /// PixelFormatExtensions.ToSRgb(...)</c> whenever <c>PreferredColorSpace == Linear</c>). An
    /// sRGB-formatted render target auto-encodes (gamma-encodes) whatever a shader writes to it, on
    /// the GPU, unconditionally - that is what makes it "sRGB". <see
    /// cref="SkiaStrideVulkanRenderTarget2D.End"/> composites this class's <see cref="Texture"/> via
    /// <c>SpriteBatch.Draw</c> straight onto whatever render target Stride's
    /// <c>GraphicsCompositor</c> currently has bound - which, for a renderer appended via
    /// <c>Game.AddSceneRenderer</c> (see <see cref="SkiaStrideVulkanSceneRenderer"/>), is the real,
    /// already-tonemapped, presented back buffer (Stride Community Toolkit's
    /// <c>AddSceneRenderer</c> appends as a sibling drawn after the existing top-level renderer, not
    /// into some earlier intermediate stage - checked against that library's actual source, not
    /// assumed). Stride's own <c>SpriteBatch</c>/<c>BatchBase</c> shader
    /// (<c>SpriteBatchShader.sdsl</c> on Stride's GitHub) does NOT compensate for this - its
    /// <c>ColorSpace</c>-keyed effect variant only linearizes the per-draw tint <c>Color4</c>
    /// (via <c>ColorUtility.ToLinear</c>), never the sampled texture color, so a <c>Draw(Texture,
    /// Vector2.Zero)</c> call with the default white tint gets zero compensation either way.
    /// </para>
    /// <para>
    /// Net effect without compensation: this texture holds ordinary, already gamma-encoded sRGB
    /// bytes (exactly what <see cref="SKPaint.Color"/> and friends always mean), Skia writes them
    /// raw, and the sRGB-formatted destination then gamma-ENCODES them a second time on write - a
    /// real double encode, not a Vulkan-specific corruption. Feeding <c>SKColors.Crimson</c>
    /// (220,20,60) through a second sRGB encode lands at ~(239,79,133), matching the reported "bright
    /// pink" almost exactly. The fix applied here: <see cref="SkiaStrideVulkanContext.CreateSurface"/>
    /// is given <c>SKColorSpace.CreateSrgbLinear()</c> whenever <c>graphicsDevice.ColorSpace ==
    /// ColorSpace.Linear</c>, so Skia itself linearizes (decodes) every color drawn into this
    /// (still plain, non-sRGB-formatted - see <see cref="ToPixelFormat"/>) texture before storing
    /// its bytes. The GPU's later auto-encode-on-write into the real sRGB back buffer then exactly
    /// cancels that decode (encode and decode are inverse functions by construction), reproducing
    /// the original byte values. Empirically verified end-to-end through the real
    /// <c>VkSkiaSurfaceFactory</c>/lavapipe path in <c>tests/Tests.Core.VK</c> before landing here:
    /// wrapping a plain (non-sRGB) <c>VkImage</c> with an <c>SKColorSpace.CreateSrgbLinear()</c>-
    /// tagged surface and clearing it to crimson reads back (182,2,12) - exactly the sRGB decode of
    /// (220,20,60) - proving Skia performs the linearization purely in software, with no change to
    /// the underlying image format (also confirmed the other direction: actually creating the
    /// VkImage itself with the <c>_SRGB</c> pixel-format variant makes
    /// <c>SKSurface.Create</c> fail outright for Skia's Vulkan backend, even with a matching
    /// <c>SKColorSpace</c> attached - not a viable alternative fix here).
    /// </para>
    /// <para>
    /// Deliberately keyed off <c>graphicsDevice.ColorSpace</c> rather than hardcoded: a consumer that
    /// sets <c>PreferredColorSpace = ColorSpace.Gamma</c> gets a plain, non-sRGB back buffer with no
    /// hardware auto-encode, so compensating would introduce the exact same bug in the opposite
    /// direction (an uncompensated decode, with nothing to cancel it back out).
    /// </para>
    /// </summary>
    internal sealed class SkiaStrideVulkanTarget : IDisposable
    {
        readonly SkiaStrideVulkanContext _context;
        Texture? _texture;
        SKSurface? _surface;
        GRBackendRenderTarget? _renderTarget;
        VkTextureState? _renderState;
        bool _disposed;

        internal SkiaStrideVulkanTarget(
            SkiaStrideVulkanContext context, GraphicsDevice graphicsDevice, int width, int height, SKColorType colorType)
        {
            _context = context;

            // TextureFlags.RenderTarget sets VkImageUsageFlags.ColorAttachment (see
            // SkiaStrideVulkanContext.ComputeImageUsageFlags), which Skia's wrapped surface needs.
            // Stride.Graphics.Texture.New2D allocates the underlying VkImage eagerly, same as the
            // D3D11 path - no lazy-allocation workaround needed before reflecting the image out.
            _texture = Texture.New2D(
                graphicsDevice, width, height, ToPixelFormat(colorType),
                TextureFlags.RenderTarget | TextureFlags.ShaderResource);

            _context.BeginDraw();
            try
            {
                _renderState = _context.CreateTextureState(_texture);

                // See this class's doc comment: compensates for Stride's own hardware sRGB
                // encode-on-write under its default Linear color-space pipeline. Only applies when
                // that pipeline is actually active - a Gamma-pipeline consumer's render target has no
                // such auto-encode to compensate for.
                var skiaColorSpace = graphicsDevice.ColorSpace == ColorSpace.Linear
                    ? SKColorSpace.CreateSrgbLinear()
                    : null;
                var result = _context.CreateSurface(_renderState, width, height, colorType, skiaColorSpace);
                _surface = result.surface;
                _renderTarget = result.renderTarget;
            }
            catch
            {
                _context.EndDraw();
                _texture.Dispose();
                throw;
            }
            _context.EndDraw();
        }

        internal Texture Texture => _texture ?? throw new ObjectDisposedException(nameof(SkiaStrideVulkanTarget));

        internal SKSurface Surface => _surface ?? throw new ObjectDisposedException(nameof(SkiaStrideVulkanTarget));

        /// <summary>Mirrors <c>SkiaStrideTarget.ToPixelFormat</c> (the D3D11 adapter's SKColorType -> Stride PixelFormat map) - graphics-API-agnostic on the Stride side, so the mapping itself is identical.</summary>
        static PixelFormat ToPixelFormat(SKColorType colorType) => colorType switch
        {
            SKColorType.Rgba1010102 => PixelFormat.R10G10B10A2_UNorm,
            SKColorType.Rgba16161616 => PixelFormat.R16G16B16A16_UNorm,
            SKColorType.Alpha8 => PixelFormat.A8_UNorm,
            SKColorType.Bgra8888 => PixelFormat.B8G8R8A8_UNorm,
            _ => PixelFormat.R8G8B8A8_UNorm,
        };

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            _context.BeginDraw();
            try
            {
                _surface?.Dispose();
                _surface = null;
                _renderTarget?.Dispose();
                _renderTarget = null;
                // VkTextureState (unlike ANGLE's AngleTextureState) owns no unmanaged resource of its
                // own - the wrapped VkImage is host-owned and released below when _texture disposes,
                // so there is nothing further to release here beyond dropping the reference.
                _renderState = null;
            }
            finally
            {
                _context.EndDraw();
            }

            _texture?.Dispose();
            _texture = null;
        }
    }
}
