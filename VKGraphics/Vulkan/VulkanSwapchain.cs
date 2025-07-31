using OpenTK.Graphics.Vulkan;
using OpenTK.Platform;
using System.Buffers;
using System.Diagnostics;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal sealed class VulkanSwapchain : Swapchain, IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly VkSurfaceKHR _surface;
    private VkSwapchainKHR _deviceSwapchain;
    private readonly VulkanSwapchainFramebuffer _framebuffer;

    private readonly int _presentQueueIndex;
    private readonly VkQueue _presentQueue;

    private VkSemaphore[] _semaphores = [];
    private VkFence[] _fences = [];
    private uint _fenceIndex;
    private uint _currentImageIndex;
    private uint _imageCount;
    private int _presentTargetQueueLength;

    private readonly WindowHandle _swapchainSource;
    private readonly bool _colorSrgb;
    private bool _syncToVBlank;
    private bool? _newSyncToVBlank;
    private bool _useFifoLatestIfAvailable;
    private bool? _newUseFifoLatestIfAvailable;

    private string? _name;
    public ResourceRefCount RefCount { get; }
    public object PresentLock { get; } = new();

    public override VulkanSwapchainFramebuffer Framebuffer => _framebuffer;
    public override bool IsDisposed => RefCount.IsDisposed;
    public VkSwapchainKHR DeviceSwapchain => _deviceSwapchain;
    public uint ImageIndex => _currentImageIndex;
    public VkSurfaceKHR Surface => _surface;
    public VkQueue PresentQueue => _presentQueue;
    public int PresentQueueIndex => _presentQueueIndex;

    internal unsafe VulkanSwapchain(VulkanGraphicsDevice gd, in SwapchainDescription description, ref VkSurfaceKHR surface, int presentQueueIndex)
    {
        _gd = gd;
        _surface = surface;

        _swapchainSource = description.Source;
        _syncToVBlank = description.SyncToVerticalBlank;
        _colorSrgb = description.ColorSrgb;

        Debug.Assert(presentQueueIndex != -1);
        _presentQueueIndex = presentQueueIndex;
        _presentQueue = gd._deviceCreateState.MainQueue; // right now, we only ever create one queue

        RefCount = new(this);
        surface = default; // we take ownership of the surface

        try
        {
            _framebuffer = new(gd, this, description);

            CreateSwapchain(description.Width, description.Height);

            // make sure we pre-emptively acquire the first image for the swapchain
            _ = AcquireNextImage(_gd.Device);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public override void Dispose() => RefCount?.DecrementDispose();
    unsafe void IResourceRefCountTarget.RefZeroed()
    {
        _framebuffer?.RefZeroed();

        foreach (var fence in _fences)
        {
            if (fence != VkFence.Zero)
            {
                WaitForFences(_gd.Device, 1, &fence, 1, ulong.MaxValue);
                DestroyFence(_gd.Device, fence, null);
            }
        }
        foreach (var semaphore in _semaphores)
        {
            if (semaphore != VkSemaphore.Zero)
            {
                DestroySemaphore(_gd.Device, semaphore, null);
            }
        }

        if (_deviceSwapchain != VkSwapchainKHR.Zero)
        {
            DestroySwapchainKHR(_gd.Device, _deviceSwapchain, null);
        }

        if (_surface != VkSurfaceKHR.Zero)
        {
            DestroySurfaceKHR(_gd._deviceCreateState.Instance, _surface, null);
        }
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeSwapchainKhrExt, _deviceSwapchain.Handle, value);
        }
    }

    public override bool SyncToVerticalBlank
    {
        get => _newSyncToVBlank ?? _syncToVBlank;
        set
        {
            if (_syncToVBlank != value)
            {
                _newSyncToVBlank = value;
            }
        }
    }

    public bool UseFifoLatestIfAvailable
    {
        get => _newUseFifoLatestIfAvailable ?? _useFifoLatestIfAvailable;

        set
        {
            if (_useFifoLatestIfAvailable != value)
            {
                _newUseFifoLatestIfAvailable = value;
            }
        }
    }

    private unsafe bool CreateSwapchain(uint width, uint height)
    {
        var physicalDevice = _gd._deviceCreateState.PhysicalDevice;

        // Obtain the surface capabilities first -- this will indicate whether the surface has been lost.
        VkSurfaceCapabilitiesKHR surfaceCapabilities;
        VkResult result = GetPhysicalDeviceSurfaceCapabilitiesKHR(physicalDevice, _surface, &surfaceCapabilities);
        if (result == VkResult.ErrorSurfaceLostKhr)
        {
            throw new VeldridException($"The Swapchain's underlying surface has been lost.");
        }

        if (surfaceCapabilities.minImageExtent.width == 0 && surfaceCapabilities.minImageExtent.height == 0
            && surfaceCapabilities.maxImageExtent.width == 0 && surfaceCapabilities.maxImageExtent.height == 0)
        {
            return false;
        }

        /*
        if (_deviceSwapchain != VkSwapchainKHR.NULL)
        {
            if (_fences.Length > 0)
            {
                fixed (VkFence* pFences = _fences)
                {
                    vkWaitForFences(_gd.Device, _imageCount + 1, pFences, 1, ulong.MaxValue);
                }
            }
            else
            {
                _gd.WaitForIdle();
            }
        }
        */

        _currentImageIndex = 0;
        var surfaceFormatCount = 0u;
        VulkanUtil.CheckResult(GetPhysicalDeviceSurfaceFormatsKHR(physicalDevice, _surface, &surfaceFormatCount, null));
        VkSurfaceFormatKHR[] formats = new VkSurfaceFormatKHR[surfaceFormatCount];
        fixed (VkSurfaceFormatKHR* formatsPtr = formats)
        {
            VulkanUtil.CheckResult(GetPhysicalDeviceSurfaceFormatsKHR(_gd._deviceCreateState.PhysicalDevice, _surface, &surfaceFormatCount, formatsPtr));
        }

        VkFormat desiredFormat = _colorSrgb
            ? VkFormat.FormatB8g8r8a8Srgb
            : VkFormat.FormatB8g8r8a8Unorm;

        VkSurfaceFormatKHR surfaceFormat = new();
        if (formats.Length == 1 && formats[0].format == VkFormat.FormatUndefined)
        {
            surfaceFormat.format = desiredFormat;
            surfaceFormat.colorSpace = VkColorSpaceKHR.ColorspaceSrgbNonlinearKhr;
        }
        else
        {
            foreach (VkSurfaceFormatKHR format in formats)
            {
                if (format.colorSpace == VkColorSpaceKHR.ColorspaceSrgbNonlinearKhr && format.format == desiredFormat)
                {
                    surfaceFormat = format;
                    break;
                }
            }
            if (surfaceFormat.format == VkFormat.FormatUndefined)
            {
                if (_colorSrgb && surfaceFormat.format != VkFormat.FormatB8g8r8a8Srgb)
                {
                    throw new VeldridException($"Unable to create an sRGB Swapchain for this surface.");
                }

                surfaceFormat = formats[0];
            }
        }

        uint presentModeCount = 0;
        VulkanUtil.CheckResult(GetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, _surface, &presentModeCount, null));
        VkPresentModeKHR[] presentModes = new VkPresentModeKHR[presentModeCount];
        fixed (VkPresentModeKHR* presentModesPtr = presentModes)
        {
            VulkanUtil.CheckResult(GetPhysicalDeviceSurfacePresentModesKHR(physicalDevice, _surface, &presentModeCount, presentModesPtr));
        }

        uint maxImageCount = surfaceCapabilities.maxImageCount == 0 ? uint.MaxValue : surfaceCapabilities.maxImageCount;
        // TODO: it would maybe be a good idea to enable this to be configurable?
        uint imageCount = Math.Min(maxImageCount, surfaceCapabilities.minImageCount + 1);
        VkPresentModeKHR presentMode = VkPresentModeKHR.PresentModeFifoKhr;
        _presentTargetQueueLength = 0;
        if (_syncToVBlank)
        {
            const VkPresentModeKHR VK_PRESENT_MODE_FIFO_LATEST_READY_EXT = VkPresentModeKHR.PresentModeFifoLatestReadyKhr;
            // TODO: figure out how to throttle sanely with FIFO_LATEST_READY
            if (_useFifoLatestIfAvailable && _gd._deviceCreateState.HasFifoLatestReady && presentModes.Contains(VK_PRESENT_MODE_FIFO_LATEST_READY_EXT))
            {
                presentMode = VK_PRESENT_MODE_FIFO_LATEST_READY_EXT;
                _presentTargetQueueLength = 1;
            }
            else if (presentModes.Contains(VkPresentModeKHR.PresentModeFifoRelaxedKhr))
            {
                presentMode = VkPresentModeKHR.PresentModeFifoRelaxedKhr;
            }
        }
        else
        {
            if (presentModes.Contains(VkPresentModeKHR.PresentModeMailboxKhr))
            {
                presentMode = VkPresentModeKHR.PresentModeMailboxKhr;
                _presentTargetQueueLength = (int)imageCount;
            }
            else if (presentModes.Contains(VkPresentModeKHR.PresentModeImmediateKhr))
            {
                presentMode = VkPresentModeKHR.PresentModeImmediateKhr;
                _presentTargetQueueLength = (int)imageCount;
            }
        }


        uint clampedWidth = Util.Clamp(width, surfaceCapabilities.minImageExtent.width, surfaceCapabilities.maxImageExtent.width);
        uint clampedHeight = Util.Clamp(height, surfaceCapabilities.minImageExtent.height, surfaceCapabilities.maxImageExtent.height);
        VkSwapchainCreateInfoKHR swapchainCI = new()
        {
            surface = _surface,
            presentMode = presentMode,
            imageFormat = surfaceFormat.format,
            imageColorSpace = surfaceFormat.colorSpace,
            imageExtent = new VkExtent2D() { width = clampedWidth, height = clampedHeight },
            minImageCount = imageCount,
            imageArrayLayers = 1,
            imageUsage = VkImageUsageFlagBits.ImageUsageColorAttachmentBit | VkImageUsageFlagBits.ImageUsageTransferDstBit
        };

        uint* queueFamilyIndices = stackalloc uint[]
        {
            (uint)_gd._deviceCreateState.QueueFamilyInfo.MainGraphicsFamilyIdx,
            (uint)_presentQueueIndex,
        };

        if (queueFamilyIndices[0] != queueFamilyIndices[1])
        {
            swapchainCI.imageSharingMode = VkSharingMode.SharingModeConcurrent;
            swapchainCI.queueFamilyIndexCount = 2;
            swapchainCI.pQueueFamilyIndices = queueFamilyIndices;
        }
        else
        {
            swapchainCI.imageSharingMode = VkSharingMode.SharingModeExclusive;
            swapchainCI.queueFamilyIndexCount = 0;
        }

        swapchainCI.preTransform = VkSurfaceTransformFlagBitsKHR.SurfaceTransformIdentityBitKhr;
        swapchainCI.compositeAlpha = VkCompositeAlphaFlagBitsKHR.CompositeAlphaOpaqueBitKhr;
        swapchainCI.clipped = (VkBool32)true;

        VkSwapchainKHR oldSwapchain = _deviceSwapchain;
        swapchainCI.oldSwapchain = oldSwapchain;

        VkSwapchainKHR deviceSwapchain;
        VulkanUtil.CheckResult(CreateSwapchainKHR(_gd.Device, &swapchainCI, null, &deviceSwapchain));
        _deviceSwapchain = deviceSwapchain;

        if (Name is { } name)
        {
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeSwapchainKhrExt, deviceSwapchain.Handle, name);
        }

        if (oldSwapchain != VkSwapchainKHR.Zero)
        {
            DestroySwapchainKHR(_gd.Device, oldSwapchain, null);
        }

        VulkanUtil.CheckResult(GetSwapchainImagesKHR(_gd.Device, deviceSwapchain, &imageCount, null));
        _imageCount = imageCount;

        // as a last step, we need to set up our fences and semaphores
        var oldFenceCount = _fences.Length;
        var oldSemaphoreCount = _semaphores.Length;
        Util.EnsureArrayMinimumSize(ref _fences, imageCount + 1);
        Util.EnsureArrayMinimumSize(ref _semaphores, imageCount + 1);
        _fenceIndex = 0;

        // need to collect an array of the fences and semaphores so we can record them to be destroyed later
        var fenceArr = ArrayPool<VkFence>.Shared.Rent(oldFenceCount);
        var semaphoreArr = ArrayPool<VkSemaphore>.Shared.Rent(oldSemaphoreCount);
        Array.Clear(fenceArr);
        Array.Clear(semaphoreArr);
        oldFenceCount = 0;
        oldSemaphoreCount = 0;

        for (var i = 0; i < _fences.Length; i++)
        {
            if (_fences[i] != VkFence.Zero)
            {
                // we always want to recreate any fences we have, because we want them to be default-signalled
                fenceArr[oldFenceCount++] = _fences[i];
                _fences[i] = VkFence.Zero;
            }

            if (i < imageCount + 1)
            {
                var fenceCi = new VkFenceCreateInfo()
                {
                    flags = VkFenceCreateFlagBits.FenceCreateSignaledBit,
                };
                VkFence fence;
                VulkanUtil.CheckResult(CreateFence(_gd.Device, &fenceCi, null, &fence));
                _fences[i] = fence;
            }
        }

        for (var i = 0; i < _semaphores.Length; i++)
        {
            if (_semaphores[i] != VkSemaphore.Zero)
            {
                // we always want to recreate any semaphores we have, to make sure they aren't signalled when we do our initial acquire
                semaphoreArr[oldSemaphoreCount++] = _semaphores[i];
                _semaphores[i] = VkSemaphore.Zero;
            }

            if (i < imageCount + 1)
            {
                var semaphoreCi = new VkSemaphoreCreateInfo() { };
                VkSemaphore semaphore;
                VulkanUtil.CheckResult(CreateSemaphore(_gd.Device, &semaphoreCi, null, &semaphore));
                _semaphores[i] = semaphore;
            }
        }

        _gd.RegisterSwapchainOldFences(new()
        {
            Fences = fenceArr,
            NumFences = oldFenceCount,
            Semaphores = semaphoreArr,
            NumSemaphores = oldSemaphoreCount,
        });
        _framebuffer.SetNewSwapchain(_deviceSwapchain, width, height, surfaceFormat, swapchainCI.imageExtent);
        return true;
    }

    public unsafe bool AcquireNextImage(VkDevice device)
    {
        if (_newSyncToVBlank != null)
        {
            _syncToVBlank = _newSyncToVBlank.Value;
            _newSyncToVBlank = null;
            RecreateAndReacquire(_framebuffer.Width, _framebuffer.Height);
            return false;
        }
        if (_newUseFifoLatestIfAvailable != null)
        {
            _useFifoLatestIfAvailable = _newUseFifoLatestIfAvailable.Value;
            _newUseFifoLatestIfAvailable = null;
            RecreateAndReacquire(_framebuffer.Width, _framebuffer.Height);
            return false;
        }

        // first, wait for the fence corresponding to the image slot (not to be confused with the image index!) that we'll acquire
        // _presentTargetQueueLength determines how many frames we want to keep in the queue at a time, so how far back we should look before waiting.
        // For standard FIFO-like present (a.k.a. vsync), this is 1. For PRESENT_LATEST, it's 2. For non-vsync, it's the image count.
        var fenceIndex = (_fenceIndex + (uint)_fences.Length - _presentTargetQueueLength) % (uint)_fences.Length;
        // first, wait for the i - N'th fence (which mod N is just the current fence, and the one we will be passing to acquire)
        var waitFence = _fences[fenceIndex];
        _ = WaitForFences(_gd.Device, 1, &waitFence, 1, ulong.MaxValue);
        _ = ResetFences(_gd.Device, 1, &waitFence);

        // then, pick up the semaphore we're going to use
        // we always grab the "extra" one, and we'll swap it into place in the array once we know the image we've acquired
        // The semaphore we pass in to vkAcquireNextImage MUST be unsignaled (so waited-upon), which we guarantee at the callsites
        // of AcquireNextImage. (Either because we *just* recreated the swapchain, or because we are doing a presentation, and thus
        // have a command list that we can force to wait on it.)
        var semaphore = _semaphores[_imageCount];

        uint imageIndex = _currentImageIndex;
        VkResult result = AcquireNextImageKHR(
            device,
            _deviceSwapchain,
            ulong.MaxValue,
            semaphore,
            waitFence,
            &imageIndex);

        _framebuffer.SetImageIndex(imageIndex, semaphore);
        _currentImageIndex = imageIndex;
        // swap this semaphore into position
        _semaphores[_imageCount] = _semaphores[imageIndex];
        _semaphores[imageIndex] = semaphore;
        // and move our fence index forward
        _fences[_imageCount] = _fences[imageIndex];
        _fences[imageIndex] = waitFence;

        if (result is VkResult.ErrorOutOfDateKhr or VkResult.SuboptimalKhr)
        {
            CreateSwapchain(_framebuffer.Width, _framebuffer.Height);
            return false;
        }
        else
        {
            VulkanUtil.CheckResult(result);
        }

        return true;
    }

    private unsafe void RecreateAndReacquire(uint width, uint height)
    {
        if (CreateSwapchain(width, height))
        {
            _ = AcquireNextImage(_gd.Device);
        }
    }

    public override void Resize(uint width, uint height) => RecreateAndReacquire(width, height);
}
