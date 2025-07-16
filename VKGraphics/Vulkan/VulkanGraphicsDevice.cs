
using OpenTK.Graphics.Vulkan;

using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal sealed partial class VulkanGraphicsDevice : GraphicsDevice
{
    internal readonly DeviceCreateState _deviceCreateState;
    internal readonly List<FixedUtf8String> _surfaceExtensions;

    private readonly VkDeviceMemoryManager _memoryManager;
    private readonly VulkanDescriptorPoolManager _descriptorPoolManager;
    private readonly ConcurrentDictionary<VkFormat, VkFilter> _filters = new();
    internal readonly ConcurrentDictionary<RenderPassCacheKey, VulkanRenderPassHolder> _renderPasses = new();
    internal readonly object QueueLock = new();

    public VkDeviceMemoryManager MemoryManager => _memoryManager;

    private readonly ConcurrentBag<VkSemaphore> _availableSemaphores = new();
    private readonly ConcurrentBag<VkFence> _availableSubmissionFences = new();

    private const int MaxSharedCommandLists = 8;
    private readonly ConcurrentBag<VulkanCommandList> _sharedCommandLists = new();

    private readonly object _fenceCompletionCallbackLock = new();
    private readonly List<FenceCompletionCallbackInfo> _fenceCompletionCallbacks = new();
    private readonly List<SwapchainOldFenceSemaphoreInfo> _swapchainOldFences = new();

    private const uint MinStagingBufferSize = 64;
    private const uint MaxStagingBufferSize = 512;
    private readonly List<VulkanBuffer> _availableStagingBuffers = new();
    private readonly List<VulkanTexture> _availableStagingTextures = new();

    private readonly Dictionary<MappableResource, ResourceMapping> _mappedResources = new();
    private readonly object _mappedResourcesLock = new();

#if DEBUG
    internal readonly ConcurrentDictionary<VkImage, WeakReference<VulkanTexture>> NativeToManagedImages = new();
#endif

    // optional functions

    // synchronization2
    internal readonly unsafe delegate* unmanaged<VkQueue, uint, VkSubmitInfo2*, VkFence, VkResult> vkQueueSubmit2;
    internal readonly unsafe delegate* unmanaged<VkCommandBuffer, VkDependencyInfo*, void> CmdPipelineBarrier2;

    // dynamic_rendering
    internal readonly unsafe delegate* unmanaged<VkCommandBuffer, VkRenderingInfo*, void> CmdBeginRendering;
    internal readonly unsafe delegate* unmanaged<VkCommandBuffer, void> CmdEndRendering;

    // dedicated allocation and memreq2
    internal readonly unsafe delegate* unmanaged<VkDevice, VkBufferMemoryRequirementsInfo2*, VkMemoryRequirements2*, void> GetBufferMemoryRequirements2;
    internal readonly unsafe delegate* unmanaged<VkDevice, VkImageMemoryRequirementsInfo2*, VkMemoryRequirements2*, void> GetImageMemoryRequirements2;

    // debug marker ext
    internal readonly unsafe delegate* unmanaged<VkDevice, VkDebugMarkerObjectNameInfoEXT*, VkResult> vkDebugMarkerSetObjectNameEXT;
    internal readonly unsafe delegate* unmanaged<VkCommandBuffer, VkDebugMarkerMarkerInfoEXT*, void> CmdDebugMarkerBeginEXT;
    internal readonly unsafe delegate* unmanaged<VkCommandBuffer, void> CmdDebugMarkerEndEXT;
    internal readonly unsafe delegate* unmanaged<VkCommandBuffer, VkDebugMarkerMarkerInfoEXT*, void> CmdDebugMarkerInsertEXT;

    public VkDevice Device => _deviceCreateState.Device;
    public unsafe bool HasSetMarkerName => vkDebugMarkerSetObjectNameEXT is not null;
    public new VulkanResourceFactory ResourceFactory => (VulkanResourceFactory)base.ResourceFactory;
    public VulkanDescriptorPoolManager DescriptorPoolManager => _descriptorPoolManager;

    public string? DriverName { get; }
    public string? DriverInfo { get; }

    private unsafe VulkanGraphicsDevice(ref DeviceCreateState deviceCreateState, SwapchainDescription? swapchainDesc, List<FixedUtf8String> surfaceExtensions)
    {
        try
        {
            // once we adopt the DCS, default-out the source because the caller will try to free the handles (which we now own)
            _deviceCreateState = deviceCreateState;
            deviceCreateState = default;
            _surfaceExtensions = surfaceExtensions;

            // Populate knowns based on the fact that this is a Vulkan implementation
            BackendType = GraphicsBackend.Vulkan;
            IsUvOriginTopLeft = true;
            IsDepthRangeZeroToOne = true;
            IsClipSpaceYInverted = true;
            ApiVersion = new(
                (int)_deviceCreateState.ApiVersion.Major,
                (int)_deviceCreateState.ApiVersion.Minor,
                (int)_deviceCreateState.ApiVersion.Patch, 0);

            // Then stuff out of the physical device properties
            UniformBufferMinOffsetAlignment = (uint)_deviceCreateState.PhysicalDeviceProperties.limits.minUniformBufferOffsetAlignment;
            StructuredBufferMinOffsetAlignment = (uint)_deviceCreateState.PhysicalDeviceProperties.limits.minStorageBufferOffsetAlignment;
            DeviceName = Util.GetString(_deviceCreateState.PhysicalDeviceProperties.deviceName);
            VendorName = $"id:{_deviceCreateState.PhysicalDeviceProperties.vendorID:x8}";
            
            // Then driver properties (if available)
            if (_deviceCreateState.HasDriverPropertiesExt)
            {
                var GetPhysicalDeviceProperties2 =
                    (delegate* unmanaged<VkPhysicalDevice, void*, void>)
                    GetInstanceProcAddr("vkGetPhysicalDeviceProperties2"u8, "vkGetPhysicalDeviceProperties2KHR"u8);

                if (GetPhysicalDeviceProperties2 is not null)
                {
                    var driverProps = new VkPhysicalDeviceDriverProperties()
                    {
                    };
                    var deviceProps = new VkPhysicalDeviceProperties2()
                    {
                        pNext = &driverProps,
                    };
                    GetPhysicalDeviceProperties2(_deviceCreateState.PhysicalDevice, &deviceProps);

                    DriverName = Util.GetString(driverProps.driverName);
                    DriverInfo = Util.GetString(driverProps.driverInfo);

                    ApiVersion = new(
                        driverProps.conformanceVersion.major,
                        driverProps.conformanceVersion.minor,
                        driverProps.conformanceVersion.subminor,
                        driverProps.conformanceVersion.patch);
                }
            }

            // Then several optional extension functions
            if (_deviceCreateState.HasDebugMarkerExt)
            {
                vkDebugMarkerSetObjectNameEXT =
                    (delegate* unmanaged<VkDevice, VkDebugMarkerObjectNameInfoEXT*, VkResult>)GetInstanceProcAddr("vkDebugMarkerSetObjectNameEXT"u8);
                CmdDebugMarkerBeginEXT =
                    (delegate* unmanaged<VkCommandBuffer, VkDebugMarkerMarkerInfoEXT*, void>)GetInstanceProcAddr("vkCmdDebugMarkerBeginEXT"u8);
                CmdDebugMarkerEndEXT =
                    (delegate* unmanaged<VkCommandBuffer, void>)GetInstanceProcAddr("vkCmdDebugMarkerEndEXT"u8);
                CmdDebugMarkerInsertEXT =
                    (delegate* unmanaged<VkCommandBuffer, VkDebugMarkerMarkerInfoEXT*, void>)GetInstanceProcAddr("vkCmdDebugMarkerInsertEXT"u8);
            }

            if (_deviceCreateState.HasDedicatedAllocationExt && _deviceCreateState.HasMemReqs2Ext)
            {
                GetBufferMemoryRequirements2 =
                    (delegate* unmanaged<VkDevice, VkBufferMemoryRequirementsInfo2*, VkMemoryRequirements2*, void>)
                    GetDeviceProcAddr("vkGetBufferMemoryRequirements2"u8, "vkGetBufferMemoryRequirements2KHR"u8);
                GetImageMemoryRequirements2 =
                    (delegate* unmanaged<VkDevice, VkImageMemoryRequirementsInfo2*, VkMemoryRequirements2*, void>)
                    GetDeviceProcAddr("vkGetImageMemoryRequirements2"u8, "vkGetImageMemoryRequirements2KHR"u8);
            }

            if (_deviceCreateState.HasDynamicRendering)
            {
                CmdBeginRendering =
                    (delegate* unmanaged<VkCommandBuffer, VkRenderingInfo*, void>)
                    GetDeviceProcAddr("vkCmdBeginRendering"u8, "vkCmdBeginRenderingKHR"u8);
                CmdEndRendering =
                    (delegate* unmanaged<VkCommandBuffer, void>)
                    GetDeviceProcAddr("vkCmdEndRendering"u8, "vkCmdEndRenderingKHR"u8);
            }

            if (_deviceCreateState.HasSync2Ext)
            {
                vkQueueSubmit2 =
                    (delegate* unmanaged<VkQueue, uint, VkSubmitInfo2*, VkFence, VkResult>)
                    GetDeviceProcAddr("vkQueueSubmit2"u8, "vkQueueSubmit2KHR"u8);
                CmdPipelineBarrier2 =
                    (delegate* unmanaged<VkCommandBuffer, VkDependencyInfo*, void>)
                    GetDeviceProcAddr("vkCmdPipelineBarrier2"u8, "vkCmdPipelineBarrier2KHR"u8);
            }

            // Create other bits and pieces
            _memoryManager = new(
                _deviceCreateState.Device,
                _deviceCreateState.PhysicalDevice,
                _deviceCreateState.PhysicalDeviceProperties.limits.bufferImageGranularity,
                chunkGranularity: 1024);

            Features = new (
                computeShader: _deviceCreateState.QueueFamilyInfo.MainComputeFamilyIdx >= 0,
                geometryShader: (VkBool32)_deviceCreateState.PhysicalDeviceFeatures2.features.geometryShader,
                tessellationShaders: (VkBool32)_deviceCreateState.PhysicalDeviceFeatures2.features.tessellationShader,
                multipleViewports: (VkBool32)_deviceCreateState.PhysicalDeviceFeatures2.features.multiViewport,
                samplerLodBias: true,
                drawBaseVertex: true,
                drawBaseInstance: true,
                drawIndirect: true,
                drawIndirectBaseInstance: (VkBool32)_deviceCreateState.PhysicalDeviceFeatures2.features.drawIndirectFirstInstance,
                fillModeWireframe: (VkBool32)_deviceCreateState.PhysicalDeviceFeatures2.features.fillModeNonSolid,
                samplerAnisotropy: (VkBool32)_deviceCreateState.PhysicalDeviceFeatures2.features.samplerAnisotropy,
                depthClipDisable: (VkBool32)_deviceCreateState.PhysicalDeviceFeatures2.features.depthClamp,
                texture1D: true,
                independentBlend: (VkBool32)_deviceCreateState.PhysicalDeviceFeatures2.features.independentBlend,
                structuredBuffer: true,
                subsetTextureView: true,
                commandListDebugMarkers: _deviceCreateState.HasDebugMarkerExt,
                bufferRangeBinding: true,
                shaderFloat64: (VkBool32)_deviceCreateState.PhysicalDeviceFeatures2.features.shaderFloat64);

            base.ResourceFactory = new VulkanResourceFactory(this);
            _descriptorPoolManager = new(this);

            // TODO: MainSwapchain
            if (swapchainDesc is { } desc)
            {
                Debug.Assert(_deviceCreateState.Surface != VkSurfaceKHR.Zero);

                // note: the main swapchain takes ownership of the created surface
                MainSwapchain = new VulkanSwapchain(this, desc, ref Unsafe.AsRef(ref _deviceCreateState.Surface), _deviceCreateState.QueueFamilyInfo.PresentFamilyIdx);
            }

            EagerlyAllocateSomeResources();
            PostDeviceCreated();
        }
        catch
        {
            // eagerly dispose if we threw here
            DisposeDirectOwned();
            throw;
        }
    }

    private bool _disposed;

    protected override unsafe void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        DisposeDirectOwned();
    }

    private unsafe void DisposeDirectOwned()
    {
        if (_disposed) return;
        _disposed = true;

        // if we have any unbalanced mappings, clean those up first
        lock (_mappedResourcesLock)
        {
            foreach (var (resource, mapInfo) in _mappedResources)
            {
                mapInfo.RefCount.DecrementDispose();
                // force-deallocate
                while (!mapInfo.RefCount.IsClosed)
                    mapInfo.RefCount.Decrement();
            }
        }

        // TODO: destroy all other associated information
        MainSwapchain?.Dispose();

        foreach (var rpi in _renderPasses.Values)
        {
            rpi.DecRef();
        }

        var dcs = _deviceCreateState;

        lock (_availableStagingBuffers)
        {
            foreach (var buf in _availableStagingBuffers)
            {
                buf.Dispose();
            }
        }

        lock (_availableStagingTextures)
        {
            foreach (var tex in _availableStagingTextures)
            {
                tex.Dispose();
            }
        }

        while (_sharedCommandLists.TryTake(out var cl))
        {
            cl.Dispose();
        }

        while (_availableSemaphores.TryTake(out var semaphore))
        {
            DestroySemaphore(dcs.Device, semaphore, null);
        }

        while (_availableSubmissionFences.TryTake(out var fence))
        {
            DestroyFence(dcs.Device, fence, null);
        }

        if (_descriptorPoolManager is { } poolManager)
        {
            poolManager.DestroyAll();
        }

        if (_memoryManager is { } memoryManager)
        {
            memoryManager.Dispose();
        }

        if (dcs.Device != VkDevice.Zero)
        {
            DestroyDevice(dcs.Device, null);
        }

        if (dcs.Surface != VkSurfaceKHR.Zero)
        {
            DestroySurfaceKHR(dcs.Instance, dcs.Surface, null);
        }

        if (dcs.DebugCallbackHandle != VkDebugReportCallbackEXT.Zero)
        {
            var DestroyDebugReportCallbackEXT =
                (delegate* unmanaged<VkInstance, VkDebugReportCallbackEXT, VkAllocationCallbacks*, void>)
                GetInstanceProcAddr(dcs.Instance, "vkDestroyDebugReportCallbackEXT"u8);
            DestroyDebugReportCallbackEXT(dcs.Instance, dcs.DebugCallbackHandle, null);
        }

        if (dcs.Instance != VkInstance.Zero)
        {
            DestroyInstance(dcs.Instance, null);
        }
    }

    private unsafe void EagerlyAllocateSomeResources()
    {
        // eagerly allocate a few semaphores and submission fences
        // semaphores are used for synchronization between command lists
        // (we use them particularly for the memory sync, as well as being able to associate multiple fences with a submit)

        var semaphoreCreateInfo = new VkSemaphoreCreateInfo()
        {
        };

        var fenceCreateInfo = new VkFenceCreateInfo()
        {
        };

        for (var i = 0; i < 4; i++)
        {
            VkSemaphore semaphore;
            VulkanUtil.CheckResult(CreateSemaphore(Device, &semaphoreCreateInfo, null, &semaphore));
            _availableSemaphores.Add(semaphore);

            VkFence fence;
            VulkanUtil.CheckResult(CreateFence(Device, &fenceCreateInfo, null, &fence));
            _availableSubmissionFences.Add(fence);
        }
    }

    internal unsafe void SetDebugMarkerName(VkDebugReportObjectTypeEXT type, ulong target, ReadOnlySpan<char> name)
    {
        if (vkDebugMarkerSetObjectNameEXT is null) return;
        DoSet(this, type, target, name);

        [SkipLocalsInit]
        static void DoSet(VulkanGraphicsDevice @this, VkDebugReportObjectTypeEXT type, ulong target, ReadOnlySpan<char> name)
        {
            Span<byte> utf8Buffer = stackalloc byte[128];
            Util.GetNullTerminatedUtf8(name, ref utf8Buffer);
            @this.SetDebugMarkerName(type, target, utf8Buffer);
        }
    }

    internal unsafe void SetDebugMarkerName(VkDebugReportObjectTypeEXT type, ulong target, ReadOnlySpan<byte> nameUtf8)
    {
        if (vkDebugMarkerSetObjectNameEXT is null) return;

        fixed (byte* utf8Ptr = nameUtf8)
        {
            VkDebugMarkerObjectNameInfoEXT nameInfo = new()
            {
                objectType = type,
                obj = target,
                pObjectName = utf8Ptr
            };

            VulkanUtil.CheckResult(vkDebugMarkerSetObjectNameEXT(Device, &nameInfo));
        }
    }

    internal unsafe VkCommandPool CreateCommandPool(bool transient)
    {
        var commandPoolCreateInfo = new VkCommandPoolCreateInfo()
        {
            flags = VkCommandPoolCreateFlagBits.CommandPoolCreateResetCommandBufferBit,
            queueFamilyIndex = (uint)_deviceCreateState.QueueFamilyInfo.MainGraphicsFamilyIdx,
        };

        if (transient)
        {
            commandPoolCreateInfo.flags |= VkCommandPoolCreateFlagBits.CommandPoolCreateTransientBit;
        }

        VkCommandPool commandPool;
        VulkanUtil.CheckResult(Vk.CreateCommandPool(_deviceCreateState.Device, &commandPoolCreateInfo, null, &commandPool));
        return commandPool;
    }

    internal unsafe VkSemaphore GetSemaphore()
    {
        if (_availableSemaphores.TryTake(out var semaphore))
        {
            return semaphore;
        }

        var semCreateInfo = new VkSemaphoreCreateInfo()
        {
        };

        VulkanUtil.CheckResult(CreateSemaphore(Device, &semCreateInfo, null, &semaphore));
        return semaphore;
    }

    internal void ReturnSemaphore(VkSemaphore semaphore)
    {
        _availableSemaphores.Add(semaphore);
    }

    internal unsafe VkFence GetSubmissionFence(bool reset = true)
    {
        if (_availableSubmissionFences.TryTake(out var fence))
        {
            if (reset)
            {
                VulkanUtil.CheckResult(ResetFences(Device, 1, &fence));
            }
            return fence;
        }

        var fenceCreateInfo = new VkFenceCreateInfo()
        {
        };

        VulkanUtil.CheckResult(CreateFence(Device, &fenceCreateInfo, null, &fence));
        return fence;
    }

    internal void ReturnSubmissionFence(VkFence fence)
    {
        _availableSubmissionFences.Add(fence);
    }

    private struct FenceCompletionCallbackInfo
    {
        public VkFence Fence;
        public VulkanCommandList.FenceCompletionCallbackInfo CallbackInfo;
    }

    internal struct SwapchainOldFenceSemaphoreInfo
    {
        public VkFence[]? Fences;
        public VkSemaphore[]? Semaphores;
        public int NumFences;
        public int NumSemaphores;
    }

    internal void RegisterFenceCompletionCallback(VkFence fence, in VulkanCommandList.FenceCompletionCallbackInfo callbackInfo)
    {
        lock (_fenceCompletionCallbackLock)
        {
            _fenceCompletionCallbacks.Add(new()
            {
                Fence = fence,
                CallbackInfo = callbackInfo,
            });
        }
    }

    internal void RegisterSwapchainOldFences(in SwapchainOldFenceSemaphoreInfo oldFences)
    {
        if (oldFences.NumFences == 0)
        {
            if (oldFences.Fences is { } fences)
            {
                ArrayPool<VkFence>.Shared.Return(fences, clearArray: true);
            }
        }
        if (oldFences.NumSemaphores == 0)
        {
            if (oldFences.Semaphores is { } semaphores)
            {
                ArrayPool<VkSemaphore>.Shared.Return(semaphores, clearArray: true);
            }
        }

        if (oldFences.NumFences is 0 && oldFences.NumSemaphores is 0)
        {
            // nothing to do, so don't do anything
            return;
        }

        lock (_fenceCompletionCallbackLock)
        {
            _swapchainOldFences.Add(oldFences);
        }
    }

    internal unsafe void CheckFencesForCompletion()
    {
        lock (_fenceCompletionCallbackLock)
        {
            var list = _fenceCompletionCallbacks;
            for (int i = 0; i < list.Count; i++)
            {
                ref var callback = ref CollectionsMarshal.AsSpan(list)[i];
                var result = GetFenceStatus(_deviceCreateState.Device, callback.Fence);
                if (result == VkResult.Success)
                {
                    // the fence is complete, invoke the callback
                    callback.CallbackInfo.CommandList.OnSubmissionFenceCompleted(callback.Fence, in callback.CallbackInfo, errored: false);
                }
                else if (result is not VkResult.NotReady)
                {
                    // some error condition, also invoke the callback to give it a chance to clean up, but tell it that this is an error condition
                    callback.CallbackInfo.CommandList.OnSubmissionFenceCompleted(callback.Fence, in callback.CallbackInfo, errored: true);
                }
                else // result is VkResult.VK_NOT_READY
                {
                    Debug.Assert(result is VkResult.NotReady);
                    // not ready, keep it in the list
                    continue;
                }

                // NOTE: `callback` is invalidated once the list is modified. Do not read after this point.
                list.RemoveAt(i);
                i -= 1;
            }

            var list2 = _swapchainOldFences;
            for (int i = 0; i < list2.Count; i++)
            {
                ref var fences = ref CollectionsMarshal.AsSpan(list2)[i];
                VkResult result = VkResult.Success;
                fixed (VkFence* pFences = fences.Fences)
                {
                    if (pFences is not null)
                    {
                        result = Vk.WaitForFences(Device, (uint)fences.NumFences, pFences, 1, 0);
                    }
                }

                if (result == VkResult.Success)
                {
                    // fences are complete, clean everything up
                    if (fences.Fences is { } fenceArr)
                    {
                        foreach (var fence in fenceArr)
                        {
                            if (fence != VkFence.Zero)
                            {
                                DestroyFence(Device, fence, null);
                            }
                        }
                        ArrayPool<VkFence>.Shared.Return(fenceArr, clearArray: true);
                    }

                    if (fences.Semaphores is { } semArr)
                    {
                        foreach (var sem in semArr)
                        {
                            if (sem != VkSemaphore.Zero)
                            {
                                DestroySemaphore(Device, sem, null);
                            }
                        }
                        ArrayPool<VkSemaphore>.Shared.Return(semArr, clearArray: true);
                    }
                }
                else if (result == VkResult.Timeout)
                {
                    // fences are not complete, move on
                    continue;
                }
                else
                {
                    // some other error condition, dunno what to do here tho
                }

                // NOTE: `callback` is invalidated once the list is modified. Do not read after this point.
                list2.RemoveAt(i);
                i -= 1;
            }
        }
    }

    private protected override void SubmitCommandsCore(CommandList commandList, Fence? fence)
    {
        var cl = Util.AssertSubtype<CommandList, VulkanCommandList>(commandList);
        var vkFence = Util.AssertSubtypeOrNull<Fence, VulkanFence>(fence);

        lock (QueueLock)
        {
            _ = cl.SubmitToQueue(_deviceCreateState.MainQueue, vkFence, null, 0);
        }

        // also take the opportunity to check for fence completions
        CheckFencesForCompletion();
    }

    internal VulkanCommandList GetAndBeginCommandList()
    {
        if (!_sharedCommandLists.TryTake(out var sharedList))
        {
            var desc = new CommandListDescription() { Transient = true };
            sharedList = ResourceFactory.CreateCommandList(desc);
            sharedList.Name = "GraphicsDevice Shared CommandList";
        }

        sharedList.Begin();
        return sharedList;
    }

    private static readonly Action<VulkanCommandList> s_returnClToPool = static cl =>
    {
        var device = cl.Device;

        if (device._sharedCommandLists.Count < MaxSharedCommandLists)
        {
            device._sharedCommandLists.Add(cl);
        }
        else
        {
            cl.Dispose();
        }
    };

    internal (VkSemaphore Sem, VkFence Fence) EndAndSubmitCommands(VulkanCommandList cl, VkPipelineStageFlagBits2 semaphoreStages = 0)
    {
        cl.End();
        CheckFencesForCompletion();

        lock (QueueLock)
        {
            return cl.SubmitToQueue(_deviceCreateState.MainQueue, null, s_returnClToPool, semaphoreStages);
        }
    }

    internal VulkanBuffer GetPooledStagingBuffer(uint size)
    {
        lock (_availableStagingBuffers)
        {
            for (int i = 0; i < _availableStagingBuffers.Count; i++)
            {
                var buffer = _availableStagingBuffers[i];
                if (buffer.SizeInBytes >= size)
                {
                    _availableStagingBuffers.RemoveAt(i);
                    // note: don't reset sync state, as it is REQUIRED that we sync against it for writes
                    return buffer;
                }
            }
        }

        uint newBufferSize = Math.Max(MinStagingBufferSize, size);
        var buf = ResourceFactory.CreateBuffer(
            new BufferDescription(newBufferSize, BufferUsage.StagingWrite));
        buf.Name = "Staging Buffer (GraphicsDevice)";
        return buf;
    }

    internal void ReturnPooledStagingBuffers(ReadOnlySpan<VulkanBuffer> buffers)
    {
        lock (_availableStagingBuffers)
        {
            foreach (var buf in buffers)
            {
                _availableStagingBuffers.Add(buf);
            }
        }
    }

    internal VulkanTexture GetPooledStagingTexture(uint width, uint height, uint depth, PixelFormat format)
    {
        var totalSize = FormatHelpers.GetRegionSize(width, height, depth, format);
        lock (_availableStagingTextures)
        {
            for (int i = 0; i < _availableStagingTextures.Count; i++)
            {
                var tex = _availableStagingTextures[i];
                if (tex.Memory.Size >= totalSize)
                {
                    _availableStagingTextures.RemoveAt(i);
                    tex.SetStagingDimensions(width, height, depth, format);
                    // note: we CANNOT reset sync state, because writes must be correctly sync'd against readers
                    return tex;
                }
            }
        }

        var texWidth = uint.Max(256, width);
        var texHeight = uint.Max(256, height);
        var newTex = ResourceFactory.CreateTexture(
            TextureDescription.Texture3D(texWidth, texHeight, depth, 1, format, TextureUsage.Staging));
        newTex.SetStagingDimensions(width, height, depth, format);
        newTex.Name = "Staging Texture (GraphicsDevice)";
        return newTex;
    }

    internal void ReturnPooledStagingTextures(ReadOnlySpan<VulkanTexture> textures)
    {
        lock (_availableStagingTextures)
        {
            foreach (var tex in textures)
            {
                _availableStagingTextures.Add(tex);
            }
        }
    }

    private protected override void WaitForIdleCore()
    {
        lock (QueueLock)
        {
            QueueWaitIdle(_deviceCreateState.MainQueue);
        }

        // when the queue has gone idle, all of our fences *should* be signalled.
        // Make sure we clean up their associated information.
        CheckFencesForCompletion();
    }

    public override unsafe void ResetFence(Fence fence)
    {
        var vkFence = Util.AssertSubtype<Fence, VulkanFence>(fence);
        var devFence = vkFence.DeviceFence;
        VulkanUtil.CheckResult(ResetFences(Device, 1, &devFence));
    }

    public override unsafe bool WaitForFence(Fence fence, ulong nanosecondTimeout)
    {
        var vkFence = Util.AssertSubtype<Fence, VulkanFence>(fence);
        var devFence = vkFence.DeviceFence;

        var result = Vk.WaitForFences(Device, 1, &devFence, 1, nanosecondTimeout) == VkResult.Success;
        // if we're waiting for fences, they're probably submission fences
        CheckFencesForCompletion();

        return result;
    }

    public override unsafe bool WaitForFences(Fence[] fences, bool waitAll, ulong nanosecondTimeout)
    {
        VkFence[]? arr = null;
        Span<VkFence> vkFences = fences.Length > 16
            ? (arr = ArrayPool<VkFence>.Shared.Rent(fences.Length))
            : stackalloc VkFence[16];

        for (var i = 0; i < fences.Length; i++)
        {
            vkFences[i] = Util.AssertSubtype<Fence, VulkanFence>(fences[i]).DeviceFence;
        }

        bool result;
        fixed (VkFence* pFences = vkFences)
        {
            result = Vk.WaitForFences(Device, (uint)fences.Length, pFences, waitAll ? 1 : 0, nanosecondTimeout) == VkResult.Success;
        }

        if (arr is not null)
        {
            ArrayPool<VkFence>.Shared.Return(arr);
        }

        // if we're waiting for fences, they're probably submission fences
        CheckFencesForCompletion();

        return result;
    }

    public unsafe override TextureSampleCount GetSampleCountLimit(PixelFormat format, bool depthFormat)
    {
        VkImageUsageFlagBits usageFlags = VkImageUsageFlagBits.ImageUsageSampledBit;
        usageFlags |= depthFormat
            ? VkImageUsageFlagBits.ImageUsageDepthStencilAttachmentBit
            : VkImageUsageFlagBits.ImageUsageColorAttachmentBit;

        VkImageFormatProperties formatProperties;
        GetPhysicalDeviceImageFormatProperties(
            _deviceCreateState.PhysicalDevice,
            VkFormats.VdToVkPixelFormat(format, depthFormat ? TextureUsage.DepthStencil : default),
            VkImageType.ImageType2d,
            VkImageTiling.ImageTilingOptimal,
            usageFlags,
            0,
            &formatProperties);

        VkSampleCountFlagBits vkSampleCounts = formatProperties.sampleCounts;
        if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount64Bit) == VkSampleCountFlagBits.SampleCount64Bit)
        {
            return TextureSampleCount.Count64;
        }
        else if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount32Bit) == VkSampleCountFlagBits.SampleCount32Bit)
        {
            return TextureSampleCount.Count32;
        }
        else if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount16Bit) == VkSampleCountFlagBits.SampleCount16Bit)
        {
            return TextureSampleCount.Count16;
        }
        else if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount8Bit) == VkSampleCountFlagBits.SampleCount8Bit)
        {
            return TextureSampleCount.Count8;
        }
        else if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount4Bit) == VkSampleCountFlagBits.SampleCount4Bit)
        {
            return TextureSampleCount.Count4;
        }
        else if ((vkSampleCounts & VkSampleCountFlagBits.SampleCount2Bit) == VkSampleCountFlagBits.SampleCount2Bit)
        {
            return TextureSampleCount.Count2;
        }
        return TextureSampleCount.Count1;
    }

    internal unsafe VkFilter GetFormatFilter(VkFormat format)
    {
        if (!_filters.TryGetValue(format, out VkFilter filter))
        {
            VkFormatProperties vkFormatProps;
            GetPhysicalDeviceFormatProperties(_deviceCreateState.PhysicalDevice, format, &vkFormatProps);
            filter = (vkFormatProps.optimalTilingFeatures & VkFormatFeatureFlagBits.FormatFeatureSampledImageFilterLinearBit) != 0
                ? VkFilter.FilterLinear
                : VkFilter.FilterNearest;
            _filters.TryAdd(format, filter);
        }

        return filter;
    }

    private protected unsafe override bool GetPixelFormatSupportCore(PixelFormat format, TextureType type, TextureUsage usage, out PixelFormatProperties properties)
    {
        VkFormat vkFormat = VkFormats.VdToVkPixelFormat(format, usage);
        VkImageType vkType = VkFormats.VdToVkTextureType(type);
        VkImageTiling tiling = usage == TextureUsage.Staging
            ? VkImageTiling.ImageTilingLinear
            : VkImageTiling.ImageTilingOptimal;
        VkImageUsageFlagBits vkUsage = VkFormats.VdToVkTextureUsage(usage);

        VkImageFormatProperties vkProps;
        VkResult result = GetPhysicalDeviceImageFormatProperties(
            _deviceCreateState.PhysicalDevice,
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
        VulkanUtil.CheckResult(result);

        properties = new PixelFormatProperties(
           vkProps.maxExtent.width,
           vkProps.maxExtent.height,
           vkProps.maxExtent.depth,
           vkProps.maxMipLevels,
           vkProps.maxArrayLayers,
           (uint)vkProps.sampleCounts);
        return true;
    }

    private protected unsafe override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, nint source, uint sizeInBytes)
    {
        var vkBuffer = Util.AssertSubtype<DeviceBuffer, VulkanBuffer>(buffer);
        VulkanBuffer? copySrcBuffer = null;

        byte* mappedPtr;
        byte* destPtr;
        if (vkBuffer.Memory.IsPersistentMapped)
        {
            mappedPtr = (byte*)vkBuffer.Memory.BlockMappedPointer;
            destPtr = mappedPtr + bufferOffsetInBytes;
        }
        else
        {
            copySrcBuffer = GetPooledStagingBuffer(sizeInBytes);
            mappedPtr = (byte*)copySrcBuffer.Memory.BlockMappedPointer;
            destPtr = mappedPtr;
        }

        Unsafe.CopyBlock(destPtr, (void*)source, sizeInBytes);

        if (copySrcBuffer is not null)
        {
            // note: we DON'T need an explicit flush here, because queue submission does so implicitly

            // QueueLock is how we sync global sync state
            lock (QueueLock)
            {
                // the buffer WAS written to though, make sure we note that
                copySrcBuffer.AllSyncStates.Fill(new()
                {
                    LastWriter = new()
                    {
                        AccessMask = VkAccessFlagBits.AccessHostWriteBit,
                        StageMask = VkPipelineStageFlagBits.PipelineStageHostBit,
                    },
                    PerStageReaders = 0,
                });
            }

            var cl = GetAndBeginCommandList();
            cl.AddStagingResource(copySrcBuffer);
            // then CopyBuffer will handle synchro in the CommandList itself
            cl.CopyBuffer(copySrcBuffer, 0, vkBuffer, bufferOffsetInBytes, sizeInBytes);
            EndAndSubmitCommands(cl);
        }
        else
        {
            // not a staging buffer, we need to explicitly flush

            // note: we don't need to flush because the memory is "coherent"

            // QueueLock is how we sync global sync state
            lock (QueueLock)
            {
                vkBuffer.AllSyncStates.Fill(new()
                {
                    LastWriter = new()
                    {
                        AccessMask = VkAccessFlagBits.AccessHostWriteBit,
                        StageMask = VkPipelineStageFlagBits.PipelineStageHostBit,
                    },
                    PerStageReaders = 0,
                });
            }
        }
    }

    private protected unsafe override void UpdateTextureCore(Texture texture,
        nint source, uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
        var tex = Util.AssertSubtype<Texture, VulkanTexture>(texture);

        if ((tex.Usage & TextureUsage.Staging) != 0)
        {
            // staging buffer, persistent-mapped VkBuffer, not an image
            UpdateStagingTexture(tex, source, x, y, z, width, height, depth, mipLevel, arrayLayer);
        }
        else
        {
            // not staging, backed by an actual VkImage, meaning we need to use a staging texture
            var stagingTex = GetPooledStagingTexture(width, height, depth, tex.Format);
            // use the helper directly to avoid an unnecessary synchronization op to device memory
            UpdateStagingTexture(stagingTex, source, 0, 0, 0, width, height, depth, 0, 0);
            // a queue submit implicitly synchronizes host->device, which normally requires the flush

            var cl = GetAndBeginCommandList();
            cl.AddStagingResource(stagingTex);
            cl.CopyTexture(
                stagingTex, 0, 0, 0, 0, 0,
                tex, x, y, z, mipLevel, arrayLayer,
                width, height, depth, 1);
            EndAndSubmitCommands(cl);
        }
    }

    private unsafe void UpdateStagingTexture(
        VulkanTexture tex,
        nint source, uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
        var layout = tex.GetSubresourceLayout(mipLevel, arrayLayer);
        var basePtr = (byte*)tex.Memory.BlockMappedPointer + layout.offset;

        var rowPitch = FormatHelpers.GetRowPitch(width, tex.Format);
        var depthPitch = FormatHelpers.GetDepthPitch(rowPitch, height, tex.Format);
        Util.CopyTextureRegion(
            (void*)source,
            0, 0, 0,
            rowPitch, depthPitch,
            basePtr,
            x, y, z,
            (uint)layout.rowPitch, (uint)layout.depthPitch,
            width, height, depth,
            tex.Format);

        // QueueLock synchronizes access to global sync state
        lock (QueueLock)
        {
            tex.AllSyncStates.Fill(new()
            {
                LastWriter = new()
                {
                    AccessMask = VkAccessFlagBits.AccessHostWriteBit,
                    StageMask = VkPipelineStageFlagBits.PipelineStageHostBit,
                },
                PerStageReaders = 0,
                // note: staging textures don't track layout
            });
        }
    }


    // TODO: currently, mapping a resource maps ALL subresources, even though the Veldrid API only asks for 1
    private protected unsafe override MappedResource MapCore(MappableResource resource, uint bufferOffsetInBytes, uint sizeInBytes, MapMode mode, uint subresource)
    {
        VkMemoryBlock memoryBlock;
        ISynchronizedResource syncResource;
        void* mappedPtr = null;
        var rowPitch = 0u;
        var depthPitch = 0u;
        var syncSubresource = new SyncSubresourceRange(0, 0, 1, 1);

        if (resource is VulkanBuffer buffer)
        {
            syncResource = buffer;
            memoryBlock = buffer.Memory;
        }
        else
        {
            var tex = Util.AssertSubtype<MappableResource, VulkanTexture>(resource);
            syncResource = tex;
            Util.GetMipLevelAndArrayLayer(tex, subresource, out var mipLevel, out var arrayLayer);
            syncSubresource = new(arrayLayer, mipLevel, 1, 1);

            var layout = tex.GetSubresourceLayout(mipLevel, arrayLayer);
            memoryBlock = tex.Memory;
            bufferOffsetInBytes += (uint)layout.offset;
            rowPitch = (uint)layout.rowPitch;
            depthPitch = (uint)layout.depthPitch;
        }

        if (memoryBlock.DeviceMemory != VkDeviceMemory.Zero)
        {
            var mapOffset = memoryBlock.Offset + bufferOffsetInBytes;

            if ((mode & MapMode.Read) != 0)
            {
                var barrierMasks = new SyncBarrierMasks()
                {
                    StageMask = VkPipelineStageFlagBits.PipelineStageHostBit,
                    AccessMask = VkAccessFlagBits.AccessHostReadBit,
                };
                var syncRequest = new SyncRequest()
                {
                    BarrierMasks = barrierMasks,
                    // note: host reads must be done in PREINITIALIZED or GENERAL.
                    // Because we don't have a good way to know which we're in here, we always transition
                    // to GENERAL for a map operation.
                    Layout = VkImageLayout.ImageLayoutGeneral
                };

                var needSyncOrLayoutTransition = false;
                lock (QueueLock)
                {
                    ref var syncInfo = ref syncResource.SyncStateForSubresource(new(syncSubresource.BaseLayer, syncSubresource.BaseMip));
                    var syncInfoCopy = syncInfo;
                    needSyncOrLayoutTransition = VulkanCommandList.TryBuildSyncBarrier(ref syncInfoCopy, syncRequest, transitionFromUnknown: true, out _, out _);
                    if (!needSyncOrLayoutTransition)
                    {
                        // we don't need to do an explicit sync, actually update the sync info
                        syncInfo = syncInfoCopy;
                    }
                }

                if (needSyncOrLayoutTransition)
                {
                    // a read mode was requested, we need to sync-to-host to make sure the memory is visible
                    var cl = GetAndBeginCommandList();
                    cl.SyncResourceDyn(syncResource, syncSubresource, syncRequest);
                    var (_, fence) = EndAndSubmitCommands(cl);
                    // now we need to wait for our fence so we know that the sync has gone through
                    VulkanUtil.CheckResult(Vk.WaitForFences(_deviceCreateState.Device, 1, &fence, 1, ulong.MaxValue));
                    // since we just waited on a fence, lets process pending fences and return stuff to pools
                    CheckFencesForCompletion();
                }
            }

            ResourceMapping? mapping;
            lock (_mappedResourcesLock)
            {
                if (_mappedResources.TryGetValue(resource, out mapping) && !mapping.RefCount.IsClosed)
                {
                    // mapping already exists, update the mode and increment
                    mapping.RefCount.Increment();
                    mapping.UpdateMode(mode);
                }
                else
                {
                    // need to create a new mapping
                    if (memoryBlock.IsPersistentMapped)
                    {
                        mappedPtr = (byte*)memoryBlock.BaseMappedPointer + mapOffset;
                    }
                    else
                    {
                        var atomSize = _deviceCreateState.PhysicalDeviceProperties.limits.nonCoherentAtomSize;
                        var bindOffset = mapOffset / atomSize * atomSize;
                        var bindSize = (sizeInBytes + atomSize - 1) / atomSize * atomSize;

                        // TODO: I'm pretty sure this is STILL wrong if mapping multiple subresources independently
                        var result = MapMemory(Device, memoryBlock.DeviceMemory, bindOffset, bindSize, 0, &mappedPtr);
                        if (result is not VkResult.ErrorMemoryMapFailed)
                        {
                            VulkanUtil.CheckResult(result);
                        }
                        else
                        {
                            ThrowMapFailedException(resource, subresource);
                        }

                        mappedPtr = (byte*)mappedPtr + (mapOffset - bindOffset);
                    }

                    // and an associated resource
                    mapping = new(this, syncResource, memoryBlock);
                    mapping.UpdateMode(mode);
                    _mappedResources[resource] = mapping;
                }
            }

            // Note: InvalidateMappedMemoryRanges is only needed if memory is allocated without HOST_COHERENT
            // We never actually allocate with HOST_COHERENT, only HOST_CACHED, so we need to invalidate.
            var mappedRange = new VkMappedMemoryRange()
            {
                memory = memoryBlock.DeviceMemory,
                offset = memoryBlock.Offset,
                size = memoryBlock.Size,
            };

            VulkanUtil.CheckResult(InvalidateMappedMemoryRanges(Device, 1, &mappedRange));
        }

        return new MappedResource(resource, mode, (nint)mappedPtr, bufferOffsetInBytes, sizeInBytes, subresource, rowPitch, depthPitch);
    }

    private sealed class ResourceMapping : Vulkan.IResourceRefCountTarget
    {
        public readonly VulkanGraphicsDevice Device;
        public readonly ISynchronizedResource Resource;
        public readonly VkMemoryBlock MemoryBlock;
        public Vulkan.ResourceRefCount RefCount { get; }

        private int _mode;
        public MapMode Mode => (MapMode)(byte)Volatile.Read(ref _mode);

        public ResourceMapping(VulkanGraphicsDevice device, ISynchronizedResource resource, VkMemoryBlock memoryBlock)
        {
            Device = device;
            Resource = resource;
            MemoryBlock = memoryBlock;
            RefCount = new(this);
            resource.RefCount.Increment();
        }

        public void UpdateMode(MapMode mode)
        {
            int oldMode, newMode;
            do
            {
                oldMode = Volatile.Read(ref _mode);
                newMode = oldMode | (byte)mode;
            }
            while (Interlocked.CompareExchange(ref _mode, newMode, oldMode) != oldMode);
        }

        // RefZeroed corresponds to us needing to unmap, but ONLY to unmap. Sync is always done independently, because it may need to happen multiple times.
        public void RefZeroed()
        {
            if (MemoryBlock.DeviceMemory != VkDeviceMemory.Zero && !MemoryBlock.IsPersistentMapped)
            {
                UnmapMemory(Device.Device, MemoryBlock.DeviceMemory);
            }

            Resource.RefCount.Decrement();
        }
    }

    private protected unsafe override void UnmapCore(MappableResource resource, uint subresource)
    {
        VkMemoryBlock memoryBlock;
        ref SyncState syncState = ref Unsafe.NullRef<SyncState>();
        if (resource is VulkanBuffer buffer)
        {
            memoryBlock = buffer.Memory;
            syncState = ref buffer.AllSyncStates[0];
        }
        else
        {
            var tex = Util.AssertSubtype<MappableResource, VulkanTexture>(resource);
            memoryBlock = tex.Memory;
            Util.GetMipLevelAndArrayLayer(tex, subresource, out var mipLevel, out var arrayLayer);
            syncState = ref tex.SyncStateForSubresource(new(arrayLayer, mipLevel));
        }

        lock (_mappedResourcesLock)
        {
            if (_mappedResources.TryGetValue(resource, out var mapping) && !mapping.RefCount.IsClosed)
            {
                if ((mapping.Mode & MapMode.Write) != 0)
                {
                    // the mapping is mapped with write access, so we should flush it
                    var mappedRange = new VkMappedMemoryRange()
                    {
                        memory = memoryBlock.DeviceMemory,
                        offset = 0,
                        size = WholeSize,
                    };

                    VulkanUtil.CheckResult(FlushMappedMemoryRanges(Device, 1, &mappedRange));

                    // the queue lock is what we use to sync access to global sync state
                    lock (QueueLock)
                    {
                        syncState = new()
                        {
                            PerStageReaders = 0,
                            LastWriter = new()
                            {
                                AccessMask = VkAccessFlagBits.AccessHostWriteBit,
                                StageMask = VkPipelineStageFlagBits.PipelineStageHostBit,
                            }
                        };
                    }
                }

                // AFTER syncing, decrement to (possibly) unmap
                mapping.RefCount.Decrement();
                if (mapping.RefCount.IsClosed)
                {
                    // if was the last ourstanding reference, remove the resource from this dict
                    _mappedResources.Remove(resource);
                }
            }
        }
    }

    private protected unsafe override void SwapBuffersCore(Swapchain swapchain)
    {
        var vkSwapchain = Util.AssertSubtype<Swapchain, VulkanSwapchain>(swapchain);
        var deviceSwapchain = vkSwapchain.DeviceSwapchain;
        var imageIndex = vkSwapchain.ImageIndex;

        // transition all swapchain images into PRESENT_SRC layout
        var cl = GetAndBeginCommandList();
        cl.UseSwapchainFramebuffer(vkSwapchain.Framebuffer, VkPipelineStageFlagBits.PipelineStageAllCommandsBit);
        foreach (ref var colorTarget in vkSwapchain.Framebuffer.CurrentFramebuffer.ColorTargetsArray.AsSpan())
        {
            var tex = Util.AssertSubtype<Texture, VulkanTexture>(colorTarget.Target);
            cl.SyncResource(tex, new(colorTarget.ArrayLayer, colorTarget.MipLevel, 1, 1), new()
            {
                Layout = VkImageLayout.ImageLayoutPresentSrcKhr,
                BarrierMasks = new()
                {
                    StageMask = VkPipelineStageFlagBits.PipelineStageBottomOfPipeBit,
                    AccessMask = 0,
                }
            });
        }
        // note: synchro affects things in submission order, so we don't need to semaphore-wait
        EndAndSubmitCommands(cl);

        var waitSemaphore = vkSwapchain.Framebuffer.UseFramebufferSemaphore();
        var presentInfo = new VkPresentInfoKHR()
        {
            swapchainCount = 1,
            pSwapchains = &deviceSwapchain,
            pImageIndices = &imageIndex,
            waitSemaphoreCount = waitSemaphore != VkSemaphore.Zero ? 1u : 0u,
            pWaitSemaphores = &waitSemaphore,
        };

        var presentLock = vkSwapchain.PresentQueueIndex == _deviceCreateState.QueueFamilyInfo.MainGraphicsFamilyIdx ? QueueLock : vkSwapchain.PresentLock;
        lock (presentLock)
        {
            var presentResult = QueuePresentKHR(vkSwapchain.PresentQueue, &presentInfo);

            if (presentResult
                is not VkResult.Success
                and not VkResult.SuboptimalKhr
                and not VkResult.ErrorOutOfDateKhr)
            {
                VulkanUtil.ThrowResult(presentResult);
            }

            _ = vkSwapchain.AcquireNextImage(Device);
        }
    }
}
