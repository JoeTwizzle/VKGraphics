using OpenTK.Graphics;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static VKGraphics.Vulkan.VulkanUtil;

namespace VKGraphics.Vulkan;

internal unsafe class VkGraphicsDevice : GraphicsDevice
{
    public override string DeviceName => deviceName;

    public override string VendorName => vendorName;

    public override GraphicsApiVersion ApiVersion => apiVersion;

    public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

    public override bool IsUvOriginTopLeft => true;

    public override bool IsDepthRangeZeroToOne => true;

    public override bool IsClipSpaceYInverted => !standardClipYDirection;

    //public override Swapchain MainSwapchain => mainSwapchain;

    public override GraphicsDeviceFeatures Features { get; }

    public VkInstance Instance => instance;
    public VkDevice Device => device;
    public VkPhysicalDevice PhysicalDevice { get; private set; }

    public VkPhysicalDeviceMemoryProperties PhysicalDeviceMemProperties => physicalDeviceMemProperties;
    public VkQueue GraphicsQueue => graphicsQueue;
    public uint GraphicsQueueIndex { get; private set; }

    public uint PresentQueueIndex { get; private set; }

    public string DriverName { get; private set; }

    public string DriverInfo { get; private set; }

    public VkDeviceMemoryManager MemoryManager { get; }

    public VkDescriptorPoolManager DescriptorPoolManager { get; private set; }

    public VkCmdDebugMarkerBeginExtT MarkerBegin { get; private set; }

    public VkCmdDebugMarkerEndExtT MarkerEnd { get; private set; }

    public VkCmdDebugMarkerInsertExtT MarkerInsert { get; private set; }

    public VkGetBufferMemoryRequirements2T GetBufferMemoryRequirements2 { get; private set; }

    public VkGetImageMemoryRequirements2T GetImageMemoryRequirements2 { get; private set; }

    public VkCreateMetalSurfaceExtT CreateMetalSurfaceExt { get; private set; }

    public override ResourceFactory ResourceFactory { get; }
    private static readonly FixedUtf8String s_name = "Veldrid-VkGraphicsDevice";
    private static readonly Lazy<bool> s_is_supported = new Lazy<bool>(checkIsSupported, true);
    private readonly object graphicsCommandPoolLock = new object();
    internal readonly object graphicsQueueLock = new object();
    private readonly ConcurrentDictionary<VkFormat, VkFilter> filters = new ConcurrentDictionary<VkFormat, VkFilter>();
    private readonly BackendInfoVulkan vulkanInfo;

    private const int shared_command_pool_count = 4;

    // Staging Resources
    private const uint min_staging_buffer_size = 64;
    private const uint max_staging_buffer_size = 512;

    private readonly object stagingResourcesLock = new object();
    private readonly List<VkTexture> availableStagingTextures = new List<VkTexture>();
    private readonly List<VkBuffer> availableStagingBuffers = new List<VkBuffer>();

    private readonly Dictionary<VkCommandBuffer, VkTexture> submittedStagingTextures
        = new Dictionary<VkCommandBuffer, VkTexture>();

    private readonly Dictionary<VkCommandBuffer, VkBuffer> submittedStagingBuffers
        = new Dictionary<VkCommandBuffer, VkBuffer>();

    private readonly Dictionary<VkCommandBuffer, SharedCommandPool> submittedSharedCommandPools
        = new Dictionary<VkCommandBuffer, SharedCommandPool>();

    private readonly object submittedFencesLock = new object();
    private readonly ConcurrentQueue<OpenTK.Graphics.Vulkan.VkFence> availableSubmissionFences = new();
    private readonly List<FenceSubmissionInfo> submittedFences = new List<FenceSubmissionInfo>();
    //private readonly VkSwapchain mainSwapchain;

    private readonly List<FixedUtf8String> surfaceExtensions = new List<FixedUtf8String>();

    private VkInstance instance;
    private string deviceName;
    private string vendorName;
    private GraphicsApiVersion apiVersion;
    private VkPhysicalDeviceProperties physicalDeviceProperties;
    private VkPhysicalDeviceFeatures physicalDeviceFeatures;
    private VkPhysicalDeviceMemoryProperties physicalDeviceMemProperties;
    private VkDevice device;
    private VkCommandPool graphicsCommandPool;
    private VkQueue graphicsQueue;
    private VkDebugReportCallbackEXT debugCallbackHandle;
    private PFN_vkDebugReportCallbackEXT debugCallbackFunc;
    private bool debugMarkerEnabled;
    private VkDebugMarkerSetObjectNameExtT setObjectNameDelegate;
    private readonly Stack<SharedCommandPool> sharedGraphicsCommandPools = new Stack<SharedCommandPool>();
    private bool standardValidationSupported;
    private bool khronosValidationSupported;
    private bool standardClipYDirection;
    private VkGetPhysicalDeviceProperties2T getPhysicalDeviceProperties2;

    public VkGraphicsDevice(GraphicsDeviceOptions options) : this(options, new())
    {
    }

    public VkGraphicsDevice(GraphicsDeviceOptions options, VulkanDeviceOptions vkOptions)
    {
        createInstance(options.Debug, vkOptions);

        var surface = VkSurfaceKHR.Zero;

        createPhysicalDevice();
        createLogicalDevice(surface, options.PreferStandardClipSpaceYDirection, vkOptions);

        MemoryManager = new VkDeviceMemoryManager(
            device,
            physicalDeviceProperties.limits.bufferImageGranularity,
            GetBufferMemoryRequirements2,
            GetImageMemoryRequirements2);

        Features = new GraphicsDeviceFeatures(
            true,
            physicalDeviceFeatures.geometryShader != 0,
            physicalDeviceFeatures.tessellationShader != 0,
            physicalDeviceFeatures.multiViewport != 0,
            true,
            true,
            true,
            true,
            physicalDeviceFeatures.drawIndirectFirstInstance != 0,
            physicalDeviceFeatures.fillModeNonSolid != 0,
            physicalDeviceFeatures.samplerAnisotropy != 0,
            physicalDeviceFeatures.depthClamp != 0,
            true,
            physicalDeviceFeatures.independentBlend != 0,
            true,
            true,
            debugMarkerEnabled,
            true,
            physicalDeviceFeatures.shaderFloat64 != 0);

        ResourceFactory = new VkResourceFactory(this);

        createDescriptorPool();
        createGraphicsCommandPool();
        for (int i = 0; i < shared_command_pool_count; i++)
        {
            sharedGraphicsCommandPools.Push(new SharedCommandPool(this, true));
        }

        vulkanInfo = new BackendInfoVulkan(this);

        PostDeviceCreated();
    }

    public override bool GetVulkanInfo(out BackendInfoVulkan info)
    {
        info = vulkanInfo;
        return true;
    }

    public bool HasSurfaceExtension(FixedUtf8String extension)
    {
        return surfaceExtensions.Contains(extension);
    }

    public void EnableDebugCallback(VkDebugReportFlagBitsEXT flags = VkDebugReportFlagBitsEXT.DebugReportWarningBitExt | VkDebugReportFlagBitsEXT.DebugReportErrorBitExt)
    {
        Debug.WriteLine("Enabling Vulkan Debug callbacks.");
        debugCallbackFunc = debugCallback;
        var debugCallbackCi = new VkDebugReportCallbackCreateInfoEXT();
        debugCallbackCi.flags = flags;
        var callbackPtr = Marshal.GetFunctionPointerForDelegate(debugCallbackFunc);
        debugCallbackCi.pfnCallback = (delegate* unmanaged[Cdecl]<VkDebugReportFlagBitsEXT, VkDebugReportObjectTypeEXT, ulong, nuint, int, byte*, byte*, void*, int>)callbackPtr;
        IntPtr createFnPtr;
        using (FixedUtf8String debugExtFnName = "Vk.CreateDebugReportCallbackEXT")
        {
            createFnPtr = Vk.GetInstanceProcAddr(instance, debugExtFnName);
        }

        if (createFnPtr == IntPtr.Zero)
        {
            return;
        }

        var createDelegate = Marshal.GetDelegateForFunctionPointer<VkCreateDebugReportCallbackExtD>(createFnPtr);
        var result = createDelegate(instance, &debugCallbackCi, IntPtr.Zero, out debugCallbackHandle);
        CheckResult(result);
    }

    public VkExtensionProperties[] GetDeviceExtensionProperties()
    {
        uint propertyCount = 0;
        var result = Vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &propertyCount, null);
        CheckResult(result);
        var props = new VkExtensionProperties[(int)propertyCount];

        fixed (VkExtensionProperties* properties = props)
        {
            result = Vk.EnumerateDeviceExtensionProperties(PhysicalDevice, (byte*)null, &propertyCount, properties);
            CheckResult(result);
        }

        return props;
    }

    public override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
    {
        var usageFlags = VkImageUsageFlagBits.ImageUsageSampledBit;
        usageFlags |= depthFormat ? VkImageUsageFlagBits.ImageUsageDepthStencilAttachmentBit : VkImageUsageFlagBits.ImageUsageColorAttachmentBit;
        VkImageFormatProperties formatProperties;
        Vk.GetPhysicalDeviceImageFormatProperties(
            PhysicalDevice,
            VkFormats.VdToVkPixelFormat(format),
            VkImageType.ImageType2d,
            VkImageTiling.ImageTilingOptimal,
            usageFlags,
            (VkImageCreateFlagBits)0,
            &formatProperties);

        var vkSampleCounts = formatProperties.sampleCounts;
        if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount32Bit) == VkSampleCountFlagBits.SampleCount32Bit)
        {
            return TextureSampleCount.Count32;
        }

        if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount16Bit) == VkSampleCountFlagBits.SampleCount16Bit)
        {
            return TextureSampleCount.Count16;
        }

        if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount8Bit) == VkSampleCountFlagBits.SampleCount8Bit)
        {
            return TextureSampleCount.Count8;
        }

        if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount4Bit) == VkSampleCountFlagBits.SampleCount4Bit)
        {
            return TextureSampleCount.Count4;
        }

        if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount2Bit) == VkSampleCountFlagBits.SampleCount2Bit)
        {
            return TextureSampleCount.Count2;
        }

        return TextureSampleCount.Count1;
    }

    public override void ResetFence(Fence fence)
    {
        var vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
        Vk.ResetFences(device, 1, &vkFence);
    }

    public override bool WaitForFence(Fence fence, ulong nanosecondTimeout)
    {
        var vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
        var result = Vk.WaitForFences(device, 1, &vkFence, 1, nanosecondTimeout);
        return result == VkResult.Success;
    }

    public override bool WaitForFences(Fence[] fences, bool waitAll, ulong nanosecondTimeout)
    {
        int fenceCount = fences.Length;
        var fencesPtr = stackalloc OpenTK.Graphics.Vulkan.VkFence[fenceCount];
        for (int i = 0; i < fenceCount; i++)
        {
            fencesPtr[i] = ((VkFence)fences[i]).DeviceFence;
        }

        var result = Vk.WaitForFences(device, (uint)fenceCount, fencesPtr, waitAll ? 1 : 0, nanosecondTimeout);
        return result == VkResult.Success;
    }

    internal static bool IsSupported()
    {
        return s_is_supported.Value;
    }

    internal void SetResourceName(IDeviceResource resource, string? name)
    {
        if (debugMarkerEnabled)
        {
            switch (resource)
            {
                case VkBuffer buffer:
                    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeBufferExt, buffer.DeviceBuffer.Handle, name);
                    break;

                case VkCommandList commandList:
                    setDebugMarkerName(
                        VkDebugReportObjectTypeEXT.DebugReportObjectTypeCommandBufferExt,
                        (ulong)commandList.CommandBuffer.Handle,
                        $"{name}_CommandBuffer");
                    setDebugMarkerName(
                        VkDebugReportObjectTypeEXT.DebugReportObjectTypeCommandPoolExt,
                        commandList.CommandPool.Handle,
                        $"{name}_CommandPool");
                    break;

                case VkFramebuffer framebuffer:
                    setDebugMarkerName(
                        VkDebugReportObjectTypeEXT.DebugReportObjectTypeFramebufferExt,
                        framebuffer.CurrentFramebuffer.Handle,
                        name);
                    break;

                case VkPipeline pipeline:
                    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypePipelineExt, pipeline.DevicePipeline.Handle, name);
                    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypePipelineLayoutExt, pipeline.PipelineLayout.Handle, name);
                    break;

                case VkResourceLayout resourceLayout:
                    setDebugMarkerName(
                        VkDebugReportObjectTypeEXT.DebugReportObjectTypeDescriptorSetLayoutExt,
                        resourceLayout.DescriptorSetLayout.Handle,
                        name);
                    break;

                case VkResourceSet resourceSet:
                    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeDescriptorSetExt, resourceSet.DescriptorSet.Handle, name);
                    break;

                case VkSampler sampler:
                    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeSamplerExt, sampler.DeviceSampler.Handle, name);
                    break;

                case VkShader shader:
                    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeShaderModuleExt, shader.ShaderModule.Handle, name);
                    break;

                case VkTexture tex:
                    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeImageExt, tex.OptimalDeviceImage.Handle, name);
                    break;

                case VkTextureView texView:
                    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeImageViewExt, texView.ImageView.Handle, name);
                    break;

                case VkFence fence:
                    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeFenceExt, fence.DeviceFence.Handle, name);
                    break;

                //case VkSwapchain sc:
                //    setDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeSwapchainKhrExt, sc.DeviceSwapchain.Handle, name);
                //    break;
            }
        }
    }

    internal VkFilter GetFormatFilter(VkFormat format)
    {
        if (!filters.TryGetValue(format, out var filter))
        {
            VkFormatProperties vkFormatProps;
            Vk.GetPhysicalDeviceFormatProperties(PhysicalDevice, format, &vkFormatProps);
            filter = (vkFormatProps.optimalTilingFeatures & VkFormatFeatureFlagBits.FormatFeatureSampledImageFilterLinearBit) != 0
                ? VkFilter.FilterLinear
                : VkFilter.FilterNearest;
            filters.TryAdd(format, filter);
        }

        return filter;
    }

    internal void ClearColorTexture(VkTexture texture, VkClearColorValue color)
    {
        uint effectiveLayers = texture.ArrayLayers;
        if ((texture.Usage & TextureUsage.Cubemap) != 0)
        {
            effectiveLayers *= 6;
        }

        var range = new VkImageSubresourceRange(
            VkImageAspectFlagBits.ImageAspectColorBit,
            0,
            texture.MipLevels,
            0,
            effectiveLayers);
        var pool = getFreeCommandPool();
        var cb = pool.BeginNewCommandBuffer();
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VkImageLayout.ImageLayoutTransferDstOptimal);
        Vk.CmdClearColorImage(cb, texture.OptimalDeviceImage, VkImageLayout.ImageLayoutTransferDstOptimal, &color, 1, &range);
        var colorLayout = texture.IsSwapchainTexture ? VkImageLayout.ImageLayoutPresentSrcKhr : VkImageLayout.ImageLayoutColorAttachmentOptimal;
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, colorLayout);
        pool.EndAndSubmit(cb);
    }

    internal void ClearDepthTexture(VkTexture texture, VkClearDepthStencilValue clearValue)
    {
        uint effectiveLayers = texture.ArrayLayers;
        if ((texture.Usage & TextureUsage.Cubemap) != 0)
        {
            effectiveLayers *= 6;
        }

        var aspect = FormatHelpers.IsStencilFormat(texture.Format)
            ? VkImageAspectFlagBits.ImageAspectDepthBit | VkImageAspectFlagBits.ImageAspectStencilBit
            : VkImageAspectFlagBits.ImageAspectDepthBit;
        var range = new VkImageSubresourceRange(
            aspect,
            0,
            texture.MipLevels,
            0,
            effectiveLayers);
        var pool = getFreeCommandPool();
        var cb = pool.BeginNewCommandBuffer();
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VkImageLayout.ImageLayoutTransferDstOptimal);
        Vk.CmdClearDepthStencilImage(
            cb,
            texture.OptimalDeviceImage,
            VkImageLayout.ImageLayoutTransferDstOptimal,
            &clearValue,
            1,
            &range);
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, effectiveLayers, VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal);
        pool.EndAndSubmit(cb);
    }

    internal override uint GetUniformBufferMinOffsetAlignmentCore()
    {
        return (uint)physicalDeviceProperties.limits.minUniformBufferOffsetAlignment;
    }

    internal override uint GetStructuredBufferMinOffsetAlignmentCore()
    {
        return (uint)physicalDeviceProperties.limits.minStorageBufferOffsetAlignment;
    }

    internal void TransitionImageLayout(VkTexture texture, VkImageLayout layout)
    {
        var pool = getFreeCommandPool();
        var cb = pool.BeginNewCommandBuffer();
        texture.TransitionImageLayout(cb, 0, texture.MipLevels, 0, texture.ActualArrayLayers, layout);
        pool.EndAndSubmit(cb);
    }

    protected override MappedResource MapCore(IMappableResource resource, MapMode mode, uint subresource)
    {
        VkMemoryBlock memoryBlock;
        IntPtr mappedPtr = IntPtr.Zero;
        uint sizeInBytes;
        uint offset = 0;
        uint rowPitch = 0;
        uint depthPitch = 0;

        if (resource is VkBuffer buffer)
        {
            memoryBlock = buffer.Memory;
            sizeInBytes = buffer.SizeInBytes;
        }
        else
        {
            var texture = Util.AssertSubtype<IMappableResource, VkTexture>(resource);
            var layout = texture.GetSubresourceLayout(subresource);
            memoryBlock = texture.Memory;
            sizeInBytes = (uint)layout.size;
            offset = (uint)layout.offset;
            rowPitch = (uint)layout.rowPitch;
            depthPitch = (uint)layout.depthPitch;
        }

        if (memoryBlock.DeviceMemory.Handle != 0)
        {
            if (memoryBlock.IsPersistentMapped)
            {
                mappedPtr = (IntPtr)memoryBlock.BlockMappedPointer;
            }
            else
            {
                mappedPtr = MemoryManager.Map(memoryBlock);
            }
        }

        byte* dataPtr = (byte*)mappedPtr.ToPointer() + offset;
        return new MappedResource(
            resource,
            mode,
            (IntPtr)dataPtr,
            sizeInBytes,
            subresource,
            rowPitch,
            depthPitch);
    }

    protected override void UnmapCore(IMappableResource resource, uint subresource)
    {
        VkMemoryBlock memoryBlock;

        if (resource is VkBuffer buffer)
        {
            memoryBlock = buffer.Memory;
        }
        else
        {
            var tex = Util.AssertSubtype<IMappableResource, VkTexture>(resource);
            memoryBlock = tex.Memory;
        }

        if (memoryBlock.DeviceMemory.Handle != 0 && !memoryBlock.IsPersistentMapped)
        {
            Vk.UnmapMemory(device, memoryBlock.DeviceMemory);
        }
    }

    protected override void PlatformDispose()
    {
        Debug.Assert(submittedFences.Count == 0);
        foreach (var fence in availableSubmissionFences)
        {
            Vk.DestroyFence(device, fence, null);
        }

        //mainSwapchain?.Dispose();

        if (debugCallbackFunc != null)
        {
            debugCallbackFunc = null;
            FixedUtf8String debugExtFnName = "Vk.DestroyDebugReportCallbackEXT";
            IntPtr destroyFuncPtr = Vk.GetInstanceProcAddr(instance, debugExtFnName);
            var destroyDel
                = Marshal.GetDelegateForFunctionPointer<VkDestroyDebugReportCallbackExtD>(destroyFuncPtr);
            destroyDel(instance, debugCallbackHandle, null);
        }

        DescriptorPoolManager.DestroyAll();
        Vk.DestroyCommandPool(device, graphicsCommandPool, null);

        Debug.Assert(submittedStagingTextures.Count == 0);
        foreach (var tex in availableStagingTextures)
        {
            tex.Dispose();
        }

        Debug.Assert(submittedStagingBuffers.Count == 0);
        foreach (var buffer in availableStagingBuffers)
        {
            buffer.Dispose();
        }

        lock (graphicsCommandPoolLock)
        {
            while (sharedGraphicsCommandPools.Count > 0)
            {
                var sharedPool = sharedGraphicsCommandPools.Pop();
                sharedPool.Destroy();
            }
        }

        MemoryManager.Dispose();

        var result = Vk.DeviceWaitIdle(device);
        CheckResult(result);
        Vk.DestroyDevice(device, null);
        Vk.DestroyInstance(instance, null);
    }

    private static bool checkIsSupported()
    {
        if (!IsVulkanLoaded())
        {
            return false;
        }

        var instanceCi = new VkInstanceCreateInfo();
        var applicationInfo = new VkApplicationInfo
        {
            apiVersion = new VkVersion(1, 0, 0),
            applicationVersion = new VkVersion(1, 0, 0),
            engineVersion = new VkVersion(1, 0, 0),
            pApplicationName = s_name,
            pEngineName = s_name
        };

        instanceCi.pApplicationInfo = &applicationInfo;
        VkInstance testInstance;
        var result = Vk.CreateInstance(&instanceCi, null, &testInstance);
        if (result != VkResult.Success)
        {
            return false;
        }

        uint physicalDeviceCount = 0;
        result = Vk.EnumeratePhysicalDevices(testInstance, &physicalDeviceCount, null);

        if (result != VkResult.Success || physicalDeviceCount == 0)
        {
            Vk.DestroyInstance(testInstance, null);
            return false;
        }

        Vk.DestroyInstance(testInstance, null);

        var instanceExtensions = new HashSet<string>(GetInstanceExtensions());
        if (!instanceExtensions.Contains(CommonStrings.VkKhrSurfaceExtensionName))
        {
            return false;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return instanceExtensions.Contains(CommonStrings.VkKhrWin32SurfaceExtensionName);
        }
#if NET5_0_OR_GREATER

        if (OperatingSystem.IsAndroid())
        {
            return instanceExtensions.Contains(CommonStrings.VkKhrAndroidSurfaceExtensionName);
        }
#endif

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (RuntimeInformation.OSDescription.Contains("Unix")) // Android
            {
                return instanceExtensions.Contains(CommonStrings.VkKhrAndroidSurfaceExtensionName);
            }

            return instanceExtensions.Contains(CommonStrings.VkKhrXlibSurfaceExtensionName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (RuntimeInformation.OSDescription.Contains("Darwin")) // macOS
            {
                return instanceExtensions.Contains(CommonStrings.VkMvkMacosSurfaceExtensionName);
            }
            // iOS
            return instanceExtensions.Contains(CommonStrings.VkMvkIOSSurfaceExtensionName);
        }

        return false;
    }

    private void submitCommandList(
        CommandList cl,
        uint waitSemaphoreCount,
        VkSemaphore* waitSemaphoresPtr,
        uint signalSemaphoreCount,
        VkSemaphore* signalSemaphoresPtr,
        Fence fence)
    {
        var vkCl = Util.AssertSubtype<CommandList, VkCommandList>(cl);
        var vkCb = vkCl.CommandBuffer;

        vkCl.CommandBufferSubmitted(vkCb);
        submitCommandBuffer(vkCl, vkCb, waitSemaphoreCount, waitSemaphoresPtr, signalSemaphoreCount, signalSemaphoresPtr, fence);
    }

    private void submitCommandBuffer(
        VkCommandList vkCl,
        VkCommandBuffer vkCb,
        uint waitSemaphoreCount,
        VkSemaphore* waitSemaphoresPtr,
        uint signalSemaphoreCount,
        VkSemaphore* signalSemaphoresPtr,
        Fence fence)
    {
        checkSubmittedFences();

        bool useExtraFence = fence != null;
        var si = new VkSubmitInfo();
        si.commandBufferCount = 1;
        si.pCommandBuffers = &vkCb;
        var waitDstStageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
        si.pWaitDstStageMask = &waitDstStageMask;

        si.pWaitSemaphores = waitSemaphoresPtr;
        si.waitSemaphoreCount = waitSemaphoreCount;
        si.pSignalSemaphores = signalSemaphoresPtr;
        si.signalSemaphoreCount = signalSemaphoreCount;

        OpenTK.Graphics.Vulkan.VkFence vkFence;
        OpenTK.Graphics.Vulkan.VkFence submissionFence;

        if (useExtraFence)
        {
            vkFence = Util.AssertSubtype<Fence, VkFence>(fence).DeviceFence;
            submissionFence = getFreeSubmissionFence();
        }
        else
        {
            vkFence = getFreeSubmissionFence();
            submissionFence = vkFence;
        }

        lock (graphicsQueueLock)
        {
            var result = Vk.QueueSubmit(graphicsQueue, 1, &si, vkFence);
            CheckResult(result);

            if (useExtraFence)
            {
                result = Vk.QueueSubmit(graphicsQueue, 0, null, submissionFence);
                CheckResult(result);
            }
        }

        lock (submittedFencesLock)
        {
            submittedFences.Add(new FenceSubmissionInfo(submissionFence, vkCl, vkCb));
        }
    }

    private void checkSubmittedFences()
    {
        lock (submittedFencesLock)
        {
            for (int i = 0; i < submittedFences.Count; i++)
            {
                var fsi = submittedFences[i];

                if (Vk.GetFenceStatus(device, fsi.Fence) == VkResult.Success)
                {
                    completeFenceSubmission(fsi);
                    submittedFences.RemoveAt(i);
                    i -= 1;
                }
                else
                {
                    break; // Submissions are in order; later submissions cannot complete if this one hasn't.
                }
            }
        }
    }

    private void completeFenceSubmission(FenceSubmissionInfo fsi)
    {
        var fence = fsi.Fence;
        var completedCb = fsi.CommandBuffer;
        fsi.CommandList?.CommandBufferCompleted(completedCb);
        var resetResult = Vk.ResetFences(device, 1, &fence);
        CheckResult(resetResult);
        returnSubmissionFence(fence);

        lock (stagingResourcesLock)
        {
            if (submittedStagingTextures.Remove(completedCb, out var stagingTex))
            {
                availableStagingTextures.Add(stagingTex);
            }

            if (submittedStagingBuffers.Remove(completedCb, out var stagingBuffer))
            {
                if (stagingBuffer.SizeInBytes <= max_staging_buffer_size)
                {
                    availableStagingBuffers.Add(stagingBuffer);
                }
                else
                {
                    stagingBuffer.Dispose();
                }
            }

            if (submittedSharedCommandPools.Remove(completedCb, out var sharedPool))
            {
                lock (graphicsCommandPoolLock)
                {
                    if (sharedPool.IsCached)
                    {
                        sharedGraphicsCommandPools.Push(sharedPool);
                    }
                    else
                    {
                        sharedPool.Destroy();
                    }
                }
            }
        }
    }

    private void returnSubmissionFence(OpenTK.Graphics.Vulkan.VkFence fence)
    {
        availableSubmissionFences.Enqueue(fence);
    }

    private OpenTK.Graphics.Vulkan.VkFence getFreeSubmissionFence()
    {
        if (availableSubmissionFences.TryDequeue(out var availableFence))
        {
            return availableFence;
        }

        var fenceCi = new VkFenceCreateInfo();
        OpenTK.Graphics.Vulkan.VkFence newFence;
        var result = Vk.CreateFence(device, &fenceCi, null, &newFence);
        CheckResult(result);
        return newFence;
    }

    private void setDebugMarkerName(VkDebugReportObjectTypeEXT type, ulong target, string name)
    {
        Debug.Assert(setObjectNameDelegate != null);

        var nameInfo = new VkDebugMarkerObjectNameInfoEXT();
        nameInfo.objectType = type;
        nameInfo.obj = target;

        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];
        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }

        utf8Ptr[byteCount] = 0;

        nameInfo.pObjectName = utf8Ptr;
        var result = setObjectNameDelegate(device, &nameInfo);
        CheckResult(result);
    }

    private void createInstance(bool debug, VulkanDeviceOptions options)
    {
        var availableInstanceLayers = new HashSet<string>(EnumerateInstanceLayers());
        var availableInstanceExtensions = new HashSet<string>(GetInstanceExtensions());

        var instanceCi = new VkInstanceCreateInfo();
        var applicationInfo = new VkApplicationInfo
        {
            apiVersion = new VkVersion(1, 0, 0),
            applicationVersion = new VkVersion(1, 0, 0),
            engineVersion = new VkVersion(1, 0, 0),
            pApplicationName = s_name,
            pEngineName = s_name
        };

        instanceCi.pApplicationInfo = &applicationInfo;

        var instanceExtensions = new StackList<IntPtr, Size64Bytes>();
        var instanceLayers = new StackList<IntPtr, Size64Bytes>();

        if (availableInstanceExtensions.Contains(CommonStrings.VkKhrPortabilitySubset))
        {
            surfaceExtensions.Add(CommonStrings.VkKhrPortabilitySubset);
        }

        if (availableInstanceExtensions.Contains(CommonStrings.VkKhrSurfaceExtensionName))
        {
            surfaceExtensions.Add(CommonStrings.VkKhrSurfaceExtensionName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (availableInstanceExtensions.Contains(CommonStrings.VkKhrWin32SurfaceExtensionName))
            {
                surfaceExtensions.Add(CommonStrings.VkKhrWin32SurfaceExtensionName);
            }
        }
        else if (
#if NET5_0_OR_GREATER
            OperatingSystem.IsAndroid() ||
#endif
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (availableInstanceExtensions.Contains(CommonStrings.VkKhrAndroidSurfaceExtensionName))
            {
                surfaceExtensions.Add(CommonStrings.VkKhrAndroidSurfaceExtensionName);
            }

            if (availableInstanceExtensions.Contains(CommonStrings.VkKhrXlibSurfaceExtensionName))
            {
                surfaceExtensions.Add(CommonStrings.VkKhrXlibSurfaceExtensionName);
            }

            if (availableInstanceExtensions.Contains(CommonStrings.VkKhrWaylandSurfaceExtensionName))
            {
                surfaceExtensions.Add(CommonStrings.VkKhrWaylandSurfaceExtensionName);
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            if (availableInstanceExtensions.Contains(CommonStrings.VkExtMetalSurfaceExtensionName))
            {
                surfaceExtensions.Add(CommonStrings.VkExtMetalSurfaceExtensionName);
            }
            else // Legacy MoltenVK extensions
            {
                if (availableInstanceExtensions.Contains(CommonStrings.VkMvkMacosSurfaceExtensionName))
                {
                    surfaceExtensions.Add(CommonStrings.VkMvkMacosSurfaceExtensionName);
                }

                if (availableInstanceExtensions.Contains(CommonStrings.VkMvkIOSSurfaceExtensionName))
                {
                    surfaceExtensions.Add(CommonStrings.VkMvkIOSSurfaceExtensionName);
                }
            }
        }

        foreach (var ext in surfaceExtensions)
        {
            instanceExtensions.Add(ext);
        }

        bool hasDeviceProperties2 = availableInstanceExtensions.Contains(CommonStrings.VkKhrGetPhysicalDeviceProperties2);
        if (hasDeviceProperties2)
        {
            instanceExtensions.Add(CommonStrings.VkKhrGetPhysicalDeviceProperties2);
        }

        string[] requestedInstanceExtensions = options.InstanceExtensions ?? Array.Empty<string>();
        var tempStrings = new List<FixedUtf8String>();

        foreach (string requiredExt in requestedInstanceExtensions)
        {
            if (!availableInstanceExtensions.Contains(requiredExt))
            {
                throw new VeldridException($"The required instance extension was not available: {requiredExt}");
            }

            var utf8Str = new FixedUtf8String(requiredExt);
            instanceExtensions.Add(utf8Str);
            tempStrings.Add(utf8Str);
        }

        bool debugReportExtensionAvailable = false;

        if (debug)
        {
            if (availableInstanceExtensions.Contains(CommonStrings.VkExtDebugReportExtensionName))
            {
                debugReportExtensionAvailable = true;
                instanceExtensions.Add(CommonStrings.VkExtDebugReportExtensionName);
            }

            if (availableInstanceLayers.Contains(CommonStrings.StandardValidationLayerName))
            {
                standardValidationSupported = true;
                instanceLayers.Add(CommonStrings.StandardValidationLayerName);
            }

            if (availableInstanceLayers.Contains(CommonStrings.KhronosValidationLayerName))
            {
                khronosValidationSupported = true;
                instanceLayers.Add(CommonStrings.KhronosValidationLayerName);
            }
        }

        instanceCi.enabledExtensionCount = instanceExtensions.Count;
        instanceCi.ppEnabledExtensionNames = (byte**)instanceExtensions.Data;

        instanceCi.enabledLayerCount = instanceLayers.Count;
        if (instanceLayers.Count > 0)
        {
            instanceCi.ppEnabledLayerNames = (byte**)instanceLayers.Data;
        }

        VkInstance vkInstance;
        var result = Vk.CreateInstance(&instanceCi, null, &vkInstance);
        instance = vkInstance;
        VKLoader.SetInstance(vkInstance);
        CheckResult(result);

        if (HasSurfaceExtension(CommonStrings.VkExtMetalSurfaceExtensionName))
        {
            CreateMetalSurfaceExt = getInstanceProcAddr<VkCreateMetalSurfaceExtT>("Vk.CreateMetalSurfaceEXT");
        }

        if (debug && debugReportExtensionAvailable)
        {
            EnableDebugCallback();
        }

        if (hasDeviceProperties2)
        {
            getPhysicalDeviceProperties2 = getInstanceProcAddr<VkGetPhysicalDeviceProperties2T>("Vk.GetPhysicalDeviceProperties2")
                                           ?? getInstanceProcAddr<VkGetPhysicalDeviceProperties2T>("Vk.GetPhysicalDeviceProperties2KHR");
        }

        foreach (var tempStr in tempStrings)
        {
            tempStr.Dispose();
        }
    }

    private static uint debugCallback(
        uint flags,
        VkDebugReportObjectTypeEXT objectType,
        ulong @object,
        UIntPtr location,
        int messageCode,
        byte* pLayerPrefix,
        byte* pMessage,
        void* pUserData)
    {
        string message = Util.GetString(pMessage);
        var debugReportFlags = (VkDebugReportFlagBitsEXT)flags;

#if DEBUG
        if (Debugger.IsAttached)
        {
            Debugger.Break();
        }
#endif

        string fullMessage = $"[{debugReportFlags}] ({objectType}) {message}";

        if (debugReportFlags == VkDebugReportFlagBitsEXT.DebugReportErrorBitExt)
        {
            throw new VeldridException("A Vulkan validation error was encountered: " + fullMessage);
        }

        Console.WriteLine(fullMessage);
        return 0;
    }

    private void createPhysicalDevice()
    {
        uint deviceCount = 0;
        Vk.EnumeratePhysicalDevices(instance, &deviceCount, null);
        if (deviceCount == 0)
        {
            throw new InvalidOperationException("No physical devices exist.");
        }

        var physicalDevices = stackalloc VkPhysicalDevice[(int)deviceCount];
        Vk.EnumeratePhysicalDevices(instance, &deviceCount, physicalDevices);
        // Just use the first one.
        PhysicalDevice = physicalDevices[0];
        VkPhysicalDeviceProperties vkphysicalDeviceProperties;
        Vk.GetPhysicalDeviceProperties(PhysicalDevice, &vkphysicalDeviceProperties);
        physicalDeviceProperties = vkphysicalDeviceProperties;
        deviceName = Encoding.UTF8.GetString(MemoryMarshal.CreateReadOnlySpanFromNullTerminated(&vkphysicalDeviceProperties.deviceName[0]));

        vendorName = "id:" + physicalDeviceProperties.vendorID.ToString("x8");
        apiVersion = GraphicsApiVersion.Unknown;
        DriverInfo = "version:" + physicalDeviceProperties.driverVersion.ToString("x8");
        VkPhysicalDeviceFeatures vkphysicalDeviceFeatures;
        Vk.GetPhysicalDeviceFeatures(PhysicalDevice, &vkphysicalDeviceFeatures);
        physicalDeviceFeatures = vkphysicalDeviceFeatures;
        VkPhysicalDeviceMemoryProperties vkphysicalDeviceMemProperties;
        Vk.GetPhysicalDeviceMemoryProperties(PhysicalDevice, &vkphysicalDeviceMemProperties);
        physicalDeviceMemProperties = vkphysicalDeviceMemProperties;
    }

    private void createLogicalDevice(VkSurfaceKHR surface, bool preferStandardClipY, VulkanDeviceOptions options)
    {
        getQueueFamilyIndices(surface);

        var familyIndices = new HashSet<uint> { GraphicsQueueIndex, PresentQueueIndex };
        var queueCreateInfos = stackalloc VkDeviceQueueCreateInfo[familyIndices.Count];
        uint queueCreateInfosCount = (uint)familyIndices.Count;

        int i = 0;

        foreach (uint _ in familyIndices)
        {
            var queueCreateInfo = new VkDeviceQueueCreateInfo();
            queueCreateInfo.queueFamilyIndex = GraphicsQueueIndex;
            queueCreateInfo.queueCount = 1;
            float priority = 1f;
            queueCreateInfo.pQueuePriorities = &priority;
            queueCreateInfos[i] = queueCreateInfo;
            i += 1;
        }

        var deviceFeatures = physicalDeviceFeatures;

        var props = GetDeviceExtensionProperties();

        var requiredInstanceExtensions = new HashSet<string>(options.DeviceExtensions ?? Array.Empty<string>());

        bool hasMemReqs2 = false;
        bool hasDedicatedAllocation = false;
        bool hasDriverProperties = false;
        IntPtr[] activeExtensions = new IntPtr[props.Length];
        uint activeExtensionCount = 0;

        fixed (VkExtensionProperties* properties = props)
        {
            for (int property = 0; property < props.Length; property++)
            {
                string extensionName = Util.GetString(&properties[property].extensionName[0]);

                if (extensionName == "VK_EXT_debug_marker")
                {
                    activeExtensions[activeExtensionCount++] = CommonStrings.VkExtDebugMarkerExtensionName;
                    requiredInstanceExtensions.Remove(extensionName);
                    debugMarkerEnabled = true;
                }
                else if (extensionName == "VK_KHR_swapchain")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)(&properties[property].extensionName[0]);
                    requiredInstanceExtensions.Remove(extensionName);
                }
                else if (preferStandardClipY && extensionName == "VK_KHR_maintenance1")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)(&properties[property].extensionName[0]);
                    requiredInstanceExtensions.Remove(extensionName);
                    standardClipYDirection = true;
                }
                else if (extensionName == "VK_KHR_get_memory_requirements2")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)(&properties[property].extensionName[0]);
                    requiredInstanceExtensions.Remove(extensionName);
                    hasMemReqs2 = true;
                }
                else if (extensionName == "VK_KHR_dedicated_allocation")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)(&properties[property].extensionName[0]);
                    requiredInstanceExtensions.Remove(extensionName);
                    hasDedicatedAllocation = true;
                }
                else if (extensionName == "VK_KHR_driver_properties")
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)(&properties[property].extensionName[0]);
                    requiredInstanceExtensions.Remove(extensionName);
                    hasDriverProperties = true;
                }
                else if (extensionName == CommonStrings.VkKhrPortabilitySubset)
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)(&properties[property].extensionName[0]);
                    requiredInstanceExtensions.Remove(extensionName);
                }
                else if (requiredInstanceExtensions.Remove(extensionName))
                {
                    activeExtensions[activeExtensionCount++] = (IntPtr)(&properties[property].extensionName[0]);
                }
            }
        }

        if (requiredInstanceExtensions.Count != 0)
        {
            string missingList = string.Join(", ", requiredInstanceExtensions);
            throw new VeldridException(
                $"The following Vulkan device extensions were not available: {missingList}");
        }

        var deviceCreateInfo = new VkDeviceCreateInfo();
        deviceCreateInfo.queueCreateInfoCount = queueCreateInfosCount;
        deviceCreateInfo.pQueueCreateInfos = queueCreateInfos;

        deviceCreateInfo.pEnabledFeatures = &deviceFeatures;

        var layerNames = new StackList<IntPtr>();
        if (standardValidationSupported)
        {
            layerNames.Add(CommonStrings.StandardValidationLayerName);
        }

        if (khronosValidationSupported)
        {
            layerNames.Add(CommonStrings.KhronosValidationLayerName);
        }

        deviceCreateInfo.enabledLayerCount = layerNames.Count;
        deviceCreateInfo.ppEnabledLayerNames = (byte**)layerNames.Data;

        fixed (IntPtr* activeExtensionsPtr = activeExtensions)
        {
            deviceCreateInfo.enabledExtensionCount = activeExtensionCount;
            deviceCreateInfo.ppEnabledExtensionNames = (byte**)activeExtensionsPtr;
            VkDevice vkdevice;
            var result = Vk.CreateDevice(PhysicalDevice, &deviceCreateInfo, null, &vkdevice);
            device = vkdevice;
            CheckResult(result);
        }
        VkQueue vkgraphicsQueue;
        Vk.GetDeviceQueue(device, GraphicsQueueIndex, 0, &vkgraphicsQueue);
        graphicsQueue = vkgraphicsQueue;
        if (debugMarkerEnabled)
        {
            setObjectNameDelegate = Marshal.GetDelegateForFunctionPointer<VkDebugMarkerSetObjectNameExtT>(
                getInstanceProcAddr("Vk.DebugMarkerSetObjectNameEXT"));
            MarkerBegin = Marshal.GetDelegateForFunctionPointer<VkCmdDebugMarkerBeginExtT>(
                getInstanceProcAddr("Vk.CmdDebugMarkerBeginEXT"));
            MarkerEnd = Marshal.GetDelegateForFunctionPointer<VkCmdDebugMarkerEndExtT>(
                getInstanceProcAddr("Vk.CmdDebugMarkerEndEXT"));
            MarkerInsert = Marshal.GetDelegateForFunctionPointer<VkCmdDebugMarkerInsertExtT>(
                getInstanceProcAddr("Vk.CmdDebugMarkerInsertEXT"));
        }

        if (hasDedicatedAllocation && hasMemReqs2)
        {
            GetBufferMemoryRequirements2 = getDeviceProcAddr<VkGetBufferMemoryRequirements2T>("Vk.GetBufferMemoryRequirements2")
                                           ?? getDeviceProcAddr<VkGetBufferMemoryRequirements2T>("Vk.GetBufferMemoryRequirements2KHR");
            GetImageMemoryRequirements2 = getDeviceProcAddr<VkGetImageMemoryRequirements2T>("Vk.GetImageMemoryRequirements2")
                                          ?? getDeviceProcAddr<VkGetImageMemoryRequirements2T>("Vk.GetImageMemoryRequirements2KHR");
        }

        if (getPhysicalDeviceProperties2 != null && hasDriverProperties)
        {
            var deviceProps = new VkPhysicalDeviceProperties2();
            var driverProps = new VkPhysicalDeviceDriverProperties();
            deviceProps.pNext = &driverProps;
            getPhysicalDeviceProperties2(PhysicalDevice, &deviceProps);

            string driverName = Util.GetString(&driverProps.driverName[0]);

            string driverInfo = Util.GetString(&driverProps.driverInfo[0]);

            var conforming = driverProps.conformanceVersion;
            apiVersion = new GraphicsApiVersion(conforming.major, conforming.minor, conforming.subminor, conforming.patch);
            DriverName = driverName;
            DriverInfo = driverInfo;
        }
    }

    private IntPtr getInstanceProcAddr(string name)
    {
        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];

        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }

        utf8Ptr[byteCount] = 0;
        return Vk.GetInstanceProcAddr(instance, utf8Ptr);
    }

    private T getInstanceProcAddr<T>(string name)
    {
        IntPtr funcPtr = getInstanceProcAddr(name);
        if (funcPtr != IntPtr.Zero)
        {
            return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
        }

        return default;
    }

    private IntPtr getDeviceProcAddr(string name)
    {
        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];

        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }

        utf8Ptr[byteCount] = 0;

        return Vk.GetDeviceProcAddr(device, utf8Ptr);
    }

    private T getDeviceProcAddr<T>(string name)
    {
        IntPtr funcPtr = getDeviceProcAddr(name);
        if (funcPtr != IntPtr.Zero)
        {
            return Marshal.GetDelegateForFunctionPointer<T>(funcPtr);
        }

        return default;
    }

    private void getQueueFamilyIndices(VkSurfaceKHR surface)
    {
        uint queueFamilyCount = 0;
        Vk.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &queueFamilyCount, null);
        var qfp = stackalloc VkQueueFamilyProperties[(int)queueFamilyCount];
        Vk.GetPhysicalDeviceQueueFamilyProperties(PhysicalDevice, &queueFamilyCount, &qfp[0]);

        bool foundGraphics = false;
        bool foundPresent = surface == VkSurfaceKHR.Zero;

        for (uint i = 0; i < queueFamilyCount; i++)
        {
            //TODO: This queue also supports compute, we should check for it, or use a separate queue
            if ((qfp[i].queueFlags & VkQueueFlagBits.QueueGraphicsBit) != 0)
            {
                GraphicsQueueIndex = i;
                foundGraphics = true;
            }

            if (!foundPresent)
            {
                int presentSupported;
                Vk.GetPhysicalDeviceSurfaceSupportKHR(PhysicalDevice, i, surface, &presentSupported);

                if (presentSupported != 0)
                {
                    PresentQueueIndex = i;
                    foundPresent = true;
                }
            }

            if (foundGraphics && foundPresent)
            {
                return;
            }
        }
    }

    private void createDescriptorPool()
    {
        DescriptorPoolManager = new VkDescriptorPoolManager(this);
    }

    private void createGraphicsCommandPool()
    {
        var commandPoolCi = new VkCommandPoolCreateInfo();
        commandPoolCi.flags = VkCommandPoolCreateFlagBits.CommandPoolCreateResetCommandBufferBit;
        commandPoolCi.queueFamilyIndex = GraphicsQueueIndex;
        VkCommandPool vkgraphicsCommandPool;
        var result = Vk.CreateCommandPool(device, &commandPoolCi, null, &vkgraphicsCommandPool);
        graphicsCommandPool = vkgraphicsCommandPool;
        CheckResult(result);
    }

    private SharedCommandPool getFreeCommandPool()
    {
        SharedCommandPool? sharedPool = null;

        lock (graphicsCommandPoolLock)
        {
            if (sharedGraphicsCommandPools.Count > 0)
            {
                sharedPool = sharedGraphicsCommandPools.Pop();
            }
        }

        return sharedPool ?? new SharedCommandPool(this, false);
    }

    private IntPtr mapBuffer(VkBuffer buffer, uint numBytes)
    {
        if (buffer.Memory.IsPersistentMapped)
        {
            return (IntPtr)buffer.Memory.BlockMappedPointer;
        }

        void* mappedPtr;
        var result = Vk.MapMemory(Device, buffer.Memory.DeviceMemory, buffer.Memory.Offset, numBytes, 0, &mappedPtr);
        CheckResult(result);
        return (IntPtr)mappedPtr;
    }

    private void unmapBuffer(VkBuffer buffer)
    {
        if (!buffer.Memory.IsPersistentMapped)
        {
            Vk.UnmapMemory(Device, buffer.Memory.DeviceMemory);
        }
    }

    private VkTexture getFreeStagingTexture(uint width, uint height, uint depth, PixelFormat format)
    {
        uint totalSize = FormatHelpers.GetRegionSize(width, height, depth, format);

        lock (stagingResourcesLock)
        {
            for (int i = 0; i < availableStagingTextures.Count; i++)
            {
                var tex = availableStagingTextures[i];

                if (tex.Memory.Size >= totalSize)
                {
                    availableStagingTextures.RemoveAt(i);
                    tex.SetStagingDimensions(width, height, depth, format);
                    return tex;
                }
            }
        }

        uint texWidth = Math.Max(256, width);
        uint texHeight = Math.Max(256, height);
        var newTex = (VkTexture)ResourceFactory.CreateTexture(TextureDescription.Texture3D(
            texWidth, texHeight, depth, 1, format, TextureUsage.Staging));
        newTex.SetStagingDimensions(width, height, depth, format);

        return newTex;
    }

    private VkBuffer getFreeStagingBuffer(uint size)
    {
        lock (stagingResourcesLock)
        {
            for (int i = 0; i < availableStagingBuffers.Count; i++)
            {
                var buffer = availableStagingBuffers[i];

                if (buffer.SizeInBytes >= size)
                {
                    availableStagingBuffers.RemoveAt(i);
                    return buffer;
                }
            }
        }

        uint newBufferSize = Math.Max(min_staging_buffer_size, size);
        var newBuffer = (VkBuffer)ResourceFactory.CreateBuffer(
            new BufferDescription(newBufferSize, BufferUsage.Staging));
        return newBuffer;
    }

    private protected override void SubmitCommandsCore(CommandList cl, Fence fence)
    {
        submitCommandList(cl, 0, null, 0, null, fence);
    }

    //private protected override void SwapBuffersCore(Swapchain swapchain)
    //{
    //    var vkSc = Util.AssertSubtype<Swapchain, VkSwapchain>(swapchain);
    //    var deviceSwapchain = vkSc.DeviceSwapchain;
    //    var presentInfo = new VkPresentInfoKHR();
    //    presentInfo.swapchainCount = 1;
    //    presentInfo.pSwapchains = &deviceSwapchain;
    //    uint imageIndex = vkSc.ImageIndex;
    //    presentInfo.pImageIndices = &imageIndex;

    //    object presentLock = vkSc.PresentQueueIndex == GraphicsQueueIndex ? graphicsQueueLock : vkSc;

    //    lock (presentLock)
    //    {
    //        Vk.QueuePresentKHR(vkSc.PresentQueue, &presentInfo);

    //        if (vkSc.AcquireNextImage(device, VkSemaphore.Zero, vkSc.ImageAvailableFence))
    //        {
    //            var fence = vkSc.ImageAvailableFence;
    //            Vk.WaitForFences(device, 1, &fence, 1, ulong.MaxValue);
    //            Vk.ResetFences(device, 1, &fence);
    //        }
    //    }
    //}

    private protected override void WaitForIdleCore()
    {
        lock (graphicsQueueLock)
        {
            Vk.QueueWaitIdle(graphicsQueue);
        }

        checkSubmittedFences();
    }

    private protected override void WaitForNextFrameReadyCore()
    {
    }

    private protected override bool GetPixelFormatSupportCore(
        PixelFormat format,
        TextureType type,
        TextureUsage usage,
        out PixelFormatProperties properties)
    {
        var vkFormat = VkFormats.VdToVkPixelFormat(format, (usage & TextureUsage.DepthStencil) != 0);
        var vkType = VkFormats.VdToVkTextureType(type);
        var tiling = usage == TextureUsage.Staging ? VkImageTiling.ImageTilingLinear : VkImageTiling.ImageTilingOptimal;
        var vkUsage = VkFormats.VdToVkTextureUsage(usage);
        VkImageFormatProperties vkProps;
        var result = Vk.GetPhysicalDeviceImageFormatProperties(
            PhysicalDevice,
            vkFormat,
            vkType,
            tiling,
            vkUsage,
            0,
            &vkProps);

        if (result == VkResult.ErrorFormatNotSupported)
        {
            properties = default;
            return false;
        }

        CheckResult(result);

        properties = new PixelFormatProperties(
            vkProps.maxExtent.width,
            vkProps.maxExtent.height,
            vkProps.maxExtent.depth,
            vkProps.maxMipLevels,
            vkProps.maxArrayLayers,
            (uint)vkProps.sampleCounts);
        return true;
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
        VkBuffer copySrcVkBuffer = null;
        IntPtr mappedPtr;
        byte* destPtr;
        bool isPersistentMapped = vkBuffer.Memory.IsPersistentMapped;

        if (isPersistentMapped)
        {
            mappedPtr = (IntPtr)vkBuffer.Memory.BlockMappedPointer;
            destPtr = (byte*)mappedPtr + bufferOffsetInBytes;
        }
        else
        {
            copySrcVkBuffer = getFreeStagingBuffer(sizeInBytes);
            mappedPtr = (IntPtr)copySrcVkBuffer.Memory.BlockMappedPointer;
            destPtr = (byte*)mappedPtr;
        }

        Unsafe.CopyBlock(destPtr, source.ToPointer(), sizeInBytes);

        if (!isPersistentMapped)
        {
            var pool = getFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();

            var copyRegion = new VkBufferCopy
            {
                dstOffset = bufferOffsetInBytes,
                size = sizeInBytes
            };
            Vk.CmdCopyBuffer(cb, copySrcVkBuffer.DeviceBuffer, vkBuffer.DeviceBuffer, 1, &copyRegion);

            pool.EndAndSubmit(cb);
            lock (stagingResourcesLock)
            {
                submittedStagingBuffers.Add(cb, copySrcVkBuffer);
            }
        }
    }

    private protected override void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x,
        uint y,
        uint z,
        uint width,
        uint height,
        uint depth,
        uint mipLevel,
        uint arrayLayer)
    {
        var vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        bool isStaging = (vkTex.Usage & TextureUsage.Staging) != 0;

        if (isStaging)
        {
            var memBlock = vkTex.Memory;
            uint subresource = texture.CalculateSubresource(mipLevel, arrayLayer);
            var layout = vkTex.GetSubresourceLayout(subresource);
            byte* imageBasePtr = (byte*)memBlock.BlockMappedPointer + layout.offset;

            uint srcRowPitch = FormatHelpers.GetRowPitch(width, texture.Format);
            uint srcDepthPitch = FormatHelpers.GetDepthPitch(srcRowPitch, height, texture.Format);
            Util.CopyTextureRegion(
                source.ToPointer(),
                0, 0, 0,
                srcRowPitch, srcDepthPitch,
                imageBasePtr,
                x, y, z,
                (uint)layout.rowPitch, (uint)layout.depthPitch,
                width, height, depth,
                texture.Format);
        }
        else
        {
            var stagingTex = getFreeStagingTexture(width, height, depth, texture.Format);
            UpdateTexture(stagingTex, source, sizeInBytes, 0, 0, 0, width, height, depth, 0, 0);
            var pool = getFreeCommandPool();
            var cb = pool.BeginNewCommandBuffer();
            VkCommandList.CopyTextureCore_VkCommandBuffer(
                cb,
                stagingTex, 0, 0, 0, 0, 0,
                texture, x, y, z, mipLevel, arrayLayer,
                width, height, depth, 1);
            lock (stagingResourcesLock)
            {
                submittedStagingTextures.Add(cb, stagingTex);
            }

            pool.EndAndSubmit(cb);
        }
    }

    private class SharedCommandPool
    {
        public bool IsCached { get; }
        private readonly VkGraphicsDevice gd;
        private readonly VkCommandPool pool;
        private readonly VkCommandBuffer cb;

        public SharedCommandPool(VkGraphicsDevice gd, bool isCached)
        {
            this.gd = gd;
            IsCached = isCached;

            var commandPoolCi = new VkCommandPoolCreateInfo();
            commandPoolCi.flags = VkCommandPoolCreateFlagBits.CommandPoolCreateTransientBit | VkCommandPoolCreateFlagBits.CommandPoolCreateResetCommandBufferBit;
            commandPoolCi.queueFamilyIndex = this.gd.GraphicsQueueIndex;
            VkCommandPool vkpool;
            var result = Vk.CreateCommandPool(this.gd.Device, &commandPoolCi, null, &vkpool);
            pool = vkpool;
            CheckResult(result);

            var allocateInfo = new VkCommandBufferAllocateInfo();
            allocateInfo.commandBufferCount = 1;
            allocateInfo.level = VkCommandBufferLevel.CommandBufferLevelPrimary;
            allocateInfo.commandPool = pool;
            VkCommandBuffer vkcb;
            result = Vk.AllocateCommandBuffers(this.gd.Device, &allocateInfo, &vkcb);
            cb = vkcb;
            CheckResult(result);
        }

        public VkCommandBuffer BeginNewCommandBuffer()
        {
            var beginInfo = new VkCommandBufferBeginInfo();
            beginInfo.flags = VkCommandBufferUsageFlagBits.CommandBufferUsageOneTimeSubmitBit;
            var result = Vk.BeginCommandBuffer(cb, &beginInfo);
            CheckResult(result);

            return cb;
        }

        public void EndAndSubmit(VkCommandBuffer cb)
        {
            var result = Vk.EndCommandBuffer(cb);
            CheckResult(result);
            gd.submitCommandBuffer(null, cb, 0, null, 0, null, null);
            lock (gd.stagingResourcesLock)
            {
                gd.submittedSharedCommandPools.Add(cb, this);
            }
        }

        internal void Destroy()
        {
            Vk.DestroyCommandPool(gd.Device, pool, null);
        }
    }

    private struct FenceSubmissionInfo
    {
        public readonly OpenTK.Graphics.Vulkan.VkFence Fence;
        public readonly VkCommandList CommandList;
        public readonly VkCommandBuffer CommandBuffer;

        public FenceSubmissionInfo(OpenTK.Graphics.Vulkan.VkFence fence, VkCommandList commandList, VkCommandBuffer commandBuffer)
        {
            Fence = fence;
            CommandList = commandList;
            CommandBuffer = commandBuffer;
        }
    }
}

internal unsafe delegate VkResult VkCreateDebugReportCallbackExtD(
    VkInstance instance,
    VkDebugReportCallbackCreateInfoEXT* createInfo,
    IntPtr allocatorPtr,
    out VkDebugReportCallbackEXT ret);

internal unsafe delegate void VkDestroyDebugReportCallbackExtD(
    VkInstance instance,
    VkDebugReportCallbackEXT callback,
    VkAllocationCallbacks* pAllocator);

internal unsafe delegate VkResult VkDebugMarkerSetObjectNameExtT(VkDevice device, VkDebugMarkerObjectNameInfoEXT* pNameInfo);

internal unsafe delegate void VkCmdDebugMarkerBeginExtT(VkCommandBuffer commandBuffer, VkDebugMarkerMarkerInfoEXT* pMarkerInfo);

internal delegate void VkCmdDebugMarkerEndExtT(VkCommandBuffer commandBuffer);

internal unsafe delegate void VkCmdDebugMarkerInsertExtT(VkCommandBuffer commandBuffer, VkDebugMarkerMarkerInfoEXT* pMarkerInfo);

internal unsafe delegate void VkGetBufferMemoryRequirements2T(VkDevice device, VkBufferMemoryRequirementsInfo2* pInfo, VkMemoryRequirements2* pMemoryRequirements);

internal unsafe delegate void VkGetImageMemoryRequirements2T(VkDevice device, VkImageMemoryRequirementsInfo2* pInfo, VkMemoryRequirements2* pMemoryRequirements);

internal unsafe delegate void VkGetPhysicalDeviceProperties2T(VkPhysicalDevice physicalDevice, void* properties);

// VK_EXT_metal_surface

internal unsafe delegate VkResult VkCreateMetalSurfaceExtT(
    VkInstance instance,
    VkMetalSurfaceCreateInfoExt* pCreateInfo,
    VkAllocationCallbacks* pAllocator,
    VkSurfaceKHR* pSurface);

#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
internal unsafe struct VkMetalSurfaceCreateInfoExt
{
    public const VkStructureType VK_STRUCTURE_TYPE_METAL_SURFACE_CREATE_INFO_EXT = (VkStructureType)1000217000;

    public VkStructureType SType;
    public void* PNext;
    public uint Flags;
    public void* PLayer;
}

internal enum VkDriverId
{
}

internal struct VkConformanceVersion
{
    public byte Major;
    public byte Minor;
    public byte Subminor;
    public byte Patch;
}
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value

