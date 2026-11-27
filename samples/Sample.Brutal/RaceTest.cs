using System.Diagnostics;
using System.Runtime.InteropServices;
using SampleShared;
using SkiaGameRendering;
using SkiaSharp;
using static Tests.CoreVK.VulkanTestNative;

/// <summary>
/// --race-test: many frames of Skia drawing the Scene into one target while the app reads that target on the GPU,
/// with no CPU wait between frames. Each frame stamps a frame-unique color into a corner; the app's copy of frame i,
/// submitted right after <c>End</c>, must see frame i's stamp, never frame i+1's. Also prints the per-frame cost.
/// Exit 0 = every frame read its own stamp.
/// </summary>
static class RaceTest
{
    const int Warmup = 30;
    const int Frames = 1000;
    const int StampX = 4, StampY = 4;

    static SKColor Stamp(int frame) => new((byte)(frame & 0xFF), (byte)((frame >> 8) & 0xFF), 77);

    public static unsafe int Run(IntPtr physical, IntPtr device, IntPtr queue, uint family, SkiaVulkanContext context)
    {
        static void Ok(int r, string what) { if (r != VK_SUCCESS) throw new InvalidOperationException($"{what} failed: {r}"); }
        var total = Warmup + Frames;
        using var target = new SkiaRenderTarget2D(context, Scene.Width, Scene.Height);
        var image = (IntPtr)(long)target.VkImage;

        // One host-visible texel per frame.
        var bufferInfo = new VkBufferCreateInfo
        {
            sType = VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO, size = (ulong)(total * 4), usage = VK_BUFFER_USAGE_TRANSFER_DST_BIT, sharingMode = VK_SHARING_MODE_EXCLUSIVE,
        };
        Ok(vkCreateBuffer(device, &bufferInfo, IntPtr.Zero, out var buffer), "vkCreateBuffer");
        vkGetBufferMemoryRequirements(device, buffer, out var req);
        vkGetPhysicalDeviceMemoryProperties(physical, out var memProps);
        var type = -1;
        for (var i = 0; i < memProps.memoryTypeCount && type < 0; i++)
            if ((req.memoryTypeBits & (1u << i)) != 0 && (memProps.memoryTypes[i].propertyFlags & (VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT)) ==
                (VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT | VK_MEMORY_PROPERTY_HOST_COHERENT_BIT))
                type = i;
        var alloc = new VkMemoryAllocateInfo { sType = VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO, allocationSize = req.size, memoryTypeIndex = (uint)type };
        Ok(vkAllocateMemory(device, &alloc, IntPtr.Zero, out var memory), "vkAllocateMemory");
        Ok(vkBindBufferMemory(device, buffer, memory, 0), "vkBindBufferMemory");

        // Pre-recorded reads, one per frame, so the loop only submits: the app samples (here, copies) frame i's image.
        var poolInfo = new VkCommandPoolCreateInfo { sType = VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO, queueFamilyIndex = family };
        Ok(vkCreateCommandPool(device, &poolInfo, IntPtr.Zero, out var pool), "vkCreateCommandPool");
        var cbs = new IntPtr[total];
        var range = new VkImageSubresourceRange { aspectMask = VK_IMAGE_ASPECT_COLOR_BIT, levelCount = 1, layerCount = 1 };
        for (var i = 0; i < total; i++)
        {
            var cbInfo = new VkCommandBufferAllocateInfo { sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO, commandPool = pool, level = VK_COMMAND_BUFFER_LEVEL_PRIMARY, commandBufferCount = 1 };
            Ok(vkAllocateCommandBuffers(device, &cbInfo, out cbs[i]), "vkAllocateCommandBuffers");
            var begin = new VkCommandBufferBeginInfo { sType = VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO };
            Ok(vkBeginCommandBuffer(cbs[i], &begin), "vkBeginCommandBuffer");
            var toCopy = new VkImageMemoryBarrier
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER, srcAccessMask = 0x20 /* SHADER_READ */, dstAccessMask = VK_ACCESS_TRANSFER_READ_BIT,
                oldLayout = 5, newLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL,
                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED, dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED, image = image, subresourceRange = range,
            };
            vkCmdPipelineBarrier(cbs[i], 0x80 /* FRAGMENT_SHADER */, VK_PIPELINE_STAGE_TRANSFER_BIT, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 1, &toCopy);
            var region = new VkBufferImageCopy
            {
                bufferOffset = (ulong)(i * 4),
                imageSubresource = new VkImageSubresourceLayers { aspectMask = VK_IMAGE_ASPECT_COLOR_BIT, layerCount = 1 },
                imageOffset = new VkOffset3D { x = StampX, y = StampY },
                imageExtent = new VkExtent3D { width = 1, height = 1, depth = 1 },
            };
            vkCmdCopyImageToBuffer(cbs[i], image, VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, buffer, 1, &region);
            // Back to the contract layout; Skia's next Begin waits on fragment-shader work, which this chains into.
            var back = new VkImageMemoryBarrier
            {
                sType = VK_STRUCTURE_TYPE_IMAGE_MEMORY_BARRIER, srcAccessMask = 0, dstAccessMask = 0x20,
                oldLayout = VK_IMAGE_LAYOUT_TRANSFER_SRC_OPTIMAL, newLayout = 5,
                srcQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED, dstQueueFamilyIndex = VK_QUEUE_FAMILY_IGNORED, image = image, subresourceRange = range,
            };
            vkCmdPipelineBarrier(cbs[i], VK_PIPELINE_STAGE_TRANSFER_BIT, 0x80, 0, 0, IntPtr.Zero, 0, IntPtr.Zero, 1, &back);
            Ok(vkEndCommandBuffer(cbs[i]), "vkEndCommandBuffer");
        }

        using var stamp = new SKPaint();
        var skiaMs = new double[Frames];
        var loop = Stopwatch.StartNew();
        for (var i = 0; i < total; i++)
        {
            if (i == Warmup)
            {
                Ok(vkDeviceWaitIdle(device), "vkDeviceWaitIdle");
                loop.Restart();
            }
            var t0 = Stopwatch.GetTimestamp();
            target.Begin(clear: false);
            target.Canvas.Clear(Scene.Background);
            Scene.Draw(target.Canvas);
            stamp.Color = Stamp(i);
            target.Canvas.DrawRect(0, 0, 16, 16, stamp);
            target.End();
            if (i >= Warmup)
                skiaMs[i - Warmup] = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

            var cb = cbs[i];
            var submit = new VkSubmitInfo { sType = VK_STRUCTURE_TYPE_SUBMIT_INFO, commandBufferCount = 1, pCommandBuffers = (IntPtr)(&cb) };
            Ok(vkQueueSubmit(queue, 1, &submit, IntPtr.Zero), "vkQueueSubmit");
        }
        Ok(vkDeviceWaitIdle(device), "vkDeviceWaitIdle");
        var loopMs = loop.Elapsed.TotalMilliseconds / Frames;

        Ok(vkMapMemory(device, memory, 0, (ulong)(total * 4), 0, out var mapped), "vkMapMemory");
        var bytes = new byte[total * 4];
        Marshal.Copy(mapped, bytes, 0, bytes.Length);
        vkUnmapMemory(device, memory);
        var wrong = 0;
        var firstWrong = -1;
        for (var i = 0; i < total; i++)
        {
            var want = Stamp(i);
            if (bytes[i * 4] != want.Red || bytes[i * 4 + 1] != want.Green || bytes[i * 4 + 2] != want.Blue)
            {
                if (firstWrong < 0)
                    firstWrong = i;
                wrong++;
            }
        }
        if (firstWrong >= 0)
            Console.WriteLine($"race: frame {firstWrong} read {bytes[firstWrong * 4]},{bytes[firstWrong * 4 + 1]},{bytes[firstWrong * 4 + 2]} (want {Stamp(firstWrong).Red},{Stamp(firstWrong).Green},77)");

        var half = Frames / 2;
        var sorted = (double[])skiaMs.Clone();
        Array.Sort(sorted);
        Console.WriteLine(
            $"race test: {wrong} of {total} frames read the wrong image -> {(wrong == 0 ? "PASS" : "FAIL")}. " +
            $"Begin..End CPU ms/frame mean {skiaMs.Average():F3} (halves {skiaMs[..half].Average():F3} / {skiaMs[half..].Average():F3}), " +
            $"median {sorted[Frames / 2]:F3}; loop ms/frame incl. GPU {loopMs:F3} ({Frames} frames, {Scene.Width}x{Scene.Height} Scene)");

        vkDestroyCommandPool(device, pool, IntPtr.Zero);
        vkFreeMemory(device, memory, IntPtr.Zero);
        vkDestroyBuffer(device, buffer, IntPtr.Zero);
        return wrong == 0 ? 0 : 1;
    }
}
