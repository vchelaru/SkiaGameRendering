using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Framework.Utilities;
using SkiaGameRendering.Core.VK;
using SkiaSharp;

namespace SkiaGameRendering
{
    /// <summary>
    /// SkiaBackend for MonoGame's native DesktopVK platform (<c>MonoGame.Framework.Native</c>
    /// 3.8.6-preview.2+). Windows and Linux; SkiaSharp's macOS native build has no Vulkan backend.
    ///
    /// Adapter over <see cref="VkSkiaSurfaceFactory"/> (see that class for how the Vulkan interop
    /// itself works). Skia draws on the very <c>VkDevice</c>/<c>VkQueue</c> MonoGame renders with,
    /// read from <c>GraphicsDevice.GetNativeHandles()</c>, into a <c>VkImage</c> this backend creates
    /// (<see cref="VkImageAllocator"/>) and hands back to MonoGame through
    /// <c>RenderTarget2D.FromNativeHandle()</c>. No reflection and no CPU readback.
    ///
    /// MAINTENANCE NOTES:
    /// - <b>Image layouts.</b> MonoGame wraps the image believing it is in
    ///   <c>SHADER_READ_ONLY_OPTIMAL</c> and never changes that belief while only sampling it. Skia
    ///   leaves a surface in <c>COLOR_ATTACHMENT_OPTIMAL</c> and cannot report otherwise (see
    ///   <see cref="VkSkiaSurfaceFactory.EndDraw(bool)"/>), so the image is moved to
    ///   <c>SHADER_READ_ONLY_OPTIMAL</c> once at creation and again after every draw, and moved back to
    ///   <c>COLOR_ATTACHMENT_OPTIMAL</c> before every draw but the first (Skia's own record of the
    ///   layout survives between draws; the first draw starts from the layout it was told).
    /// - <b>Release is deferred.</b> A destroyed target's image and memory are freed a few draws after
    ///   its <see cref="RenderTarget2D"/> is disposed, because MonoGame may still have frames in
    ///   flight that sample it.
    /// - MonoGame does not expose its queue lock, so none is passed: Skia, this backend and MonoGame
    ///   must all submit from the thread that runs <c>Draw</c>.
    /// - The Skia context is created with no instance or device extensions listed and Vulkan 1.0 as
    ///   its ceiling, because MonoGame creates its instance as a 1.0 app (see <see cref="Initialize"/>).
    ///   Skia only draws 2D into an image here and never presents.
    /// </summary>
    public class SkiaVulkanBackend : SkiaBackend
    {
        // The draws a released image waits out before it is destroyed.
        const int ReleaseDelayDraws = 4;

        readonly VkSkiaSurfaceFactory _factory = new();
        readonly ConditionalWeakTable<Texture2D, TargetImage> _images = new();
        readonly List<(VkImageAllocation image, long drawIndex)> _pendingReleases = new();
        VkImageLayoutTransitioner? _transitioner;
        IntPtr _instance;
        IntPtr _physicalDevice;
        IntPtr _device;
        long _drawIndex;
        TargetImage? _current;
        TargetImage? _handBack;

        public override GRContext GRContext => _factory.GRContext;

        public override void Initialize(GraphicsDevice graphicsDevice)
        {
            GraphicsDevice = graphicsDevice;

            var handles = graphicsDevice.GetNativeHandles();
            if (handles.Backend != GraphicsBackend.Vulkan)
                throw new InvalidOperationException(
                    $"SkiaVulkanBackend requires MonoGame's DesktopVK platform; the GraphicsDevice reported {handles.Backend}.");

            _instance = handles.Instance;
            _physicalDevice = handles.PhysicalDevice;
            _device = handles.LogicalDevice;

            // MonoGame creates its VkInstance with apiVersion 1.0 (MGG_Vulkan.cpp), and drivers built on
            // Mesa's runtime (lavapipe among them) hand out only 1.0 entry points to such an app. Asking
            // Skia for more makes GRContext.CreateVulkan fail there; NVIDIA hands out everything, which is
            // why a real GPU hides it.
            var apiVersion = Math.Min(
                VkSkiaSurfaceFactory.QueryApiVersion(_instance, _physicalDevice),
                VkConstants.MakeApiVersion(1, 0));

            var queueFamilyIndex = (uint)handles.QueueFamilyIndex;
            _factory.InitializeFromNative(
                _instance, _physicalDevice, _device, handles.Queue,
                graphicsQueueFamilyIndex: queueFamilyIndex,
                apiVersion: apiVersion,
                instanceExtensions: [],
                deviceExtensions: [],
                acquireQueueLock: null);

            _transitioner = new VkImageLayoutTransitioner(_device, handles.Queue, queueFamilyIndex);
        }

        internal override void BeginDraw()
        {
            _factory.BeginDraw();
            ReleaseFinishedImages();
        }

        internal override void EndDraw()
        {
            // No CPU wait: MonoGame submits to this same queue afterward, so its sampling is queue-ordered behind Skia's draw.
            _factory.EndDraw(synchronous: false);

            if (_handBack is { } target)
            {
                _handBack = null;
                ToShaderReadOnly(target.Allocation.Image);
            }
        }

        internal override SkiaTarget CreateTarget(int width, int height, SKColorType colorType)
        {
            // Skia's pixel layout has to match the VkFormat the image was created with.
            if (colorType is not (SKColorType.Rgba8888 or SKColorType.Bgra8888))
                throw new NotSupportedException(
                    $"SkiaVulkanBackend supports SKColorType.Rgba8888 and SKColorType.Bgra8888, not {colorType}.");

            return base.CreateTarget(width, height, colorType);
        }

        internal override Texture2D CreateTexture(int width, int height, SurfaceFormat format)
        {
            var vkFormat = format switch
            {
                SurfaceFormat.Color => 37u,   // VK_FORMAT_R8G8B8A8_UNORM
                SurfaceFormat.Bgra32 => 44u,  // VK_FORMAT_B8G8R8A8_UNORM
                _ => throw new NotSupportedException($"SkiaVulkanBackend does not support SurfaceFormat.{format}."),
            };

            var allocation = VkImageAllocator.Create(_instance, _physicalDevice, _device, width, height, vkFormat);
            try
            {
                // MonoGame treats a wrapped image as already sitting in SHADER_READ_ONLY_OPTIMAL.
                _transitioner!.Transition(
                    allocation.Image,
                    oldLayout: VkConstants.ImageLayoutUndefined,
                    newLayout: VkConstants.ImageLayoutShaderReadOnlyOptimal,
                    srcStageMask: VkConstants.PipelineStageTopOfPipe,
                    srcAccessMask: 0,
                    dstStageMask: VkConstants.PipelineStageFragmentShader,
                    dstAccessMask: VkConstants.AccessShaderRead);

                var texture = RenderTarget2D.FromNativeHandle(GraphicsDevice, (nint)allocation.Image, width, height, format);
                _images.Add(texture, new TargetImage(allocation));
                texture.Disposing += (_, _) => _pendingReleases.Add((allocation, _drawIndex));
                return texture;
            }
            catch
            {
                _transitioner!.WaitForCompletion();
                VkImageAllocator.Destroy(_device, allocation);
                throw;
            }
        }

        internal override object CaptureTextureHandle(Texture2D texture) =>
            _images.TryGetValue(texture, out var image)
                ? image
                : throw new InvalidOperationException("The texture was not created by this backend.");

        internal override (SKSurface surface, GRBackendRenderTarget renderTarget) CreateSurface(
            object textureHandle, Texture2D texture, int width, int height, SKColorType colorType, out object renderState)
        {
            var target = (TargetImage)textureHandle;
            var state = _factory.CreateTextureState(
                target.Allocation.Image, target.Allocation.Format, VkConstants.ImageLayoutShaderReadOnlyOptimal,
                target.Allocation.UsageFlags,
                imageTiling: 0 /* VK_IMAGE_TILING_OPTIMAL - VkImageAllocator always creates optimal tiling. */);
            renderState = target;
            return _factory.CreateSurface(state, width, height, colorType);
        }

        internal override void BindForDrawing(object renderState)
        {
            var target = (TargetImage)renderState;
            if (target.HasDrawn)
            {
                _transitioner!.Transition(
                    target.Allocation.Image,
                    oldLayout: VkConstants.ImageLayoutShaderReadOnlyOptimal,
                    newLayout: VkConstants.ImageLayoutColorAttachmentOptimal,
                    srcStageMask: VkConstants.PipelineStageFragmentShader,
                    srcAccessMask: VkConstants.AccessShaderRead,
                    dstStageMask: VkConstants.PipelineStageColorAttachmentOutput,
                    dstAccessMask: VkConstants.AccessColorAttachmentWrite);
            }

            _current = target;
        }

        internal override void UnbindAfterDrawing()
        {
            if (_current is { } target)
            {
                _current = null;
                target.HasDrawn = true;
                _handBack = target;
            }
        }

        // The image itself is destroyed when its RenderTarget2D is disposed (see CreateTexture).
        internal override void DisposeRenderState(object renderState) { }

        void ToShaderReadOnly(ulong image) =>
            _transitioner!.Transition(
                image,
                oldLayout: VkConstants.ImageLayoutColorAttachmentOptimal,
                newLayout: VkConstants.ImageLayoutShaderReadOnlyOptimal,
                srcStageMask: VkConstants.PipelineStageColorAttachmentOutput,
                srcAccessMask: VkConstants.AccessColorAttachmentWrite,
                dstStageMask: VkConstants.PipelineStageFragmentShader,
                dstAccessMask: VkConstants.AccessShaderRead);

        void ReleaseFinishedImages()
        {
            _drawIndex++;
            for (int i = _pendingReleases.Count - 1; i >= 0; i--)
            {
                if (_drawIndex - _pendingReleases[i].drawIndex >= ReleaseDelayDraws)
                {
                    VkImageAllocator.Destroy(_device, _pendingReleases[i].image);
                    _pendingReleases.RemoveAt(i);
                }
            }
        }

        public override void Dispose()
        {
            _transitioner?.Dispose();
            _transitioner = null;
            _factory.Dispose();

            foreach (var (image, _) in _pendingReleases)
                VkImageAllocator.Destroy(_device, image);
            _pendingReleases.Clear();
        }

        sealed class TargetImage(VkImageAllocation allocation)
        {
            internal VkImageAllocation Allocation { get; } = allocation;
            internal bool HasDrawn { get; set; }
        }
    }
}
