using OpenTK.Graphics.Vulkan;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using VKGraphics.Vulkan;
using VKGraphics.Windowing.Vulkan;

namespace VKGraphics.Windowing;
public static class GraphicsDeviceExtensions
{
    public static unsafe void SwapBuffers(this GraphicsDevice graphicsDevice, Swapchain swapchain)
    {
        var vkSc = Util.AssertSubtype<Swapchain, VkSwapchain>(swapchain);
        var vkGd = Util.AssertSubtype<GraphicsDevice, VkGraphicsDevice>(graphicsDevice);
        var deviceSwapchain = vkSc.DeviceSwapchain;
        var presentInfo = new VkPresentInfoKHR();
        presentInfo.swapchainCount = 1;
        presentInfo.pSwapchains = &deviceSwapchain;
        uint imageIndex = vkSc.ImageIndex;
        presentInfo.pImageIndices = &imageIndex;

        object presentLock = vkSc.PresentQueueIndex == vkGd.GraphicsQueueIndex ? vkGd.graphicsQueueLock : vkSc;

        lock (presentLock)
        {
            Vk.QueuePresentKHR(vkSc.PresentQueue, &presentInfo);

            if (vkSc.AcquireNextImage(vkGd.Device, VkSemaphore.Zero, vkSc.ImageAvailableFence))
            {
                var fence = vkSc.ImageAvailableFence;
                Vk.WaitForFences(vkGd.Device, 1, &fence, 1, ulong.MaxValue);
                Vk.ResetFences(vkGd.Device, 1, &fence);
            }
        }
    }
}
