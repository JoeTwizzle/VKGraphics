using OpenTK.Graphics.Vulkan;
using OpenTK.Platform;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VKGraphics.Vulkan;
using VKGraphics.Windowing.Vulkan;

namespace VKGraphics.Windowing;
public static class ResourceFactoryExtensions
{
    public static Swapchain CreateSwapchain(this ResourceFactory rf, SwapchainDescription description)
    {
        var vkRf = Util.AssertSubtype<ResourceFactory, VkResourceFactory>(rf);
        return new VkSwapchain(vkRf.gd, ref description);
    }

    public static Swapchain CreateSwapchain(this ResourceFactory rf, SwapchainDescription description, VkSurfaceKHR existingSurface)
    {
        var vkRf = Util.AssertSubtype<ResourceFactory, VkResourceFactory>(rf);
        return new VkSwapchain(vkRf.gd, ref description,existingSurface);
    }
}
