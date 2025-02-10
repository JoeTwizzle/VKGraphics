using OpenTK.Graphics.Vulkan;
using OpenTK.Platform;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VKGraphics.Vulkan;
using static VKGraphics.Vulkan.VulkanUtil;

namespace VKGraphics.Windowing.Vulkan;

internal unsafe class VkSwapchain : Swapchain
{
    public override Framebuffer Framebuffer => framebuffer;

    public override bool IsDisposed => disposed;

    public VkSwapchainKHR DeviceSwapchain => deviceSwapchain;
    public uint ImageIndex => currentImageIndex;
    public OpenTK.Graphics.Vulkan.VkFence ImageAvailableFence => imageAvailableFence;
    public VkSurfaceKHR Surface { get; }

    public VkQueue PresentQueue => presentQueue;
    public uint PresentQueueIndex => presentQueueIndex;
    public ResourceRefCount RefCount { get; }

    public override string? Name
    {
        get => name;
        set
        {
            name = value;
            gd.SetResourceName(this, value);
        }
    }

    public override bool SyncToVerticalBlank
    {
        get => newSyncToVBlank ?? syncToVBlank;
        set
        {
            if (syncToVBlank != value)
            {
                newSyncToVBlank = value;
            }
        }
    }

    private readonly VkGraphicsDevice gd;
    private readonly VkSwapchainFramebuffer framebuffer;
    private readonly uint presentQueueIndex;
    private readonly VkQueue presentQueue;
    private readonly bool colorSrgb;
    private VkSwapchainKHR deviceSwapchain;
    private OpenTK.Graphics.Vulkan.VkFence imageAvailableFence;
    private bool syncToVBlank;
    private bool? newSyncToVBlank;
    private uint currentImageIndex;
    private string? name;
    private bool disposed;
    public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description) : this(gd, ref description, VkSurfaceKHR.Zero) { }

    public VkSwapchain(VkGraphicsDevice gd, ref SwapchainDescription description, VkSurfaceKHR existingSurface)
    {
        this.gd = gd;
        syncToVBlank = description.SyncToVerticalBlank;
        colorSrgb = description.ColorSrgb;
        if (existingSurface == VkSurfaceKHR.Zero)
        {
            Toolkit.Vulkan.CreateWindowSurface(gd.Instance, description.Source, null, out existingSurface);
        }
        Surface = existingSurface;

        if (!getPresentQueueIndex(out presentQueueIndex))
        {
            throw new VeldridException("The system does not support presenting the given Vulkan surface.");
        }

        VkQueue vkpresentQueue;
        Vk.GetDeviceQueue(this.gd.Device, presentQueueIndex, 0, &vkpresentQueue);
        presentQueue = vkpresentQueue;
        framebuffer = new VkSwapchainFramebuffer(gd, this, Surface, description.Width, description.Height, description.DepthFormat);

        createSwapchain(description.Width, description.Height);

        var fenceCi = new VkFenceCreateInfo();
        fenceCi.flags = 0;
        OpenTK.Graphics.Vulkan.VkFence vkimageAvailableFence;
        Vk.CreateFence(this.gd.Device, &fenceCi, null, &vkimageAvailableFence);
        imageAvailableFence = vkimageAvailableFence;
        AcquireNextImage(this.gd.Device, VkSemaphore.Zero, imageAvailableFence);
        Vk.WaitForFences(this.gd.Device, 1, &vkimageAvailableFence, 1, ulong.MaxValue);
        Vk.ResetFences(this.gd.Device, 1, &vkimageAvailableFence);

        RefCount = new ResourceRefCount(disposeCore);
    }

    #region Disposal

    public override void Dispose()
    {
        RefCount.Decrement();
    }

    #endregion

    public override void Resize(uint width, uint height)
    {
        recreateAndReacquire(width, height);
    }

    public bool AcquireNextImage(VkDevice device, VkSemaphore semaphore, OpenTK.Graphics.Vulkan.VkFence fence)
    {
        if (newSyncToVBlank != null)
        {
            syncToVBlank = newSyncToVBlank.Value;
            newSyncToVBlank = null;
            recreateAndReacquire(framebuffer.Width, framebuffer.Height);
            return false;
        }
        uint vkcurrentImageIndex;
        var result = Vk.AcquireNextImageKHR(
            device,
            deviceSwapchain,
            ulong.MaxValue,
            semaphore,
            fence,
            &vkcurrentImageIndex);
        currentImageIndex = vkcurrentImageIndex;
        framebuffer.SetImageIndex(currentImageIndex);

        if (result == VkResult.ErrorOutOfDateKhr || result == VkResult.SuboptimalKhr)
        {
            createSwapchain(framebuffer.Width, framebuffer.Height);
            return false;
        }

        if (result != VkResult.Success)
        {
            throw new VeldridException("Could not acquire next image from the Vulkan swapchain.");
        }

        return true;
    }

    private void recreateAndReacquire(uint width, uint height)
    {
        if (createSwapchain(width, height))
        {
            if (AcquireNextImage(gd.Device, VkSemaphore.Zero, imageAvailableFence))
            {
                OpenTK.Graphics.Vulkan.VkFence imageAvailableFence = this.imageAvailableFence;
                Vk.WaitForFences(gd.Device, 1, &imageAvailableFence, 1, ulong.MaxValue);
                Vk.ResetFences(gd.Device, 1, &imageAvailableFence);
            }
        }
    }

    private bool createSwapchain(uint width, uint height)
    {
        // Obtain the surface capabilities first -- this will indicate whether the surface has been lost.
        VkSurfaceCapabilitiesKHR surfaceCapabilities;
        var result = Vk.GetPhysicalDeviceSurfaceCapabilitiesKHR(gd.PhysicalDevice, Surface, &surfaceCapabilities);
        if (result == VkResult.ErrorSurfaceLostKhr)
        {
            throw new VeldridException("The Swapchain's underlying surface has been lost.");
        }
        
        if (surfaceCapabilities.minImageExtent.width == 0 && surfaceCapabilities.minImageExtent.height == 0
                                                          && surfaceCapabilities.maxImageExtent.width == 0 && surfaceCapabilities.maxImageExtent.height == 0)
        {
            return false;
        }

        if (deviceSwapchain != VkSwapchainKHR.Zero)
        {
            gd.WaitForIdle();
        }

        currentImageIndex = 0;
        uint surfaceFormatCount = 0;
        result = Vk.GetPhysicalDeviceSurfaceFormatsKHR(gd.PhysicalDevice, Surface, &surfaceFormatCount, null);
        CheckResult(result);
        Span<VkSurfaceFormatKHR> formats = stackalloc VkSurfaceFormatKHR[(int)surfaceFormatCount];
        result = Vk.GetPhysicalDeviceSurfaceFormatsKHR(gd.PhysicalDevice, Surface, &surfaceFormatCount, (VkSurfaceFormatKHR*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(formats)));
        CheckResult(result);

        var desiredFormat = colorSrgb
            ? VkFormat.FormatB8g8r8a8Srgb
            : VkFormat.FormatB8g8r8a8Unorm;

        var surfaceFormat = new VkSurfaceFormatKHR();

        if (surfaceFormatCount == 1 && formats[0].format == VkFormat.FormatUndefined)
        {
            surfaceFormat = new VkSurfaceFormatKHR { colorSpace = VkColorSpaceKHR.ColorspaceSrgbNonlinearKhr, format = desiredFormat };
        }
        else
        {
            for (int i = 0; i < surfaceFormatCount; i++)
            {
                var format = formats[i];
                if (format.colorSpace == VkColorSpaceKHR.ColorspaceSrgbNonlinearKhr && format.format == desiredFormat)
                {
                    surfaceFormat = format;
                    break;
                }
            }

            if (surfaceFormat.format == VkFormat.FormatUndefined)
            {
                if (colorSrgb && surfaceFormat.format != VkFormat.FormatR8g8b8a8Srgb)
                {
                    throw new VeldridException("Unable to create an sRGB Swapchain for this surface.");
                }

                surfaceFormat = formats[0];
            }
        }

        uint presentModeCount = 0;
        result = Vk.GetPhysicalDeviceSurfacePresentModesKHR(gd.PhysicalDevice, Surface, &presentModeCount, null);
        CheckResult(result);
        var presentModesPtr = stackalloc VkPresentModeKHR[(int)presentModeCount];
        result = Vk.GetPhysicalDeviceSurfacePresentModesKHR(gd.PhysicalDevice, Surface, &presentModeCount, presentModesPtr);
        CheckResult(result);
        var presentModes = new Span<int>(presentModesPtr, (int)presentModeCount);
        var presentMode = VkPresentModeKHR.PresentModeFifoKhr;

        if (syncToVBlank)
        {
            if (presentModes.Contains((int)VkPresentModeKHR.PresentModeFifoRelaxedKhr))
            {
                presentMode = VkPresentModeKHR.PresentModeFifoRelaxedKhr;
            }
        }
        else
        {
            if (presentModes.Contains((int)VkPresentModeKHR.PresentModeMailboxKhr))
            {
                presentMode = VkPresentModeKHR.PresentModeMailboxKhr;
            }
            else if (presentModes.Contains((int)VkPresentModeKHR.PresentModeImmediateKhr))
            {
                presentMode = VkPresentModeKHR.PresentModeImmediateKhr;
            }
        }

        uint maxImageCount = surfaceCapabilities.maxImageCount == 0 ? uint.MaxValue : surfaceCapabilities.maxImageCount;
        uint imageCount = Math.Min(maxImageCount, surfaceCapabilities.minImageCount + 1);

        var swapchainCi = new VkSwapchainCreateInfoKHR();
        swapchainCi.surface = Surface;
        swapchainCi.presentMode = presentMode;
        swapchainCi.imageFormat = surfaceFormat.format;
        swapchainCi.imageColorSpace = surfaceFormat.colorSpace;
        uint clampedWidth = Util.Clamp(width, surfaceCapabilities.minImageExtent.width, surfaceCapabilities.maxImageExtent.width);
        uint clampedHeight = Util.Clamp(height, surfaceCapabilities.minImageExtent.height, surfaceCapabilities.maxImageExtent.height);
        if (clampedWidth == 0 || clampedHeight == 0)
        {
            clampedWidth = surfaceCapabilities.currentExtent.width;
            clampedHeight = surfaceCapabilities.currentExtent.height;
        }
        
        swapchainCi.imageExtent = new VkExtent2D { width = clampedWidth, height = clampedHeight };
        swapchainCi.minImageCount = imageCount;
        swapchainCi.imageArrayLayers = 1;
        swapchainCi.imageUsage = VkImageUsageFlagBits.ImageUsageColorAttachmentBit | VkImageUsageFlagBits.ImageUsageTransferDstBit;

        var queueFamilyIndices = new FixedArray2<uint>(gd.GraphicsQueueIndex, gd.PresentQueueIndex);

        if (gd.GraphicsQueueIndex != gd.PresentQueueIndex)
        {
            swapchainCi.imageSharingMode = VkSharingMode.SharingModeConcurrent;
            swapchainCi.queueFamilyIndexCount = 2;
            swapchainCi.pQueueFamilyIndices = &queueFamilyIndices.First;
        }
        else
        {
            swapchainCi.imageSharingMode = VkSharingMode.SharingModeExclusive;
            swapchainCi.queueFamilyIndexCount = 0;
        }

        swapchainCi.preTransform = VkSurfaceTransformFlagBitsKHR.SurfaceTransformIdentityBitKhr;
        swapchainCi.compositeAlpha = VkCompositeAlphaFlagBitsKHR.CompositeAlphaOpaqueBitKhr;
        swapchainCi.clipped = 1;

        var oldSwapchain = deviceSwapchain;
        swapchainCi.oldSwapchain = oldSwapchain;
        VkSwapchainKHR vkdeviceSwapchain;
        result = Vk.CreateSwapchainKHR(gd.Device, &swapchainCi, null, &vkdeviceSwapchain);
        deviceSwapchain = vkdeviceSwapchain;
        CheckResult(result);
        if (oldSwapchain != VkSwapchainKHR.Zero)
        {
            Vk.DestroySwapchainKHR(gd.Device, oldSwapchain, null);
        }

        framebuffer.SetNewSwapchain(deviceSwapchain, width, height, surfaceFormat, swapchainCi.imageExtent);
        return true;
    }

    private bool getPresentQueueIndex(out uint queueFamilyIndex)
    {
        uint deviceGraphicsQueueIndex = gd.GraphicsQueueIndex;
        uint devicePresentQueueIndex = gd.PresentQueueIndex;

        if (queueSupportsPresent(deviceGraphicsQueueIndex, Surface))
        {
            queueFamilyIndex = deviceGraphicsQueueIndex;
            return true;
        }

        if (deviceGraphicsQueueIndex != devicePresentQueueIndex && queueSupportsPresent(devicePresentQueueIndex, Surface))
        {
            queueFamilyIndex = devicePresentQueueIndex;
            return true;
        }

        queueFamilyIndex = 0;
        return false;
    }

    private bool queueSupportsPresent(uint queueFamilyIndex, VkSurfaceKHR surface)
    {
        int supported;
        var result = Vk.GetPhysicalDeviceSurfaceSupportKHR(
            gd.PhysicalDevice,
            queueFamilyIndex,
            surface,
            &supported);
        CheckResult(result);
        return supported != 0;
    }

    private void disposeCore()
    {
        Vk.DestroyFence(gd.Device, imageAvailableFence, null);
        framebuffer.Dispose();
        Vk.DestroySwapchainKHR(gd.Device, deviceSwapchain, null);
        Vk.DestroySurfaceKHR(gd.Instance, Surface, null);

        disposed = true;
    }
}
