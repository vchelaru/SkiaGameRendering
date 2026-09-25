using Godot;
using Godot.Collections;
using SkiaSharp;

namespace SkiaGameRendering.Godot
{
    /// <summary>
    /// What the Vulkan and D3D12 backends share: creating the RD texture Godot will
    /// sample, priming it, and the <see cref="Texture2Drd"/> that exposes it to the scene tree. What
    /// differs per graphics API - how Skia gets at the device and the texture, and how the texture is
    /// handed back to Godot after a draw - is left to <see cref="VulkanGodotBackend"/> and
    /// <see cref="D3D12GodotBackend"/> through <see cref="CreateGpuResources"/>.
    ///
    /// MAINTENANCE NOTES (read from Godot 4.7.2's source, not assumed):
    /// <list type="bullet">
    /// <item>
    /// <b>Godot tracks each texture's layout/state itself.</b> Its <c>RenderingDeviceGraph</c> derives
    /// a texture's Vulkan layout (or, with D3D12 enhanced barriers, its <c>D3D12_BARRIER_LAYOUT</c>)
    /// from the last usage it recorded and only emits a barrier when that usage changes; the D3D12
    /// driver without enhanced barriers keeps legacy per-subresource states the same way. Either
    /// way a texture Godot merely samples is transitioned once and then assumed to stay put. So
    /// <see cref="PrimeForSampling"/> makes that one transition happen - by really sampling the texture
    /// in a fragment shader, then flushing and stalling the graph - before Skia ever touches it, and
    /// each backend returns the texture to exactly that state after every draw. Sampling from a
    /// fragment shader specifically, rather than compute, is what the D3D12 driver's legacy path
    /// narrows to <c>PIXEL_SHADER_RESOURCE</c>, the state Godot's 2D canvas wants later, so the
    /// tracked state never moves again.
    /// </item>
    /// <item>
    /// <b>Metal is not this package.</b> Godot exposes its <c>MTLDevice</c>/<c>MTLCommandQueue</c> the
    /// same way, but this repo has no Metal Core library yet; <see cref="SkiaGodotRenderer.Initialize"/>
    /// fails with a message naming the setting.
    /// </item>
    /// </list>
    /// </summary>
    internal abstract class RenderingDeviceGodotBackend : SkiaGodotBackend
    {
        /// <summary>
        /// Full-screen triangle from <c>gl_VertexIndex</c> plus a fragment that samples the texture -
        /// see the priming discussion in this class's doc comment. Writing the sampled value to the
        /// color output keeps the compiler from optimizing the sampler binding away (Godot's
        /// uniform-set creation would then reject a binding the shader no longer declares).
        /// </summary>
        const string PrimeVertexSource = """
            #version 450
            void main() {
                vec2 corner = vec2((gl_VertexIndex << 1) & 2, gl_VertexIndex & 2);
                gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
            }
            """;

        const string PrimeFragmentSource = """
            #version 450
            layout(set = 0, binding = 0) uniform sampler2D skia_texture;
            layout(location = 0) out vec4 color;
            void main() { color = textureLod(skia_texture, vec2(0.5), 0.0); }
            """;

        RenderingDevice _renderingDevice = null!;
        Rid _primeShader;
        Rid _primePipeline;
        Rid _primeSampler;
        Rid _primeScratchTexture;
        Rid _primeFramebuffer;

        internal RenderingDevice RenderingDevice => _renderingDevice;

        /// <summary>
        /// Whether Skia draws straight into the RD texture (Vulkan) or into a resource of its own
        /// that is copied into the RD texture per frame (D3D12). Decides the texture's usage bits.
        /// </summary>
        internal abstract bool RendersIntoGodotTexture { get; }

        internal override bool IsZeroCopy => RendersIntoGodotTexture;

        internal override void Initialize(RenderingDevice? explicitDevice)
        {
            var global = RenderingServer.GetRenderingDevice()
                ?? throw new InvalidOperationException("RenderingServer.GetRenderingDevice() returned null on a RenderingDevice driver.");
            // A local device's textures cannot back a Texture2DRD, which is how every target reaches
            // the scene tree, so a local device would give targets that never display.
            if (explicitDevice != null && explicitDevice != global)
                throw new NotSupportedException(
                    "SkiaGameRendering.Godot renders only on Godot's global RenderingDevice (RenderingServer.GetRenderingDevice()); " +
                    "textures on a local RenderingDevice cannot be shown in the scene tree.");
            _renderingDevice = global;
            InitializeCore();
        }

        protected abstract void InitializeCore();

        /// <summary>Creates the per-target GPU resources for an RD texture that has already been primed.</summary>
        internal abstract RenderingDeviceGpuResources CreateGpuResources(Rid texture, int width, int height, SKColorType colorType);

        protected abstract void DisposeCore();

        protected override SkiaGodotTargetResources CreateTargetCore(int width, int height, SKColorType colorType) =>
            new TargetResources(this, width, height, colorType);

        /// <summary>
        /// Creates the RD texture Godot will sample. <c>CanCopyFromBit</c>/<c>CanCopyToBit</c> map to the
        /// Vulkan transfer usage Skia insists on for a wrapped image (and let the D3D12 backend and
        /// tests copy into/out of it); <c>ColorAttachmentBit</c> only when Skia renders into the
        /// texture itself.
        /// <para>
        /// <paramref name="srgbFormat"/> matters even though nothing here ever samples the texture as
        /// sRGB: <c>RenderingServer.texture_rd_create</c> (what <see cref="Texture2Drd"/> calls) builds
        /// a second, sRGB-format shared view of any RD texture whose format has an sRGB twin, and on
        /// Vulkan a <c>vkCreateImageView</c> with a different format is only legal on an image created
        /// with <c>VK_IMAGE_CREATE_MUTABLE_FORMAT_BIT</c> - which Godot sets exactly when
        /// <see cref="RDTextureFormat"/> lists shareable formats. Without it the validation layer
        /// reports <c>VUID-VkImageViewCreateInfo-image-01762</c>.
        /// </para>
        /// </summary>
        Rid CreateTexture(int width, int height, RenderingDevice.DataFormat format, RenderingDevice.DataFormat? srgbFormat)
        {
            var usage =
                RenderingDevice.TextureUsageBits.SamplingBit |
                RenderingDevice.TextureUsageBits.CanCopyFromBit |
                RenderingDevice.TextureUsageBits.CanCopyToBit;
            if (RendersIntoGodotTexture)
                usage |= RenderingDevice.TextureUsageBits.ColorAttachmentBit;

            var textureFormat = new RDTextureFormat
            {
                Format = format,
                Width = (uint)width,
                Height = (uint)height,
                Depth = 1,
                ArrayLayers = 1,
                Mipmaps = 1,
                TextureType = RenderingDevice.TextureType.Type2D,
                Samples = RenderingDevice.TextureSamples.Samples1,
                UsageBits = usage,
            };
            textureFormat.AddShareableFormat(format);
            if (srgbFormat is { } srgb)
                textureFormat.AddShareableFormat(srgb);

            var rid = _renderingDevice.TextureCreate(textureFormat, new RDTextureView());
            if (!rid.IsValid)
                throw new InvalidOperationException($"RenderingDevice.TextureCreate failed for a {width}x{height} {format} texture.");
            return rid;
        }

        /// <summary>
        /// Makes Godot record <paramref name="texture"/> as a fragment-sampled texture, and execute that
        /// transition, before Skia touches it - see this class's doc comment. On return the texture
        /// really is in the sampled layout/state, Godot believes exactly that, and it will never move
        /// it again on its own as long as it is only sampled.
        /// <para>
        /// The flush is <see cref="RenderingDevice.TextureGetData"/> on the priming pass's own 1x1
        /// color attachment: Godot implements it as "record the copy, flush and stall for all
        /// frames, read back", which executes everything recorded before it - the draw included - and
        /// waits. Done on that scratch texture rather than <paramref name="texture"/> itself so the
        /// latter's recorded usage stays "sampled" instead of ending on "copied from".
        /// </para>
        /// </summary>
        void PrimeForSampling(Rid texture)
        {
            EnsurePrimeResources();

            var textureUniform = new RDUniform { UniformType = RenderingDevice.UniformType.SamplerWithTexture, Binding = 0 };
            textureUniform.AddId(_primeSampler);
            textureUniform.AddId(texture);

            var uniformSet = _renderingDevice.UniformSetCreate([textureUniform], _primeShader, 0);
            if (!uniformSet.IsValid)
                throw new InvalidOperationException("RenderingDevice.UniformSetCreate failed while priming the Skia texture for sampling.");
            try
            {
                var drawList = _renderingDevice.DrawListBegin(_primeFramebuffer);
                _renderingDevice.DrawListBindRenderPipeline(drawList, _primePipeline);
                _renderingDevice.DrawListBindUniformSet(drawList, uniformSet, 0);
                _renderingDevice.DrawListDraw(drawList, false, 1, 3);
                _renderingDevice.DrawListEnd();

                _renderingDevice.TextureGetData(_primeScratchTexture, 0);
            }
            finally
            {
                _renderingDevice.FreeRid(uniformSet);
            }
        }

        void EnsurePrimeResources()
        {
            if (_primePipeline.IsValid)
                return;

            var source = new RDShaderSource
            {
                Language = RenderingDevice.ShaderLanguage.Glsl,
                SourceVertex = PrimeVertexSource,
                SourceFragment = PrimeFragmentSource,
            };
            var spirv = _renderingDevice.ShaderCompileSpirVFromSource(source, allowCache: false);
            var compileError = spirv.CompileErrorVertex + spirv.CompileErrorFragment;
            if (!string.IsNullOrEmpty(compileError))
                throw new InvalidOperationException("Failed to compile the texture-priming shader: " + compileError);

            _primeShader = _renderingDevice.ShaderCreateFromSpirV(spirv, "SkiaGameRendering.Godot prime");
            if (!_primeShader.IsValid)
                throw new InvalidOperationException("RenderingDevice.ShaderCreateFromSpirV failed for the texture-priming shader.");

            var scratchFormat = new RDTextureFormat
            {
                Format = RenderingDevice.DataFormat.R8G8B8A8Unorm,
                Width = 1,
                Height = 1,
                Depth = 1,
                ArrayLayers = 1,
                Mipmaps = 1,
                TextureType = RenderingDevice.TextureType.Type2D,
                Samples = RenderingDevice.TextureSamples.Samples1,
                UsageBits = RenderingDevice.TextureUsageBits.ColorAttachmentBit | RenderingDevice.TextureUsageBits.CanCopyFromBit,
            };
            _primeScratchTexture = _renderingDevice.TextureCreate(scratchFormat, new RDTextureView());
            _primeFramebuffer = _renderingDevice.FramebufferCreate([_primeScratchTexture]);
            _primeSampler = _renderingDevice.SamplerCreate(new RDSamplerState());
            if (!_primeScratchTexture.IsValid || !_primeFramebuffer.IsValid || !_primeSampler.IsValid)
                throw new InvalidOperationException("RenderingDevice failed to create the texture-priming helper resources.");

            var blend = new RDPipelineColorBlendState
            {
                Attachments = new Array<RDPipelineColorBlendStateAttachment> { new() },
            };
            _primePipeline = _renderingDevice.RenderPipelineCreate(
                _primeShader,
                _renderingDevice.FramebufferGetFormat(_primeFramebuffer),
                RenderingDevice.InvalidFormatId,
                RenderingDevice.RenderPrimitive.Triangles,
                new RDPipelineRasterizationState(),
                new RDPipelineMultisampleState(),
                new RDPipelineDepthStencilState(),
                blend);
            if (!_primePipeline.IsValid)
                throw new InvalidOperationException("RenderingDevice.RenderPipelineCreate failed for the texture-priming shader.");
        }

        public override void Dispose()
        {
            DisposeCore();

            if (_renderingDevice != null)
            {
                FreeIfValid(ref _primePipeline);
                FreeIfValid(ref _primeFramebuffer);
                FreeIfValid(ref _primeScratchTexture);
                FreeIfValid(ref _primeShader);
                FreeIfValid(ref _primeSampler);
            }
        }

        void FreeIfValid(ref Rid rid)
        {
            if (rid.IsValid)
                _renderingDevice.FreeRid(rid);
            rid = default;
        }

        /// <summary>
        /// SKColorType to <see cref="RenderingDevice.DataFormat"/>. On Vulkan the VkFormat Skia is told
        /// comes back from Godot itself (<see cref="RenderingDevice.DriverResource.TextureDataFormat"/>);
        /// on D3D12 the backend picks the typed twin of the same family.
        /// </summary>
        static RenderingDevice.DataFormat ToDataFormat(SKColorType colorType) => colorType switch
        {
            SKColorType.Rgba8888 => RenderingDevice.DataFormat.R8G8B8A8Unorm,
            SKColorType.Bgra8888 => RenderingDevice.DataFormat.B8G8R8A8Unorm,
            SKColorType.Rgba1010102 => RenderingDevice.DataFormat.A2B10G10R10UnormPack32,
            SKColorType.Rgba16161616 => RenderingDevice.DataFormat.R16G16B16A16Unorm,
            _ => throw new NotSupportedException(
                $"SkiaGameRendering.Godot does not support SKColorType.{colorType}. Supported: Rgba8888, Bgra8888, Rgba1010102, " +
                "Rgba16161616 (Rgba8888 only on the Compatibility renderer)."),
        };

        /// <summary>The sRGB twin of <see cref="ToDataFormat"/>'s result, or <c>null</c> when the format has none. See <see cref="CreateTexture"/> for why Godot needs it declared.</summary>
        static RenderingDevice.DataFormat? ToSrgbDataFormat(SKColorType colorType) => colorType switch
        {
            SKColorType.Bgra8888 => RenderingDevice.DataFormat.B8G8R8A8Srgb,
            SKColorType.Rgba1010102 => null,
            SKColorType.Rgba16161616 => null,
            _ => RenderingDevice.DataFormat.R8G8B8A8Srgb,
        };

        /// <summary>
        /// Owns what one <see cref="SkiaGodotRenderTarget2D"/> holds on an RD backend: the RD texture,
        /// the <see cref="Texture2Drd"/> that exposes it to the scene tree, and the backend's per-target
        /// GPU resources.
        /// <para>
        /// <b>Alpha.</b> A GPU-backed Skia surface is always premultiplied; Godot's default CanvasItem
        /// blend mode (Mix) expects straight alpha. Opaque content is unaffected; anti-aliased edges
        /// over a transparent clear come out slightly dark unless the displaying node uses
        /// <see cref="CanvasItemMaterial.BlendModeEnum.PremultAlpha"/> -
        /// <see cref="SkiaGodotRenderTarget2D.CreatePremultipliedAlphaMaterial"/> hands one back.
        /// </para>
        /// <para>
        /// <b>Color space.</b> The RD texture is a plain UNORM format and the Skia surface carries no
        /// color-space tag: Godot's 2D pipeline with the default <c>rendering/viewport/hdr_2d = false</c>
        /// samples textures in gamma space, so Skia's sRGB-encoded bytes display 1:1, with none of the
        /// linear-pipeline compensation the Stride adapter needs. HDR 2D projects would need the same
        /// <c>SKColorSpace.CreateSrgbLinear()</c> treatment; not wired up.
        /// </para>
        /// </summary>
        sealed class TargetResources : SkiaGodotTargetResources
        {
            readonly RenderingDeviceGodotBackend _backend;
            Rid _textureRid;
            Texture2Drd? _texture;
            RenderingDeviceGpuResources? _gpu;
            bool _disposed;

            internal TargetResources(RenderingDeviceGodotBackend backend, int width, int height, SKColorType colorType)
            {
                _backend = backend;

                _textureRid = backend.CreateTexture(width, height, ToDataFormat(colorType), ToSrgbDataFormat(colorType));
                try
                {
                    // From here on the texture is in the sampled layout/state, Godot knows it, and
                    // Godot will never transition it again on its own - see PrimeForSampling.
                    backend.PrimeForSampling(_textureRid);

                    _gpu = backend.CreateGpuResources(_textureRid, width, height, colorType);

                    // Texture2Drd is the engine's own "show an RD texture in the scene tree" resource:
                    // RenderingServer.texture_rd_create builds a shared VIEW of our RD texture (no
                    // copy), so whatever ends up in the RD texture is what Sprite2D/TextureRect display.
                    _texture = new Texture2Drd { TextureRdRid = _textureRid };
                }
                catch
                {
                    _gpu?.Dispose();
                    _gpu = null;
                    backend.RenderingDevice.FreeRid(_textureRid);
                    _textureRid = default;
                    throw;
                }
            }

            internal override Texture2D Texture => _texture ?? throw new ObjectDisposedException(nameof(SkiaGodotRenderTarget2D));

            internal override Rid TextureRid => _disposed ? throw new ObjectDisposedException(nameof(SkiaGodotRenderTarget2D)) : _textureRid;

            internal override SKSurface BeginFrame()
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _gpu!.BeginFrame();
            }

            internal override void EndFrame() => _gpu!.EndFrame();

            /// <summary>
            /// Clearing <c>TextureRdRid</c> emits the resource's <c>changed</c> signal and any CanvasItem
            /// showing it calls <c>queue_redraw</c>, which Godot only allows from the node's own thread -
            /// hence main thread. Order matters too: the Texture2Drd's RenderingServer view must let go
            /// of the RD texture before the RD texture itself is freed, or Godot reports a dangling
            /// view. The Texture2Drd itself is a refcounted Godot resource and is NOT Dispose()d: a
            /// Sprite2D the caller left it on still holds it, and disposing the managed wrapper would
            /// make that reference throw instead of showing an empty texture. (Godot keeps reporting
            /// the old <c>TextureRdRid</c> afterward - only the view is freed and the size zeroed - which
            /// is Godot's behavior, not a leak.)
            /// </summary>
            internal override void ReleaseSceneTexture()
            {
                if (_texture == null)
                    return;
                _texture.TextureRdRid = default;
                _texture = null;
            }

            public override void Dispose()
            {
                if (_disposed)
                    return;
                _disposed = true;
                _backend.Untrack(this);

                ReleaseSceneTexture();

                try
                {
                    // Skia submits asynchronously; make sure the last frame's work on this texture has
                    // landed before anything backing it is destroyed.
                    _backend.WaitForPendingGpuWork();
                }
                finally
                {
                    // Freed even when the wait throws (device lost): _disposed is already set, so
                    // there is no second chance to release them.
                    try
                    {
                        _gpu?.Dispose();
                        _gpu = null;
                    }
                    finally
                    {
                        if (_textureRid.IsValid)
                        {
                            _backend.RenderingDevice.FreeRid(_textureRid);
                            _textureRid = default;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// The per-target, per-graphics-API GPU state behind one RD-backed <see cref="SkiaGodotRenderTarget2D"/>:
    /// whatever wraps (or stands in for) the RD texture on Skia's side. <see cref="BeginFrame"/> returns
    /// the surface to draw on; <see cref="EndFrame"/> submits the draw and hands the RD texture back to
    /// Godot in the state it expects.
    /// </summary>
    internal abstract class RenderingDeviceGpuResources : IDisposable
    {
        internal abstract SKSurface BeginFrame();
        internal abstract void EndFrame();
        public abstract void Dispose();
    }
}
