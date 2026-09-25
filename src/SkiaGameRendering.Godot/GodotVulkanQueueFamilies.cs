using SkiaGameRendering.Core.VK;

namespace SkiaGameRendering.Godot
{
    /// <summary>
    /// Works out which Vulkan queue family Godot puts its texture/buffer uploads on, so
    /// <see cref="VulkanGodotBackend"/> can tell whether they share the queue Skia submits to. No Godot
    /// types, so it is testable without a running engine.
    /// </summary>
    internal static class GodotVulkanQueueFamilies
    {
        /// <summary>
        /// Mirrors <c>RenderingDeviceDriverVulkan::command_queue_family_get</c> (Godot 4.7.2) as
        /// <c>RenderingDevice::initialize</c> calls it for the transfer queue: among the families Godot
        /// created a queue in (any of graphics/compute/transfer), the one whose raw flags include
        /// <c>TRANSFER</c> and have the lowest value, first on ties; the main family when there is none.
        /// Godot creates one queue per family, so the same family means the same <c>VkQueue</c>.
        /// </summary>
        internal static bool UploadsShareMainQueue(IReadOnlyList<uint> queueFamilyFlags, uint mainQueueFamily)
        {
            const uint usedByGodot = VkConstants.QueueGraphics | VkConstants.QueueCompute | VkConstants.QueueTransfer;
            uint pickedFlags = uint.MaxValue;
            int pickedFamily = -1;
            for (int i = 0; i < queueFamilyFlags.Count; i++)
            {
                var flags = queueFamilyFlags[i];
                if ((flags & usedByGodot) == 0 || (flags & VkConstants.QueueTransfer) == 0)
                    continue;
                if (flags < pickedFlags)
                {
                    pickedFlags = flags;
                    pickedFamily = i;
                }
            }
            return pickedFamily < 0 || pickedFamily == mainQueueFamily;
        }
    }
}
