using OpenTK.Graphics.Vulkan;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal partial class VulkanGraphicsDevice
{
    private static readonly Lazy<bool> s_isSupported = new(CheckIsSupported);
    private static readonly FixedUtf8String s_name = "Veldrid";

    internal static bool IsSupported()
    {
        return s_isSupported.Value;
    }

    private static unsafe bool CheckIsSupported()
    {
        VkApplicationInfo applicationInfo = new()
        {
            apiVersion = new VkVersion(1, 0, 0),
            applicationVersion = new VkVersion(1, 0, 0),
            engineVersion = new VkVersion(1, 0, 0),
            pApplicationName = s_name,
            pEngineName = s_name
        };

        VkInstanceCreateInfo instanceCI = new()
        {
            pApplicationInfo = &applicationInfo
        };

        VkInstance testInstance;
        VkResult result = Vk.CreateInstance(&instanceCI, null, &testInstance);
        if (result != VkResult.Success)
        {
            return false;
        }

        uint physicalDeviceCount = 0;
        result = EnumeratePhysicalDevices(testInstance, &physicalDeviceCount, null);
        if (result != VkResult.Success || physicalDeviceCount == 0)
        {
            DestroyInstance(testInstance, null);
            return false;
        }

        DestroyInstance(testInstance, null);

        return true;

#if false // Vulkan is supported even if it can't present. (This may not by useful for the typical case, but is in general.)
        HashSet<string> instanceExtensions = GetInstanceExtensions();
        if (!instanceExtensions.Contains(CommonStrings.VK_KHR_SURFACE_EXTENSION_NAME))
        {
            //return false;
        }

        foreach (FixedUtf8String surfaceExtension in GetSurfaceExtensions(instanceExtensions))
        {
            if (instanceExtensions.Contains(surfaceExtension))
            {
                return true;
            }
        }

        return false;
#endif
    }
}
