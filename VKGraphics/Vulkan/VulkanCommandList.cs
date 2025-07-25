using OpenTK.Graphics.Vulkan;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal unsafe sealed class VulkanCommandList : CommandList, IResourceRefCountTarget
{
    public VulkanGraphicsDevice Device { get; }
    private readonly VkCommandPool _pool;
    public ResourceRefCount RefCount { get; }
    public override bool IsDisposed => RefCount.IsDisposed;

    private string? _name;
    private string? _stagingBufferName;
    private string? _stagingTextureName;

    // Persistent reuse fields
    private readonly object _commandBufferListLock = new();
    private readonly Stack<VkCommandBuffer> _availableCommandBuffers = new();
    private readonly Stack<StagingResourceInfo> _availableStagingInfo = new();
    private readonly Dictionary<ISynchronizedResource, ResourceSyncInfo> _resourceSyncInfo = new();
    private readonly List<PendingBarrier> _pendingBarriers = new();
    private readonly Dictionary<VulkanSwapchainFramebuffer, VkPipelineStageFlagBits> _usedSwapchainFramebuffers = new();
    private int _pendingImageBarriers;
    private int _pendingBufferBarriers;

    private struct ResourceSyncInfo
    {
        public readonly ISynchronizedResource Resource;
        private readonly SubresourceSyncInfo[]? _srs;
        public readonly SyncSubresource SubresourceCounts;
        private SubresourceSyncInfo _sr0;
        public readonly bool IsImage;

        public ResourceSyncInfo(ISynchronizedResource resource, bool isImage)
        {
            Resource = resource;
            IsImage = isImage;
            _sr0 = default;

            SubresourceCounts = resource.SubresourceCounts;
            var srCount = ((int)SubresourceCounts.Mip * (int)SubresourceCounts.Layer) - 1;

            if (srCount > 0)
            {
                // srCount here is the number that need to go in an array
                _srs = ArrayPool<SubresourceSyncInfo>.Shared.Rent(srCount);
                _srs.AsSpan().Clear();
            }
        }

        public void Clear()
        {
            if (_srs is { } srs)
            {
                ArrayPool<SubresourceSyncInfo>.Shared.Return(srs);
            }

            this = default;
        }

        [UnscopedRef]
        public ref SubresourceSyncInfo GetSyncInfo(SyncSubresource subresource)
        {
            var index = (SubresourceCounts.Mip * subresource.Layer) + subresource.Mip;
            return ref index == 0 ? ref _sr0 : ref _srs![index - 1];
        }
    }

    private readonly record struct PendingBarrier(
        ISynchronizedResource Resource,
        SyncSubresourceRange Subresources, // we try to unify identical barriers where possible
        ResourceBarrierInfo Barrier
        );

    // Transient per-use fields
    private StagingResourceInfo _currentStagingInfo;

    // When we grab command buffers, we always actually allocate 2 of them: one for global memory sync, and the other for actual commands.
    private VkCommandBuffer _currentCb;
    private VkCommandBuffer _syncCb;

    private bool _bufferBegun;
    private bool _bufferEnded;

    // Dynamic state

    // Graphics State
    private uint _viewportCount;
    private VkViewport[] _viewports = [];
    private VkRect2D[] _scissorRects = [];
    private bool _viewportsChanged = false;
    private bool _scissorRectsChanged = false;

    private VkClearValue[] _clearValues = [];
    private bool[] _validClearValues = [];
    private VkClearValue? _depthClearValue;

    private VulkanFramebuffer? _currentFramebuffer;
    private VulkanPipeline? _currentGraphicsPipeline;
    private BoundResourceSetInfo[] _currentGraphicsResourceSets = [];
    private bool[] _graphicsResourceSetsChanged = [];
    private VkBuffer[] _vertexBindings = [];
    private VulkanBuffer[] _vertexBuffers = [];
    private VulkanBuffer? _indexBuffer;
    private ulong[] _vertexOffsets = [];
    private uint _numVertexBindings = 0;
    private bool _vertexBindingsChanged;
    private bool _currentFramebufferEverActive;
    private bool _framebufferRenderPassInstanceActive; // <-- This is true when the framebuffer's rendering context (whether a VkRenderPass or a dynamic context) is active

    // Compute State
    private VulkanPipeline? _currentComputePipeline;
    private BoundResourceSetInfo[] _currentComputeResourceSets = [];
    private bool[] _computeResourceSetsChanged = [];

    public VulkanCommandList(VulkanGraphicsDevice device, VkCommandPool pool, in CommandListDescription description)
        : base(description, device.Features, device.UniformBufferMinOffsetAlignment, device.StructuredBufferMinOffsetAlignment)
    {
        Device = device;
        _pool = pool;
        RefCount = new(this);
    }

    public override void Dispose() => RefCount?.DecrementDispose();

    void IResourceRefCountTarget.RefZeroed()
    {
        if (_currentStagingInfo.IsValid)
        {
            foreach (var resource in _currentStagingInfo.RefCounts)
            {
                resource.Decrement();
            }
        }

        DestroyCommandPool(Device.Device, _pool, null);
    }

    internal readonly struct StagingResourceInfo
    {
        public List<VulkanBuffer> BuffersUsed { get; }
        public List<VulkanTexture> TexturesUsed { get; }
        public HashSet<ResourceRefCount> RefCounts { get; }

        public bool IsValid => RefCounts != null;

        public StagingResourceInfo()
        {
            BuffersUsed = new();
            TexturesUsed = new();
            RefCounts = new();
        }

        public void AddResource(IResourceRefCountTarget resource)
        {
            AddRefCount(resource.RefCount);
        }

        public void AddRefCount(ResourceRefCount refCount)
        {
            if (RefCounts.Add(refCount))
            {
                refCount.Increment();
            }
        }

        public void Clear()
        {
            BuffersUsed.Clear();
            TexturesUsed.Clear();
            RefCounts.Clear();
        }
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _stagingBufferName = value + " (Staging Buffer)";
            _stagingTextureName = value + " (Staging Texture)";
            // TODO: staging buffer name?
            UpdateBufferNames(value);
        }
    }

    private void UpdateBufferNames(string? name)
    {
        if (Device.HasSetMarkerName)
        {
            if (_currentCb != VkCommandBuffer.Zero)
            {
                Device.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeCommandBufferExt,
                    (ulong)_currentCb.Handle, name);
            }

            if (_syncCb != VkCommandBuffer.Zero)
            {
                Device.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeCommandBufferExt,
                (ulong)_syncCb.Handle, name + " (synchronization)");
            }
        }
    }

    private VkCommandBuffer GetNextCommandBuffer()
    {
        VkCommandBuffer result = default;
        GetNextCommandBuffers(new(ref result));
        return result;
    }

    private void GetNextCommandBuffers(Span<VkCommandBuffer> buffers)
    {
        lock (_commandBufferListLock)
        {
            var needToCreate = buffers.Length;
            while (needToCreate > 0 && _availableCommandBuffers.TryPop(out var cb))
            {
                VulkanUtil.CheckResult(ResetCommandBuffer(cb, 0));
                needToCreate--;
                buffers[needToCreate] = cb;
            }
            if (needToCreate <= 0) { return; }

            // note: access to the underlying pool must be synchronized by the application
            var allocateInfo = new VkCommandBufferAllocateInfo()
            {
                commandPool = _pool,
                commandBufferCount = (uint)needToCreate,
                level = VkCommandBufferLevel.CommandBufferLevelPrimary,
            };

            fixed (VkCommandBuffer* pBuffers = buffers)
            {
                VulkanUtil.CheckResult(AllocateCommandBuffers(Device.Device, &allocateInfo, pBuffers));
            }
        }
    }

    private void ReturnCommandBuffer(VkCommandBuffer cb)
    {
        lock (_commandBufferListLock)
        {
            _availableCommandBuffers.Push(cb);
        }
    }

    private StagingResourceInfo GetStagingInfo()
    {
        if (!_availableStagingInfo.TryPop(out var result))
        {
            result = new();
        }
        return result;
    }

    private void RecycleStagingInfo(ref StagingResourceInfo stagingInfo)
    {
        if (stagingInfo.BuffersUsed.Count > 0)
        {
            Device.ReturnPooledStagingBuffers(CollectionsMarshal.AsSpan(stagingInfo.BuffersUsed));
        }
        if (stagingInfo.TexturesUsed.Count > 0)
        {
            Device.ReturnPooledStagingTextures(CollectionsMarshal.AsSpan(stagingInfo.TexturesUsed));
        }

        foreach (var refcount in stagingInfo.RefCounts)
        {
            refcount.Decrement();
        }

        stagingInfo.Clear();
        _availableStagingInfo.Push(stagingInfo);
        stagingInfo = default;
    }

    internal record struct FenceCompletionCallbackInfo(
        VulkanCommandList CommandList,
        VkCommandBuffer MainCb,
        VkCommandBuffer SyncCb,
        VkSemaphore MainCompleteSemaphore,
        StagingResourceInfo StagingResourceInfo,
        Action<VulkanCommandList>? OnSubmitCompleted,
        bool FenceWasRented
        );

    public (VkSemaphore SubmitSem, VkFence SubmitFence) SubmitToQueue(VkQueue queue, VulkanFence? submitFence, Action<VulkanCommandList>? onSubmitCompleted, VkPipelineStageFlagBits2 completionSemaphoreStages)
    {
        if (!_bufferEnded)
        {
            throw new VeldridException("Buffer must be ended to be submitted");
        }

        var cb = _currentCb;
        _currentCb = default;
        var syncCb = _syncCb;
        _syncCb = default;

        var resourceInfo = _currentStagingInfo;
        _currentStagingInfo = default;

        // now we want to do all of the actual submission work
        var mainCompleteSem = completionSemaphoreStages != 0 ? Device.GetSemaphore() : default;
        var fence = submitFence is not null ? submitFence.DeviceFence : Device.GetSubmissionFence();
        var fenceWasRented = submitFence is null;

        if (submitFence is not null)
        {
            // if we're to wait on a fence controlled by a VulkanFence, make sure to ref it
            resourceInfo.AddRefCount(submitFence.RefCount);
        }

        {
            // record the synchronization command buffer first
            var beginInfo = new VkCommandBufferBeginInfo()
            {
                flags = VkCommandBufferUsageFlagBits.CommandBufferUsageOneTimeSubmitBit,
            };
            VulkanUtil.CheckResult(BeginCommandBuffer(syncCb, &beginInfo));
            EmitInitialResourceSync(syncCb);
            VulkanUtil.CheckResult(EndCommandBuffer(syncCb));

            // now that we've finished everything we need to do with synchro, clear our dict
            _resourceSyncInfo.Clear();
            _pendingBarriers.Clear();
            _pendingImageBarriers = 0;
            _pendingBufferBarriers = 0;

            // next, we need to collect the semaphores that we need to care about for swapchain images
            var semaphoreInfo = ArrayPool<VkSemaphoreSubmitInfo>.Shared.Rent(_usedSwapchainFramebuffers.Count);
            var numSemaphores = 0;
            foreach (var (swapchain, stages) in _usedSwapchainFramebuffers)
            {
                var sem = swapchain.UseFramebufferSemaphore();
                if (sem == VkSemaphore.Zero) continue;
                semaphoreInfo[numSemaphores++] = new()
                {
                    semaphore = sem,
                    stageMask = (VkPipelineStageFlagBits2)stages,
                };
            }
            _usedSwapchainFramebuffers.Clear();

            var syncCmdSubmitInfo = new VkCommandBufferSubmitInfo()
            {
                commandBuffer = syncCb,
            };

            var mainSemaphoreInfo = new VkSemaphoreSubmitInfo()
            {
                semaphore = mainCompleteSem,
                stageMask = completionSemaphoreStages,
                // note: because our sync command buffer contains only barriers which affect submission order, we don't need to include
                // semaphores between them. In fact, on NVIDIA, those semaphores can add 1ms(!) of GPU stall.
            };

            var mainCmdSubmitInfo = new VkCommandBufferSubmitInfo()
            {
                commandBuffer = cb,
            };

            fixed (VkSemaphoreSubmitInfo* pSemSubmitInfo = semaphoreInfo)
            {
                Span<VkSubmitInfo2> submitInfos = [
                    // first, the synchronization command buffer
                    new()
                    {
                        commandBufferInfoCount = 1,
                        pCommandBufferInfos = &syncCmdSubmitInfo,
                        waitSemaphoreInfoCount = (uint)numSemaphores,
                        pWaitSemaphoreInfos = pSemSubmitInfo,
                    },
                    // then, the main command buffer
                    new()
                    {
                        commandBufferInfoCount = 1,
                        pCommandBufferInfos = &mainCmdSubmitInfo,
                        signalSemaphoreInfoCount = completionSemaphoreStages != 0 ? 1u : 0u,
                        pSignalSemaphoreInfos = &mainSemaphoreInfo,
                    },
                ];

                fixed (VkSubmitInfo2* pSubmitInfos = submitInfos)
                {
                    VulkanUtil.CheckResult(Device.vkQueueSubmit2(queue, (uint)submitInfos.Length, pSubmitInfos, fence));
                }
            }
        }

        RefCount.Increment();
        Device.RegisterFenceCompletionCallback(fence, new(this, cb, syncCb, mainCompleteSem, resourceInfo, onSubmitCompleted, fenceWasRented));

        return (mainCompleteSem, fence);
    }

    internal void OnSubmissionFenceCompleted(VkFence fence, in FenceCompletionCallbackInfo callbackInfo, bool errored)
    {
        Debug.Assert(this == callbackInfo.CommandList);

        try
        {
            // a submission finished, lets get the associated resource info and remove it from the list
            lock (_commandBufferListLock)
            {
                // return the command buffers
                _availableCommandBuffers.Push(callbackInfo.MainCb);
                _availableCommandBuffers.Push(callbackInfo.SyncCb);
            }

            // reset and return the fence as needed
            if (callbackInfo.FenceWasRented)
            {
                VulkanUtil.CheckResult(ResetFences(Device.Device, 1, &fence));
                Device.ReturnSubmissionFence(fence);
            }
            else
            {
                // if the fence wasn't rented, it's part of a VulkanFence object, and the application may still care about its state
            }

            // return the semaphores
            if (callbackInfo.MainCompleteSemaphore != VkSemaphore.Zero)
            {
                Device.ReturnSemaphore(callbackInfo.MainCompleteSemaphore);
            }

            // recycle the staging info
            var resourceInfo = callbackInfo.StagingResourceInfo;
            RecycleStagingInfo(ref resourceInfo);

            // and finally, invoke the callback if it was provided
            callbackInfo.OnSubmitCompleted?.Invoke(this);
        }
        finally
        {
            RefCount.Decrement();
        }
    }

    private static int ReadersStageIndex(VkPipelineStageFlagBits bit)
        => bit switch
        {
            VkPipelineStageFlagBits.PipelineStageVertexInputBit => 0,
            VkPipelineStageFlagBits.PipelineStageVertexShaderBit => 1,
            VkPipelineStageFlagBits.PipelineStageFragmentShaderBit => 2,
            VkPipelineStageFlagBits.PipelineStageComputeShaderBit => 3,

            // tesselation and geometry shaders are assigned the same pipeline stage. We make sure when building needed barriers that we over-allocate barriers for this.
            VkPipelineStageFlagBits.PipelineStageTessellationControlShaderBit => 4,
            VkPipelineStageFlagBits.PipelineStageTessellationEvaluationShaderBit => 4,
            VkPipelineStageFlagBits.PipelineStageGeometryShaderBit => 4,

            VkPipelineStageFlagBits.PipelineStageEarlyFragmentTestsBit => 5,
            VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit => 6,
            VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit => 7,
            VkPipelineStageFlagBits.PipelineStageTransferBit => 8,
            VkPipelineStageFlagBits.PipelineStageHostBit => 9,

            _ => -1,
        };

    private static int ReadersAccessIndex(VkAccessFlagBits bit)
        => bit switch
        {
            // All Shaders
            VkAccessFlagBits.AccessShaderReadBit => 0,
            VkAccessFlagBits.AccessShaderWriteBit => 1,
            VkAccessFlagBits.AccessUniformReadBit => 2,

            // Vertex Input
            VkAccessFlagBits.AccessVertexAttributeReadBit => 0,
            VkAccessFlagBits.AccessIndexReadBit => 1,

            // Color Attachments
            VkAccessFlagBits.AccessColorAttachmentReadBit => 0,
            VkAccessFlagBits.AccessColorAttachmentWriteBit => 1,

            // Depth Stencil
            VkAccessFlagBits.AccessDepthStencilAttachmentReadBit => 0,
            VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit => 1,

            // Transfer
            VkAccessFlagBits.AccessTransferReadBit => 0,
            VkAccessFlagBits.AccessTransferWriteBit => 1,

            // Host
            VkAccessFlagBits.AccessHostReadBit => 0,
            VkAccessFlagBits.AccessHostWriteBit => 1,

            _ => Illegal.Handle<VkAccessFlagBits, int>()
        };

    private static ReadOnlySpan<VkAccessFlagBits> ValidAccessFlags => [
        // VERTEX_INPUT
        VkAccessFlagBits.AccessVertexAttributeReadBit | VkAccessFlagBits.AccessIndexReadBit,
        // VERTEX_SHADER
        VkAccessFlagBits.AccessShaderReadBit | VkAccessFlagBits.AccessUniformReadBit,
        // FRAGMENT_SHADER
        VkAccessFlagBits.AccessShaderReadBit | VkAccessFlagBits.AccessUniformReadBit,
        // COMPUTE_SHADER
        VkAccessFlagBits.AccessShaderReadBit | VkAccessFlagBits.AccessUniformReadBit | VkAccessFlagBits.AccessShaderWriteBit,
        // TESS_CONTROL, TESS_EVAL, GEOMETRY
        VkAccessFlagBits.AccessShaderReadBit | VkAccessFlagBits.AccessUniformReadBit,
        // EARLY_FRAGMENT_TESTS
        VkAccessFlagBits.AccessDepthStencilAttachmentReadBit | VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit,
        // LATE_FRAGMENT_TESTS
        VkAccessFlagBits.AccessDepthStencilAttachmentReadBit | VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit,
        // COLOR_ATTACHMENT_OUTPUT,
        VkAccessFlagBits.AccessColorAttachmentReadBit | VkAccessFlagBits.AccessColorAttachmentWriteBit,
        // TRANSFER
        VkAccessFlagBits.AccessTransferReadBit | VkAccessFlagBits.AccessTransferWriteBit,
        // HOST
        VkAccessFlagBits.AccessHostReadBit | VkAccessFlagBits.AccessHostWriteBit,
    ];

    private static uint PerStageAccessMask(SyncBarrierMasks masks)
    {
        const int BitsPerStage = 3;

        var result = 0u;

        for (var stageMask = (uint)masks.StageMask; stageMask != 0;)
        {
            var stageBit = stageMask & ~(stageMask - 1);
            stageMask &= ~stageBit;

            var stageIndex = ReadersStageIndex((VkPipelineStageFlagBits)stageBit);
            if (stageIndex < 0) continue;
            for (var accessMask = (uint)masks.AccessMask; accessMask != 0;)
            {
                var accessBit = accessMask & ~(accessMask - 1);
                accessMask &= ~accessBit;

                if ((ValidAccessFlags[stageIndex] & (VkAccessFlagBits)accessBit) != 0)
                {
                    // this is an access we declare to be valid
                    var accessIndex = ReadersAccessIndex((VkAccessFlagBits)accessBit);
                    result |= 1u << ((BitsPerStage * stageIndex) + accessIndex);
                }
            }
        }

        return result;
    }

    internal static bool TryBuildSyncBarrier(ref SyncState state, in SyncRequest req, bool transitionFromUnknown, out ResourceBarrierInfo barrier, out bool isWrite)
    {
        const VkAccessFlagBits AllWriteAccesses =
            VkAccessFlagBits.AccessColorAttachmentWriteBit
            | VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit
            | VkAccessFlagBits.AccessHostWriteBit
            //| VkAccessFlagBits.VK_ACCESS_MEMORY_WRITE_BIT
            | VkAccessFlagBits.AccessShaderWriteBit
            | VkAccessFlagBits.AccessTransferWriteBit;

        const VkPipelineStageFlagBits TesselationStages =
            VkPipelineStageFlagBits.PipelineStageTessellationControlShaderBit
            | VkPipelineStageFlagBits.PipelineStageTessellationEvaluationShaderBit
            | VkPipelineStageFlagBits.PipelineStageGeometryShaderBit;

        var requestAccess = req.BarrierMasks.AccessMask;
        var requestStages = req.BarrierMasks.StageMask;
        if ((requestStages & TesselationStages) != 0)
        {
            // if any of the tesselation stages are set, set all of them
            requestStages |= TesselationStages;
        }

        // note: if the target layout is UNDEFINED, we treat that as "no change". If the current layout is UNDEFINED, this is the first (potential) barrier in this CL
        var needsLayoutTransition = req.Layout != 0 && (transitionFromUnknown || state.CurrentImageLayout != 0) && req.Layout != state.CurrentImageLayout;
        var writeAccesses = requestAccess & AllWriteAccesses;
        var needsWrite = writeAccesses != 0 || needsLayoutTransition;
        isWrite = needsWrite;

        barrier = new();
        if (!needsWrite)
        {
            // read-only operation.
            // These can run concurrently with other reads, but need to wait for outstanding writes.

            var perStageAccessMask = PerStageAccessMask(new() { StageMask = requestStages, AccessMask = requestAccess });
            var allAccessesHaveSeenWrite = (state.PerStageReaders & perStageAccessMask) == perStageAccessMask;

            if (state.LastWriter.StageMask != 0 && !allAccessesHaveSeenWrite)
            {
                // If there was a preceding write and the request stage hasn't consumed that write, we need a barrier.
                barrier.SrcStageMask |= state.LastWriter.StageMask;
                barrier.SrcAccess |= state.LastWriter.AccessMask & AllWriteAccesses;
            }

            // In either case, we need to add the op to the ongoing reads
            state.OngoingReaders.StageMask |= requestStages;
            state.OngoingReaders.AccessMask |= requestAccess;
            // And always mark the per-stage readers
            state.PerStageReaders |= perStageAccessMask;
        }
        else
        {
            // This operation is a write. Only one is permitteed simulatneously, and we must wait for outstanding reads.
            barrier.SrcStageMask |= state.OngoingReaders.StageMask;
            barrier.SrcAccess |= state.OngoingReaders.AccessMask;

            // after the write, there are no more up-to-date readers
            state.OngoingReaders = default;
            state.PerStageReaders = 0;

            // If there's already a pending write, wait for it as well.
            // If we already are waiting for reads, they're implicitly waiting for the previous write, so we don't need to add that dependency.
            if (barrier.SrcStageMask == 0 && state.LastWriter.StageMask != 0)
            {
                barrier.SrcStageMask |= state.LastWriter.StageMask;
                barrier.SrcAccess |= state.LastWriter.AccessMask;
            }

            // Mark our writer
            state.LastWriter = new() { StageMask = requestStages, AccessMask = requestAccess };

            // If the actual requested access was read-only, this is a pure layout transition.
            // This meeans it is also a read, and should be updated appropriately.
            if ((requestAccess & AllWriteAccesses) == 0)
            {
                state.OngoingReaders.StageMask |= requestStages;
                state.OngoingReaders.AccessMask |= requestAccess;
                state.PerStageReaders |= PerStageAccessMask(new() { StageMask = requestStages, AccessMask = requestAccess });
            }
        }

        // A barrier is needed if we have any stages to wait on, or if we need a layout transition.
        var needsBarrier = barrier.SrcStageMask != 0 || needsLayoutTransition;

        if (needsBarrier)
        {
            // if we need a barrier, populate the remaining fields for the barrier
            barrier.DstAccess = requestAccess;
            barrier.DstStageMask = requestStages;
            if (barrier.SrcStageMask == 0)
            {
                barrier.SrcStageMask = VkPipelineStageFlagBits.PipelineStageBottomOfPipeBit;
            }
            barrier.SrcLayout = state.CurrentImageLayout;
            barrier.DstLayout = req.Layout == 0 ? state.CurrentImageLayout : req.Layout;
        }

        // Finally, update the resource's image layout
        if (req.Layout != 0)
        {
            // note: a zero layout means IGNORE
            state.CurrentImageLayout = req.Layout;
        }

        return needsBarrier;
    }

    public bool SyncResource(VulkanBuffer resource, in SyncRequest req)
    {
        var realReq = req;
        realReq.Layout = 0;
        return SyncResourceCore(resource, new(0, 0, 1, 1), false, realReq);
    }

    public bool SyncResource(VulkanTexture resource, in SyncRequest req)
        => SyncResource(resource, new(0, 0, resource.ActualArrayLayers, resource.MipLevels), req);

    public bool SyncResource(VulkanTexture resource, SyncSubresourceRange subresources, in SyncRequest req)
    {
        if (resource.DeviceImage == VkImage.Zero)
        {
            // staging texture, actually a buffer
            var realReq = req;
            realReq.Layout = 0;
            return SyncResourceCore(resource, new(0, 0, 1, 1), false, realReq);
        }
        else
        {
            // real texture
            Debug.Assert(subresources.BaseLayer < resource.ActualArrayLayers);
            Debug.Assert(subresources.BaseLayer + subresources.NumLayers <= resource.ActualArrayLayers);
            Debug.Assert(subresources.BaseMip < resource.MipLevels);
            Debug.Assert(subresources.BaseMip + subresources.NumMips <= resource.MipLevels);

            return SyncResourceCore(resource, subresources, true, req);
        }
    }

    public bool SyncResource(VulkanTextureView resource, in SyncRequest req)
        => SyncResource(
            resource.Target,
            new(
                resource.BaseArrayLayer,
                resource.BaseMipLevel,
                resource.ArrayLayers * ((resource.Target.Usage & TextureUsage.Cubemap) != 0 ? 6u : 1u),
                resource.MipLevels),
            in req);

    public bool SyncResourceDyn(ISynchronizedResource resource, SyncSubresourceRange subresources, in SyncRequest req)
        => resource switch
        {
            VulkanBuffer buf => SyncResource(buf, req),
            VulkanTexture tex => SyncResource(tex, subresources, req),
            _ => Illegal.Handle<ISynchronizedResource, bool>(),
        };

    private static bool TryMergeBarriers(ref ResourceBarrierInfo targetBarrier, in ResourceBarrierInfo sourceBarrier)
    {
        if (targetBarrier == default)
        {
            // if we have no barriers for this layer yet, mark it
            targetBarrier = sourceBarrier;
            return true;
        }
        else if (targetBarrier.SrcLayout == sourceBarrier.SrcLayout && targetBarrier.DstLayout == sourceBarrier.DstLayout)
        {
            // if the new barrier has the same src/dst layouts, we can merge
            targetBarrier.SrcStageMask |= sourceBarrier.SrcStageMask;
            targetBarrier.DstStageMask |= sourceBarrier.DstStageMask;
            targetBarrier.SrcAccess |= sourceBarrier.SrcAccess;
            targetBarrier.DstAccess |= sourceBarrier.DstAccess;
            return true;
        }
        else
        {
            return false;
        }
    }

    private static bool TryMergeSubresourceRange(ref SyncSubresourceRange range, SyncSubresourceRange source)
    {
        var a = range;
        var b = source;

        if (a.NumLayers == 0)
        {
            a = a with { BaseLayer = b.BaseLayer };
        }
        if (a.NumMips == 0)
        {
            a = a with { BaseMip = b.BaseMip };
        }
        if (b.NumLayers == 0)
        {
            b = b with { BaseLayer = a.BaseLayer };
        }
        if (b.NumMips == 0)
        {
            b = b with { BaseMip = a.BaseMip };
        }

        // if one dimension matches exactly, extend the other dimension (if overlapping)
        if (a.BaseLayer == b.BaseLayer && a.NumLayers == b.NumLayers)
        {
            // expand on mip axis
            var ab = a.BaseMip;
            var bb = b.BaseMip;
            var ah = ab + a.NumMips;
            var bh = bb + b.NumMips;

            if (bh < ab || ah < bb)
            {
                // cannot perform the merge; non-overlapping regions
                return false;
            }

            // can perform the merge, overlapping regions
            var lo = uint.Min(ab, bb);
            var hi = uint.Max(ah, bh);
            range = a with { BaseMip = lo, NumMips = hi - lo };
            return true;
        }

        if (a.BaseMip == b.BaseMip && a.NumMips == b.NumMips)
        {
            // expand on mip axis
            var ab = a.BaseLayer;
            var bb = b.BaseLayer;
            var ah = ab + a.NumLayers;
            var bh = bb + b.NumLayers;

            if (bh < ab || ah < bb)
            {
                // cannot perform the merge; non-overlapping regions
                return false;
            }

            // can perform the merge, overlapping regions
            var lo = uint.Min(ab, bb);
            var hi = uint.Max(ah, bh);
            range = a with { BaseLayer = lo, NumLayers = hi - lo };
            return true;
        }

        if (a.BaseLayer <= b.BaseLayer && a.BaseMip <= b.BaseMip
            && a.BaseLayer + a.NumLayers >= b.BaseLayer + b.NumLayers
            && a.BaseMip + a.NumMips >= b.BaseMip + b.NumMips)
        {
            // a strictly-contains b, so a is the result
            range = a;
            return true;
        }

        if (b.BaseLayer <= a.BaseLayer && b.BaseMip <= a.BaseMip
            && b.BaseLayer + b.NumLayers >= a.BaseLayer + b.NumLayers
            && b.BaseMip + b.NumMips >= a.BaseMip + a.NumMips)
        {
            // b strictly-contains a, so b is the result
            range = b;
            return true;
        }

        return false;
    }

    private void EnqueueBarrier(ISynchronizedResource resource, SyncSubresourceRange subresources, in ResourceBarrierInfo barrier, bool resourceIsImage)
    {
        if (barrier == default)
        {
            // an empty barrier is meaningless
            return;
        }

        if (subresources is not { NumLayers: not 0, NumMips: not 0 })
        {
            // an empty subresource range is meaningless
            return;
        }

        _pendingBarriers.Add(new(resource, subresources, barrier));

        if (resourceIsImage)
        {
            _pendingImageBarriers++;
        }
        else
        {
            _pendingBufferBarriers++;
        }
    }

    private bool SyncResourceCore(ISynchronizedResource resource, SyncSubresourceRange subresources, bool resourceIsImage, in SyncRequest req)
    {
        if (resourceIsImage)
        {
            Debug.Assert(resource is VulkanTexture vkTex && vkTex.DeviceImage != VkImage.Zero);
        }
        else
        {
            Debug.Assert(resource is VulkanBuffer or VulkanTexture { DeviceImage.Handle: 0 });
        }

        ref var resourceSyncInfo = ref CollectionsMarshal.GetValueRefOrAddDefault(_resourceSyncInfo, resource, out var exists);
        if (!exists)
        {
            resourceSyncInfo = new(resource, resourceIsImage);
        }

        var needsAnyBarrier = false;
        var anyWrites = false;

        for (var i = 0; i < subresources.NumLayers; i++)
        {
            var array = (uint)(i + subresources.BaseLayer);

            for (var j = 0; j < subresources.NumMips; j++)
            {
                var mip = (uint)(j + subresources.BaseMip);
                var subresource = new SyncSubresource(array, mip);
                var singleSubresourceRange = new SyncSubresourceRange(array, mip, 1, 1);
                ref var localSyncInfo = ref resourceSyncInfo.GetSyncInfo(subresource);

                // when building barriers here, don't transition from UNKNOWN layout, because that indicates something that should go in the Expected layout
                if (TryBuildSyncBarrier(ref localSyncInfo.LocalState, in req, transitionFromUnknown: false, out var barrier, out var isWrite))
                {
                    // a barrier is needed for this subresource, mark it and try to update in local info
                    localSyncInfo.HasBarrier = true;
                    needsAnyBarrier = true;
                    anyWrites |= isWrite;
                    EnqueueBarrier(resource, singleSubresourceRange, barrier, resourceIsImage);
                }

                // mark the expected state appropriately
                if (!localSyncInfo.HasBarrier)
                {
                    localSyncInfo.Expected.BarrierMasks.StageMask |= req.BarrierMasks.StageMask;
                    localSyncInfo.Expected.BarrierMasks.AccessMask |= req.BarrierMasks.AccessMask;
                    // we want to make note of the first layout that we expect, so we can transition into it at the right time
                    if (localSyncInfo.Expected.Layout == 0)
                    {
                        localSyncInfo.Expected.Layout = req.Layout;
                    }
                }
            }
        }

        if (anyWrites && resource is VulkanTexture { ParentFramebuffer: { } swfb })
        {
            UseSwapchainFramebuffer(swfb, req.BarrierMasks.StageMask);
        }

        return needsAnyBarrier;
    }

    private void EmitInitialResourceSync(VkCommandBuffer cb)
    {
        Debug.Assert(_pendingBarriers.Count == 0);
        Debug.Assert(_pendingImageBarriers == 0);
        Debug.Assert(_pendingBufferBarriers == 0);

        // we're just going to reuse the existing buffers we have, because it's all we actually need
        // note: we're also under a global lock here, so it's safe to mutate the global information
        foreach (var (_, info) in _resourceSyncInfo)
        {
            var res = info.Resource;

            var anyWrites = false;
            var writeStageMasks = VkPipelineStageFlagBits.PipelineStageNone;

            // NOTE: this SHOULD be kept in sync with the above, if it needs any changes.
            for (var layer = 0u; layer < info.SubresourceCounts.Layer; layer++)
            {
                for (var mip = 0u; mip < info.SubresourceCounts.Mip; mip++)
                {
                    var subresource = new SyncSubresource(layer, mip);
                    var singleSubresourceRange = new SyncSubresourceRange(layer, mip, 1, 1);
                    ref var localState = ref info.GetSyncInfo(subresource);
                    ref var targetState = ref res.SyncStateForSubresource(subresource);

                    // when building barriers here, transition from UNKNOWN layout, because this is our last chance
                    if (TryBuildSyncBarrier(ref targetState, in localState.Expected, transitionFromUnknown: true, out var barrier, out var isWrite))
                    {
                        anyWrites |= isWrite;
                        writeStageMasks |= localState.Expected.BarrierMasks.StageMask;
                        EnqueueBarrier(res, singleSubresourceRange, barrier, info.IsImage);
                    }

                    // The resulting synchronization is not complete; it is only up to the *start* of the command buffer.
                    // We need *now* to update the global state according to the local state.
                    if (localState.LocalState.LastWriter.AccessMask != 0)
                    {
                        // there was a write in this commandlist, we need to full-overwrite
                        targetState = localState.LocalState;
                    }
                    else
                    {
                        // there was no write, just update the readers lists
                        targetState.OngoingReaders.StageMask |= localState.LocalState.OngoingReaders.StageMask;
                        targetState.OngoingReaders.AccessMask |= localState.LocalState.OngoingReaders.AccessMask;
                        targetState.PerStageReaders |= localState.LocalState.PerStageReaders;
                    }
                }
            }

            if (anyWrites && res is VulkanTexture { ParentFramebuffer: { } swfb })
            {
                UseSwapchainFramebuffer(swfb, writeStageMasks);
            }

            info.Clear();
        }

        // then emit the synchronization to the target command buffer
        EmitQueuedSynchro(cb, _pendingBarriers, inFb: false, ref _pendingImageBarriers, ref _pendingBufferBarriers);
    }

    public void EmitQueuedSynchro()
    {
        EmitQueuedSynchro(_currentCb, _pendingBarriers, inFb: _framebufferRenderPassInstanceActive, ref _pendingImageBarriers, ref _pendingBufferBarriers);
    }

    private void EmitQueuedSynchro(VkCommandBuffer cb,
        List<PendingBarrier> pendingBarriers, bool inFb,
        ref int pendingImageBarriers, ref int pendingBufferBarriers)
    {
        if (pendingBarriers.Count == 0)
        {
            Debug.Assert(pendingImageBarriers == 0);
            Debug.Assert(pendingBufferBarriers == 0);
            return;
        }

        Debug.Assert(pendingBarriers.Count == pendingImageBarriers + pendingBufferBarriers);

        if (Device._deviceCreateState.HasSync2Ext)
        {
            EmitQueuedSynchroSync2(Device, cb, pendingBarriers, inFb, ref pendingImageBarriers, ref pendingBufferBarriers);
        }
        else
        {
            EmitQueuedSynchroVk11(cb, pendingBarriers, inFb, ref pendingImageBarriers, ref pendingBufferBarriers);
        }
    }

    private bool HasPendingBarriers => _pendingBarriers.Count > 0;

    private static VkImageAspectFlagBits GetAspectForTexture(VulkanTexture tex)
    {
        if ((tex.Usage & TextureUsage.DepthStencil) != 0)
        {
            return FormatHelpers.IsStencilFormat(tex.Format)
                ? VkImageAspectFlagBits.ImageAspectDepthBit | VkImageAspectFlagBits.ImageAspectStencilBit
                : VkImageAspectFlagBits.ImageAspectDepthBit;
        }
        else
        {
            return VkImageAspectFlagBits.ImageAspectColorBit;
        }
    }

    private static void EmitQueuedSynchroSync2(VulkanGraphicsDevice device, VkCommandBuffer cb,
        List<PendingBarrier> pendingBarriers, bool inFb,
        ref int pendingImageBarriers, ref int pendingBufferBarriers)
    {
        var imgBarriers = ArrayPool<VkImageMemoryBarrier2>.Shared.Rent(pendingImageBarriers);
        var bufBarriers = ArrayPool<VkBufferMemoryBarrier2>.Shared.Rent(pendingBufferBarriers);
        var imgIdx = 0;
        var bufIdx = 0;
        foreach (var (resource, subresource, barrier) in pendingBarriers)
        {
            switch (resource)
            {
                case VulkanTexture vkTex when vkTex.DeviceImage != VkImage.Zero:
                    {
                        ref var vkBarrier = ref imgBarriers[imgIdx++];
                        vkBarrier = new()
                        {
                            srcQueueFamilyIndex = QueueFamilyIgnored,
                            dstQueueFamilyIndex = QueueFamilyIgnored,
                            srcStageMask = (VkPipelineStageFlagBits2)(uint)barrier.SrcStageMask,
                            dstStageMask = (VkPipelineStageFlagBits2)(uint)barrier.DstStageMask,
                            srcAccessMask = (VkAccessFlagBits2)(uint)barrier.SrcAccess,
                            dstAccessMask = (VkAccessFlagBits2)(uint)barrier.DstAccess,
                            oldLayout = barrier.SrcLayout,
                            newLayout = barrier.DstLayout,
                            image = vkTex.DeviceImage,
                            subresourceRange = new()
                            {
                                baseArrayLayer = subresource.BaseLayer,
                                baseMipLevel = subresource.BaseMip,
                                layerCount = subresource.NumLayers,
                                levelCount = subresource.NumMips,
                                aspectMask = GetAspectForTexture(vkTex)
                            },
                        };
                    }
                    break;

                case VulkanBuffer vkBuf:
                    var deviceBuf = vkBuf.DeviceBuffer;
                    goto AnyBuffer;
                case VulkanTexture vkTex when vkTex.StagingBuffer != VkBuffer.Zero:
                    deviceBuf = vkTex.StagingBuffer;
                    goto AnyBuffer;

                AnyBuffer:
                    {
                        ref var vkBarrier = ref bufBarriers[bufIdx++];
                        vkBarrier = new()
                        {
                            srcQueueFamilyIndex = QueueFamilyIgnored,
                            dstQueueFamilyIndex = QueueFamilyIgnored,
                            srcStageMask = (VkPipelineStageFlagBits2)(uint)barrier.SrcStageMask,
                            dstStageMask = (VkPipelineStageFlagBits2)(uint)barrier.DstStageMask,
                            srcAccessMask = (VkAccessFlagBits2)(uint)barrier.SrcAccess,
                            dstAccessMask = (VkAccessFlagBits2)(uint)barrier.DstAccess,
                            offset = 0,
                            size = WholeSize,
                            buffer = deviceBuf,
                        };
                    }
                    break;
            }
        }

        pendingBarriers.Clear();
        pendingImageBarriers = 0;
        pendingBufferBarriers = 0;

        if (bufIdx > 0 || imgIdx > 0)
        {
            fixed (VkBufferMemoryBarrier2* pBufBarriers = bufBarriers)
            fixed (VkImageMemoryBarrier2* pImgBarriers = imgBarriers)
            {
                var depInfo = new VkDependencyInfo()
                {
                    bufferMemoryBarrierCount = (uint)bufIdx,
                    pBufferMemoryBarriers = pBufBarriers,
                    imageMemoryBarrierCount = (uint)imgIdx,
                    pImageMemoryBarriers = pImgBarriers,
                    dependencyFlags = inFb ? VkDependencyFlagBits.DependencyByRegionBit : 0,
                };
                device.CmdPipelineBarrier2(cb, &depInfo);
            }
        }

        ArrayPool<VkImageMemoryBarrier2>.Shared.Return(imgBarriers);
        ArrayPool<VkBufferMemoryBarrier2>.Shared.Return(bufBarriers);
    }

    private void EmitQueuedSynchroVk11(VkCommandBuffer cb,
        List<PendingBarrier> pendingBarriers, bool inFb,
        ref int pendingImageBarriers, ref int pendingBufferBarriers)
    {
        var imgBarriers = ArrayPool<VkImageMemoryBarrier>.Shared.Rent(pendingImageBarriers);
        var bufBarriers = ArrayPool<VkBufferMemoryBarrier>.Shared.Rent(pendingBufferBarriers);
        var imgIdx = 0;
        var bufIdx = 0;

        // TODO: is there something better we can (or should) do for this?
        VkPipelineStageFlagBits srcStageMask = 0;
        VkPipelineStageFlagBits dstStageMask = 0;

        foreach (var (resource, subresource, barrier) in pendingBarriers)
        {
            switch (resource)
            {
                case VulkanTexture vkTex when vkTex.DeviceImage != VkImage.Zero:
                    {
                        ref var vkBarrier = ref imgBarriers[imgIdx++];
                        srcStageMask |= barrier.SrcStageMask;
                        dstStageMask |= barrier.DstStageMask;
                        vkBarrier = new()
                        {
                            srcQueueFamilyIndex = QueueFamilyIgnored,
                            dstQueueFamilyIndex = QueueFamilyIgnored,
                            srcAccessMask = barrier.SrcAccess,
                            dstAccessMask = barrier.DstAccess,
                            oldLayout = barrier.SrcLayout,
                            newLayout = barrier.DstLayout,
                            image = vkTex.DeviceImage,
                            subresourceRange = new()
                            {
                                baseArrayLayer = subresource.BaseLayer,
                                baseMipLevel = subresource.BaseMip,
                                layerCount = subresource.NumLayers,
                                levelCount = subresource.NumMips,
                                aspectMask = GetAspectForTexture(vkTex)
                            }
                        };
                    }
                    break;

                case VulkanBuffer vkBuf:
                    var deviceBuf = vkBuf.DeviceBuffer;
                    goto AnyBuffer;
                case VulkanTexture vkTex when vkTex.StagingBuffer != VkBuffer.Zero:
                    deviceBuf = vkTex.StagingBuffer;
                    goto AnyBuffer;

                AnyBuffer:
                    {
                        ref var vkBarrier = ref bufBarriers[bufIdx++];
                        srcStageMask |= barrier.SrcStageMask;
                        dstStageMask |= barrier.DstStageMask;
                        Debug.Assert(subresource == default);
                        vkBarrier = new()
                        {
                            srcQueueFamilyIndex = QueueFamilyIgnored,
                            dstQueueFamilyIndex = QueueFamilyIgnored,
                            srcAccessMask = barrier.SrcAccess,
                            dstAccessMask = barrier.DstAccess,
                            offset = 0,
                            size = WholeSize,
                            buffer = deviceBuf,
                        };
                    }
                    break;
            }
        }

        pendingBarriers.Clear();
        pendingImageBarriers = 0;
        pendingBufferBarriers = 0;

        if (bufIdx > 0 || imgIdx > 0)
        {
            fixed (VkBufferMemoryBarrier* pBufBarriers = bufBarriers)
            fixed (VkImageMemoryBarrier* pImgBarriers = imgBarriers)
            {
                CmdPipelineBarrier(cb,
                    srcStageMask,
                    dstStageMask,
                    inFb ? VkDependencyFlagBits.DependencyByRegionBit : 0,
                    0, null,
                    (uint)bufIdx, pBufBarriers,
                    (uint)imgIdx, pImgBarriers);
            }
        }

        ArrayPool<VkImageMemoryBarrier>.Shared.Return(imgBarriers);
        ArrayPool<VkBufferMemoryBarrier>.Shared.Return(bufBarriers);
    }

    internal void UseSwapchainFramebuffer(VulkanSwapchainFramebuffer framebuffer, VkPipelineStageFlagBits stages)
    {
        ref var usedStages = ref CollectionsMarshal.GetValueRefOrAddDefault(_usedSwapchainFramebuffers, framebuffer, out _);
        usedStages |= stages;
    }

    [DoesNotReturn]
    private static void ThrowUnreachableStateException()
    {
        throw new Exception("Implementation reached unexpected condition.");
    }

    public override void Begin()
    {
        if (_bufferBegun)
        {
            throw new VeldridException(
                "CommandList must be in its initial state, or End() must have been called, for Begin() to be valid to call.");
        }

        //if (_bufferEnded)
        {
            _bufferEnded = false;

            if (_currentCb != VkCommandBuffer.Zero)
            {
                ReturnCommandBuffer(_currentCb);
            }

            if (_syncCb != VkCommandBuffer.Zero)
            {
                ReturnCommandBuffer(_syncCb);
            }

            if (_currentStagingInfo.IsValid)
            {
                RecycleStagingInfo(ref _currentStagingInfo);
            }

            _pendingImageBarriers = 0;
            _pendingBufferBarriers = 0;
            _pendingBarriers.Clear();
            _resourceSyncInfo.Clear();
            _usedSwapchainFramebuffers.Clear();

            // We always want to pre-allocate BOTH our main command buffer AND our sync command buffer
            Span<VkCommandBuffer> requestedBuffers = [default, default];
            GetNextCommandBuffers(requestedBuffers);
            _currentCb = requestedBuffers[0];
            _syncCb = requestedBuffers[1];

            UpdateBufferNames(Name);
        }

        ClearCachedState();
        _currentFramebuffer = null;
        _currentGraphicsPipeline = null;
        _currentComputePipeline = null;
        ClearSets(_currentGraphicsResourceSets);
        ClearSets(_currentComputeResourceSets);
        _numVertexBindings = 0;
        _viewportCount = 0;
        _depthClearValue = null;
        _viewportsChanged = false;
        _scissorRectsChanged = false;
        _currentFramebufferEverActive = false;
        _framebufferRenderPassInstanceActive = false;
        Util.ClearArray(_graphicsResourceSetsChanged);
        Util.ClearArray(_computeResourceSetsChanged);
        Util.ClearArray(_clearValues);
        Util.ClearArray(_validClearValues);
        Util.ClearArray(_scissorRects);
        Util.ClearArray(_vertexBindings);
        Util.ClearArray(_vertexOffsets);
        Util.ClearArray(_viewports);
        Util.ClearArray(_scissorRects);

        _currentStagingInfo = GetStagingInfo();

        var beginInfo = new VkCommandBufferBeginInfo()
        {
            flags = VkCommandBufferUsageFlagBits.CommandBufferUsageOneTimeSubmitBit,
        };
        VulkanUtil.CheckResult(BeginCommandBuffer(_currentCb, &beginInfo));
        _bufferBegun = true;
    }

    public override void End()
    {
        if (!_bufferBegun)
        {
            throw new VeldridException("CommandBuffer must have been started before End() may be called.");
        }

        EnsureNoRenderPass();
        EmitQueuedSynchro(); // at the end of the CL, any queued synchronization needs to be emitted so we don't miss any

        _bufferBegun = false;
        _bufferEnded = true;

        VulkanUtil.CheckResult(EndCommandBuffer(_currentCb));
    }

    internal void AddStagingResource(VulkanBuffer buffer)
    {
        if (_stagingBufferName is { } bufName)
        {
            buffer.Name = bufName;
        }
        _currentStagingInfo.BuffersUsed.Add(buffer);
    }

    internal void AddStagingResource(VulkanTexture texture)
    {
        if (_stagingTextureName is { } texName)
        {
            texture.Name = texName;
        }
        _currentStagingInfo.TexturesUsed.Add(texture);
    }

    private static void ClearSets(Span<BoundResourceSetInfo> boundSets)
    {
        foreach (ref BoundResourceSetInfo boundSetInfo in boundSets)
        {
            boundSetInfo.Offsets.Dispose();
            boundSetInfo = default;
        }
    }

    protected override void ResolveTextureCore(Texture source, Texture destination)
    {
        var srcTex = Util.AssertSubtype<Texture, VulkanTexture>(source);
        var dstTex = Util.AssertSubtype<Texture, VulkanTexture>(destination);
        _currentStagingInfo.AddResource(srcTex);
        _currentStagingInfo.AddResource(dstTex);

        //PushDebugGroupCore($"ResolveTexture({srcTex.Name}, {dstTex.Name})");

        // texture resolve cannot be done in a render pass
        EnsureNoRenderPass();

        var aspectFlags = ((source.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil)
            ? VkImageAspectFlagBits.ImageAspectDepthBit | VkImageAspectFlagBits.ImageAspectStencilBit
            : VkImageAspectFlagBits.ImageAspectColorBit;
        var region = new VkImageResolve()
        {
            extent = new VkExtent3D() { width = source.Width, height = source.Height, depth = source.Depth },
            srcSubresource = new VkImageSubresourceLayers() { layerCount = 1, aspectMask = aspectFlags }, // note: only resolves layer 0 mip 0
            dstSubresource = new VkImageSubresourceLayers() { layerCount = 1, aspectMask = aspectFlags }
        };

        // generate synchro for the source and destination
        SyncResource(srcTex, new(0, 0, 1, 1), new()
        {
            Layout = VkImageLayout.ImageLayoutTransferSrcOptimal,
            BarrierMasks = new()
            {
                StageMask = VkPipelineStageFlagBits.PipelineStageTransferBit,
                AccessMask = VkAccessFlagBits.AccessTransferReadBit,
            }
        });
        SyncResource(dstTex, new(0, 0, 1, 1), new()
        {
            Layout = VkImageLayout.ImageLayoutTransferDstOptimal,
            BarrierMasks = new()
            {
                StageMask = VkPipelineStageFlagBits.PipelineStageTransferBit,
                AccessMask = VkAccessFlagBits.AccessTransferWriteBit,
            }
        });

        // make sure that it's actually emitted
        EmitQueuedSynchro();

        // then emit the actual resolve command
        CmdResolveImage(_currentCb,
            srcTex.DeviceImage,
            VkImageLayout.ImageLayoutTransferSrcOptimal,
            dstTex.DeviceImage,
            VkImageLayout.ImageLayoutTransferDstOptimal,
            1, &region);

        //PopDebugGroupCore();
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, nint source, uint sizeInBytes)
    {
        //PushDebugGroupCore($"UpdateBuffer({buffer.Name}, {sizeInBytes})");

        var stagingBuffer = Device.GetPooledStagingBuffer(sizeInBytes);
        AddStagingResource(stagingBuffer);

        Device.UpdateBuffer(stagingBuffer, 0, source, sizeInBytes);
        CopyBuffer(stagingBuffer, 0, buffer, bufferOffsetInBytes, sizeInBytes);

        //PopDebugGroupCore();
    }

    protected override void CopyBufferCore(DeviceBuffer source, DeviceBuffer destination, ReadOnlySpan<BufferCopyCommand> commands)
    {
        var srcBuf = Util.AssertSubtype<DeviceBuffer, VulkanBuffer>(source);
        var dstBuf = Util.AssertSubtype<DeviceBuffer, VulkanBuffer>(destination);
        _currentStagingInfo.AddResource(srcBuf);
        _currentStagingInfo.AddResource(dstBuf);

        //PushDebugGroupCore($"CopyBuffer({srcBuf.Name}, {dstBuf.Name})");

        EnsureNoRenderPass();

        // emit synchro for the 2 buffers
        SyncResource(srcBuf, new()
        {
            BarrierMasks = new()
            {
                StageMask = VkPipelineStageFlagBits.PipelineStageTransferBit,
                AccessMask = VkAccessFlagBits.AccessTransferReadBit,
            }
        });
        SyncResource(dstBuf, new()
        {
            BarrierMasks = new()
            {
                StageMask = VkPipelineStageFlagBits.PipelineStageTransferBit,
                AccessMask = VkAccessFlagBits.AccessTransferWriteBit,
            }
        });
        EmitQueuedSynchro();

        // then actually execute the copy
        // conveniently, BufferCopyCommand has the same layout as VkBufferCopy! We can issue a bunch of copies at once, as a result.
        fixed (BufferCopyCommand* commandPtr = commands)
        {
            int offset = 0;
            int prevOffset = 0;

            while (offset < commands.Length)
            {
                if (commands[offset].Length != 0)
                {
                    offset++;
                    continue;
                }

                int count = offset - prevOffset;
                if (count > 0)
                {
                    CmdCopyBuffer(
                        _currentCb,
                        srcBuf.DeviceBuffer,
                        dstBuf.DeviceBuffer,
                        (uint)count,
                        (VkBufferCopy*)(commandPtr + prevOffset));
                }

                while (offset < commands.Length)
                {
                    if (commands[offset].Length != 0)
                    {
                        break;
                    }
                    offset++;
                }
                prevOffset = offset;
            }

            {
                int count = offset - prevOffset;
                if (count > 0)
                {
                    CmdCopyBuffer(
                        _currentCb,
                        srcBuf.DeviceBuffer,
                        dstBuf.DeviceBuffer,
                        (uint)count,
                        (VkBufferCopy*)(commandPtr + prevOffset));
                }
            }
        }

        //PopDebugGroupCore();
    }

    protected override void CopyTextureCore(
        Texture source,
        uint srcX, uint srcY, uint srcZ, uint srcMipLevel, uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ, uint dstMipLevel, uint dstBaseArrayLayer,
        uint width, uint height, uint depth, uint layerCount)
    {
        var srcTex = Util.AssertSubtype<Texture, VulkanTexture>(source);
        var dstTex = Util.AssertSubtype<Texture, VulkanTexture>(destination);

        //PushDebugGroupCore($"CopyTexture({srcTex.Name}, {dstTex})");

        EnsureNoRenderPass();

        var srcIsStaging = (srcTex.Usage & TextureUsage.Staging) == TextureUsage.Staging;
        var dstIsStaging = (dstTex.Usage & TextureUsage.Staging) == TextureUsage.Staging;

        // before doing anything, make sure we're sync'd
        SyncResource(srcTex, new(srcBaseArrayLayer, srcMipLevel, layerCount, 1), new()
        {
            Layout = VkImageLayout.ImageLayoutTransferSrcOptimal,
            BarrierMasks = new()
            {
                StageMask = VkPipelineStageFlagBits.PipelineStageTransferBit,
                AccessMask = VkAccessFlagBits.AccessTransferReadBit,
            },
        });
        SyncResource(dstTex, new(dstBaseArrayLayer, dstMipLevel, layerCount, 1), new()
        {
            Layout = VkImageLayout.ImageLayoutTransferDstOptimal,
            BarrierMasks = new()
            {
                StageMask = VkPipelineStageFlagBits.PipelineStageTransferBit,
                AccessMask = VkAccessFlagBits.AccessTransferWriteBit,
            },
        });
        EmitQueuedSynchro();

        if (!srcIsStaging && !dstIsStaging)
        {
            // both textures are non-staging, we can issue a simple CopyImage command
            var copyRegion = new VkImageCopy()
            {
                extent = new() { width = width, height = height, depth = depth },
                srcOffset = new() { x = (int)srcX, y = (int)srcY, z = (int)srcZ },
                dstOffset = new() { x = (int)dstX, y = (int)dstY, z = (int)dstZ },
                srcSubresource = new()
                {
                    aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                    layerCount = layerCount,
                    mipLevel = srcMipLevel,
                    baseArrayLayer = srcBaseArrayLayer,
                },
                dstSubresource = new()
                {
                    aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                    layerCount = layerCount,
                    mipLevel = dstMipLevel,
                    baseArrayLayer = dstBaseArrayLayer,
                },
            };
            CmdCopyImage(_currentCb,
                srcTex.DeviceImage, VkImageLayout.ImageLayoutTransferSrcOptimal,
                dstTex.DeviceImage, VkImageLayout.ImageLayoutTransferDstOptimal,
                1, &copyRegion);
        }
        else if (srcIsStaging && !dstIsStaging)
        {
            // copy from a staging texture to a non-staging texture
            var srcBuf = srcTex.StagingBuffer;
            var srcLayout = srcTex.GetSubresourceLayout(srcMipLevel, srcBaseArrayLayer);

            VkImageSubresourceLayers dstSubresource = new()
            {
                aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                layerCount = layerCount,
                mipLevel = dstMipLevel,
                baseArrayLayer = dstBaseArrayLayer
            };

            Util.GetMipDimensions(srcTex, srcMipLevel, out var mipWidth, out var mipHeight, out _);
            var blockSize = FormatHelpers.IsCompressedFormat(srcTex.Format) ? 4u : 1u;
            var bufferRowLength = Math.Max(mipWidth, blockSize);
            var bufferImageHeight = Math.Max(mipHeight, blockSize);
            var compressedX = srcX / blockSize;
            var compressedY = srcY / blockSize;
            var blockSizeInBytes = blockSize == 1
                ? FormatSizeHelpers.GetSizeInBytes(srcTex.Format)
                : FormatHelpers.GetBlockSizeInBytes(srcTex.Format);
            var rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, srcTex.Format);
            var depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, srcTex.Format);

            var copyWidth = Math.Min(width, mipWidth);
            var copyheight = Math.Min(height, mipHeight);

            VkBufferImageCopy regions = new()
            {
                bufferOffset = srcLayout.offset
                    + (srcZ * depthPitch)
                    + (compressedY * rowPitch)
                    + (compressedX * blockSizeInBytes),
                bufferRowLength = bufferRowLength,
                bufferImageHeight = bufferImageHeight,
                imageExtent = new VkExtent3D() { width = copyWidth, height = copyheight, depth = depth },
                imageOffset = new VkOffset3D() { x = (int)dstX, y = (int)dstY, z = (int)dstZ },
                imageSubresource = dstSubresource
            };

            CmdCopyBufferToImage(_currentCb, srcBuf,
                dstTex.DeviceImage, VkImageLayout.ImageLayoutTransferDstOptimal,
                1, &regions);
        }
        else if (!srcIsStaging && dstIsStaging)
        {
            // copy from real texture to staging texture
            var srcImg = srcTex.DeviceImage;
            var dstBuf = dstTex.StagingBuffer;

            var srcAspect = (srcTex.Usage & TextureUsage.DepthStencil) != 0
                ? VkImageAspectFlagBits.ImageAspectDepthBit
                : VkImageAspectFlagBits.ImageAspectColorBit;

            Util.GetMipDimensions(dstTex, dstMipLevel, out var mipWidth, out var mipHeight);
            var blockSize = FormatHelpers.IsCompressedFormat(dstTex.Format) ? 4u : 1u;
            var bufferRowLength = Math.Max(mipWidth, blockSize);
            var bufferImageHeight = Math.Max(mipHeight, blockSize);
            var compressedDstX = dstX / blockSize;
            var compressedDstY = dstY / blockSize;
            var blockSizeInBytes = blockSize == 1
                ? FormatSizeHelpers.GetSizeInBytes(dstTex.Format)
                : FormatHelpers.GetBlockSizeInBytes(dstTex.Format);
            var rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, dstTex.Format);
            var depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, dstTex.Format);

            var layers = ArrayPool<VkBufferImageCopy>.Shared.Rent((int)layerCount);
            for (var layer = 0u; layer < layerCount; layer++)
            {
                var dstLayout = dstTex.GetSubresourceLayout(dstMipLevel, dstBaseArrayLayer + layer);

                var srcSubresource = new VkImageSubresourceLayers()
                {
                    aspectMask = srcAspect,
                    layerCount = 1,
                    mipLevel = srcMipLevel,
                    baseArrayLayer = srcBaseArrayLayer + layer
                };

                var region = new VkBufferImageCopy()
                {
                    bufferRowLength = bufferRowLength,
                    bufferImageHeight = bufferImageHeight,
                    bufferOffset = dstLayout.offset
                        + (dstZ * depthPitch)
                        + (compressedDstY * rowPitch)
                        + (compressedDstX * blockSizeInBytes),
                    imageExtent = new() { width = width, height = height, depth = depth },
                    imageOffset = new() { x = (int)srcX, y = (int)srcY, z = (int)srcZ },
                    imageSubresource = srcSubresource
                };

                layers[layer] = region;
            }

            fixed (VkBufferImageCopy* pLayers = layers)
            {
                CmdCopyImageToBuffer(_currentCb,
                    srcImg, VkImageLayout.ImageLayoutTransferSrcOptimal,
                    dstBuf,
                    layerCount, pLayers);
            }

            ArrayPool<VkBufferImageCopy>.Shared.Return(layers);
        }
        else
        {
            Debug.Assert(srcIsStaging && dstIsStaging);
            // both buffers are staging, just do a (set of) buffer->buffer copies
            var srcBuf = srcTex.StagingBuffer;
            var srcLayout = srcTex.GetSubresourceLayout(srcMipLevel, srcBaseArrayLayer);
            var dstBuf = dstTex.StagingBuffer;
            var dstLayout = dstTex.GetSubresourceLayout(dstMipLevel, dstBaseArrayLayer);

            var zLimit = uint.Max(depth, layerCount);
            var itemIndex = 0;
            VkBufferCopy[] copyRegions;
            if (!FormatHelpers.IsCompressedFormat(srcTex.Format))
            {
                // uncompressed format, emit a copy for regions according to each height value
                copyRegions = ArrayPool<VkBufferCopy>.Shared.Rent((int)(zLimit * height));
                var pixelSize = FormatSizeHelpers.GetSizeInBytes(srcTex.Format);
                for (var zz = 0u; zz < zLimit; zz++)
                {
                    for (var yy = 0u; yy < height; yy++)
                    {
                        copyRegions[itemIndex++] = new()
                        {
                            srcOffset = srcLayout.offset
                                + (srcLayout.depthPitch * (zz + srcZ))
                                + (srcLayout.rowPitch * (yy + srcY))
                                + (pixelSize * srcX),
                            dstOffset = dstLayout.offset
                                + (dstLayout.depthPitch * (zz + dstZ))
                                + (dstLayout.rowPitch * (yy + dstY))
                                + (pixelSize * dstX),
                            size = width * pixelSize,
                        };
                    }
                }
            }
            else
            {
                // compressed format, emit a copy for regions according to compressed rows
                var denseRowSize = FormatHelpers.GetRowPitch(width, source.Format);
                var numRows = FormatHelpers.GetNumRows(height, source.Format);
                var compressedSrcX = srcX / 4;
                var compressedSrcY = srcY / 4;
                var compressedDstX = dstX / 4;
                var compressedDstY = dstY / 4;
                var blockSizeInBytes = FormatHelpers.GetBlockSizeInBytes(source.Format);

                copyRegions = ArrayPool<VkBufferCopy>.Shared.Rent((int)(zLimit * numRows));
                for (var zz = 0u; zz < zLimit; zz++)
                {
                    for (var row = 0u; row < numRows; row++)
                    {
                        copyRegions[itemIndex++] = new()
                        {
                            srcOffset = srcLayout.offset
                                + (srcLayout.depthPitch * (zz + srcZ))
                                + (srcLayout.rowPitch * (row + compressedSrcY))
                                + (blockSizeInBytes * compressedSrcX),
                            dstOffset = dstLayout.offset
                                + (dstLayout.depthPitch * (zz + dstZ))
                                + (dstLayout.rowPitch * (row + compressedDstY))
                                + (blockSizeInBytes * compressedDstX),
                            size = denseRowSize
                        };
                    }
                }
            }

            // we now have a set of copy regions, now we just need to submit the copy
            fixed (VkBufferCopy* pCopyRegions = copyRegions)
            {
                CmdCopyBuffer(_currentCb, srcBuf, dstBuf, (uint)itemIndex, pCopyRegions);
            }
            ArrayPool<VkBufferCopy>.Shared.Return(copyRegions);
        }

        //PopDebugGroupCore();
    }

    private protected override void GenerateMipmapsCore(Texture texture)
    {
        var tex = Util.AssertSubtype<Texture, VulkanTexture>(texture);
        _currentStagingInfo.AddResource(tex);

        PushDebugGroupCore($"GenerateMipmaps({texture.Name})");

        EnsureNoRenderPass();

        var image = tex.DeviceImage;
        var layerCount = tex.ActualArrayLayers;

        var width = tex.Width;
        var height = tex.Height;
        var depth = tex.Depth;

        // iterate over all mip levels to generate
        for (var level = 1u; level < tex.MipLevels; level++)
        {
            var srcSubresources = new SyncSubresourceRange(0, level - 1, layerCount, 1);
            var dstSubresources = new SyncSubresourceRange(0, level, layerCount, 1);

            // synchronize appropriately
            SyncResource(tex, srcSubresources, new()
            {
                Layout = VkImageLayout.ImageLayoutTransferSrcOptimal,
                BarrierMasks = new()
                {
                    StageMask = VkPipelineStageFlagBits.PipelineStageTransferBit,
                    AccessMask = VkAccessFlagBits.AccessTransferReadBit,
                }
            });
            SyncResource(tex, dstSubresources, new()
            {
                Layout = VkImageLayout.ImageLayoutTransferDstOptimal,
                BarrierMasks = new()
                {
                    StageMask = VkPipelineStageFlagBits.PipelineStageTransferBit,
                    AccessMask = VkAccessFlagBits.AccessTransferWriteBit,
                }
            });
            EmitQueuedSynchro();

            // set up the blit command
            var mipWidth = uint.Max(width >> 1, 1);
            var mipHeight = uint.Max(height >> 1, 1);
            var mipDepth = uint.Max(depth >> 1, 1);

            var region = new VkImageBlit()
            {
                srcSubresource = new()
                {
                    aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                    baseArrayLayer = 0,
                    layerCount = layerCount,
                    mipLevel = level - 1,
                },
                dstSubresource = new()
                {
                    aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                    baseArrayLayer = 0,
                    layerCount = layerCount,
                    mipLevel = level,
                },
            };

            region.srcOffsets[0] = default;
            region.srcOffsets[1] = new() { x = (int)width, y = (int)height, z = (int)depth };
            region.dstOffsets[0] = default;
            region.dstOffsets[1] = new() { x = (int)mipWidth, y = (int)mipHeight, z = (int)mipDepth };

            CmdBlitImage(_currentCb,
                image, VkImageLayout.ImageLayoutTransferSrcOptimal,
                image, VkImageLayout.ImageLayoutTransferDstOptimal,
                1, &region,
                Device.GetFormatFilter(tex.VkFormat));

            width = mipWidth;
            height = mipHeight;
            depth = mipDepth;
        }

        PopDebugGroupCore();
    }

    private protected override void PushDebugGroupCore(ReadOnlySpan<char> name)
    {
        var CmdDebugMarkerBeginEXT = Device.CmdDebugMarkerBeginEXT;
        if (CmdDebugMarkerBeginEXT is null) return;

        var byteBuffer = (stackalloc byte[256]);
        var arr = Util.GetRentedNullTerminatedUtf8(name, ref byteBuffer);
        fixed (byte* utf8Ptr = byteBuffer)
        {
            VkDebugMarkerMarkerInfoEXT markerInfo = new()
            {
                pMarkerName = utf8Ptr
            };
            CmdDebugMarkerBeginEXT(_currentCb, &markerInfo);
        }
        if (arr is not null)
        {
            ArrayPool<byte>.Shared.Return(arr);
        }
    }

    public void PushDebugGroupCore([InterpolatedStringHandlerArgument("")] ref DebugMarkerInterpolatedStringHandler name)
    {
        if (name.HasValue)
        {
            PushDebugGroupCore(name.GetSpan());
        }
    }

    private protected override void PopDebugGroupCore()
    {
        var CmdDebugMarkerEndEXT = Device.CmdDebugMarkerEndEXT;
        if (CmdDebugMarkerEndEXT is null) return;
        CmdDebugMarkerEndEXT(_currentCb);
    }

    private protected override void InsertDebugMarkerCore(ReadOnlySpan<char> name)
    {
        var CmdDebugMarkerInsertEXT = Device.CmdDebugMarkerInsertEXT;
        if (CmdDebugMarkerInsertEXT is null) return;

        var byteBuffer = (stackalloc byte[256]);
        Util.GetNullTerminatedUtf8(name, ref byteBuffer);
        fixed (byte* utf8Ptr = byteBuffer)
        {
            VkDebugMarkerMarkerInfoEXT markerInfo = new()
            {
                pMarkerName = utf8Ptr
            };
            CmdDebugMarkerInsertEXT(_currentCb, &markerInfo);
        }
    }

    private protected override void ClearColorTargetCore(uint index, RgbaFloat clearColor)
    {
        var clearValue = new VkClearValue()
        {
            color = Unsafe.BitCast<RgbaFloat, VkClearColorValue>(clearColor)
        };

        if (!_framebufferRenderPassInstanceActive)
        {
            // We don't yet have a render pass, queue up the clear for the next render pass.
            _clearValues[index] = clearValue;
            _validClearValues[index] = true;
        }
        else
        {
            // We have a render pass, so we need to emit a CmdClearAttachments call.
            //
            // The synchronization here is strange though; We normally can't emit synchronization
            // calls within a render pass, because they're subject to some very strict rules.
            // For this case specifically, however, because we're targeting a color target of
            // the current render pass, it *should* be safe to write-sync and immediately enqueue
            // the resulting buffer.

            //PushDebugGroupCore($"ClearColorTarget({index})");

            var tgt = _currentFramebuffer!.ColorTargets[(int)index];
            var tex = Util.AssertSubtype<Texture, VulkanTexture>(tgt.Target);

            if (SyncResource(tex, new(tgt.ArrayLayer, tgt.MipLevel, 1, 1), new()
            {
                BarrierMasks = new()
                {
                    // CmdClearAttachments operats as COLOR_ATTACHMENT_OUTPUT (for color attachments)
                    StageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit,
                    AccessMask = VkAccessFlagBits.AccessColorAttachmentWriteBit,
                }
            }))
            {
                // a barrier was necessary, flush immediately
                // TODO: if we need a barrier here, we MUST exit the current render pass and use the other clear command.
                EmitQueuedSynchro();
            }

            // now that we've set up the synchro, emit the call
            var clearAttachment = new VkClearAttachment()
            {
                colorAttachment = index,
                aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                clearValue = clearValue,
            };
            var clearRect = new VkClearRect()
            {
                baseArrayLayer = 0,
                layerCount = 1,
                rect = new()
                {
                    offset = new(),
                    extent = new VkExtent2D() { width = tex.Width, height = tex.Height },
                }
            };
            // TODO: maybe we should try to batch these?
            CmdClearAttachments(_currentCb, 1, &clearAttachment, 1, &clearRect);

            //PopDebugGroupCore();
        }
    }

    private protected override void ClearDepthStencilCore(float depth, byte stencil)
    {
        var clearValue = new VkClearValue()
        {
            depthStencil = new VkClearDepthStencilValue()
            {
                depth = depth,
                stencil = stencil
            }
        };

        if (!_framebufferRenderPassInstanceActive)
        {
            // no render pass is active, queue it up for when we start it
            _depthClearValue = clearValue;
        }
        else
        {
            //PushDebugGroupCore($"ClearDepthStencil()");

            // A render pass is currently active. All of the same caveats apply as in ClearColorTargetCore.
            var renderableExtent = _currentFramebuffer!.RenderableExtent;
            if (renderableExtent.width > 0 && renderableExtent.height > 0)
            {
                var tgt = _currentFramebuffer!.DepthTarget!.Value;
                var tex = Util.AssertSubtype<Texture, VulkanTexture>(tgt.Target);

                if (SyncResource(tex, new(tgt.ArrayLayer, tgt.MipLevel, 1, 1), new()
                {
                    BarrierMasks = new()
                    {
                        // CmdClearAttachments operates as EARLY_FRAGMENT_TESTS and LATE_FRAGMENT_TESTS (for depth and stencil attachments)
                        StageMask = VkPipelineStageFlagBits.PipelineStageEarlyFragmentTestsBit
                                    | VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit,
                        AccessMask = VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit,
                    }
                }))
                {
                    // a barrier was necessary, flush immediately
                    EmitQueuedSynchro();
                }

                var aspect = FormatHelpers.IsStencilFormat(tex.Format)
                    ? VkImageAspectFlagBits.ImageAspectDepthBit | VkImageAspectFlagBits.ImageAspectStencilBit
                    : VkImageAspectFlagBits.ImageAspectDepthBit;

                var clearAttachment = new VkClearAttachment()
                {
                    aspectMask = aspect,
                    clearValue = clearValue
                };
                var clearRect = new VkClearRect()
                {
                    baseArrayLayer = 0,
                    layerCount = 1,
                    rect = new()
                    {
                        offset = new(),
                        extent = renderableExtent,
                    }
                };
                CmdClearAttachments(_currentCb, 1, &clearAttachment, 1, &clearRect);
            }

            //PopDebugGroupCore();
        }
    }


    protected override void SetFramebufferCore(Framebuffer fb)
    {
        //PushDebugGroupCore($"SetFramebuffer({{ {fb.Name} }})");

        var vkFbb = Util.AssertSubtype<Framebuffer, VulkanFramebufferBase>(fb);
        _currentStagingInfo.AddRefCount(vkFbb.RefCount);

        // the framebuffer we actually want to bind to is the real "current framebuffer"
        // For user-created framebufferws, it will be exactly the one passed in.
        // For swapchain framebuffers, it will be the active buffer from the swapchain.
        var vkFb = vkFbb.CurrentFramebuffer;
        _currentStagingInfo.AddRefCount(vkFb.RefCount);

        // finish any active render pass
        EnsureNoRenderPass();

        // then set up the new target framebuffer
        _currentFramebuffer = vkFb;
        _currentFramebufferEverActive = false;

        _viewportCount = uint.Max(1, (uint)vkFb.ColorTargets.Length);
        Util.EnsureArrayMinimumSize(ref _viewports, _viewportCount);
        Util.EnsureArrayMinimumSize(ref _scissorRects, _viewportCount);
        Util.ClearArray(_viewports);
        Util.ClearArray(_scissorRects);
        _viewportsChanged = false;
        _scissorRectsChanged = false;

        var clearValueCount = (uint)vkFb.ColorTargets.Length;
        Util.EnsureArrayMinimumSize(ref _clearValues, clearValueCount);
        Util.EnsureArrayMinimumSize(ref _validClearValues, clearValueCount);
        Util.ClearArray(_clearValues);
        Util.ClearArray(_validClearValues);
        _depthClearValue = null;

        //PopDebugGroupCore();
    }

    public override void SetViewport(uint index, in Viewport viewport)
    {
        var yInverted = Device.IsClipSpaceYInverted;
        var vpY = yInverted
            ? viewport.Y
            : viewport.Height + viewport.Y;
        var vpHeight = yInverted
            ? viewport.Height
            : -viewport.Height;

        _viewportsChanged = true;
        _viewports[index] = new VkViewport()
        {
            x = viewport.X,
            y = vpY,
            width = viewport.Width,
            height = vpHeight,
            minDepth = viewport.MinDepth,
            maxDepth = viewport.MaxDepth
        };
    }

    public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
    {
        var scissor = new VkRect2D()
        {
            offset = new VkOffset2D() { x = (int)x, y = (int)y },
            extent = new VkExtent2D() { width = width, height = height }
        };

        var scissorRects = _scissorRects;
        if (scissorRects[index].offset.x != scissor.offset.x ||
            scissorRects[index].offset.y != scissor.offset.y ||
            scissorRects[index].extent.width != scissor.extent.width ||
            scissorRects[index].extent.height != scissor.extent.height)
        {
            _scissorRectsChanged = true;
            scissorRects[index] = scissor;
        }
    }

    private protected override void SetPipelineCore(Pipeline pipeline)
    {
        var vkPipeline = Util.AssertSubtype<Pipeline, VulkanPipeline>(pipeline);
        _currentStagingInfo.AddResource(vkPipeline);

        if (!vkPipeline.IsComputePipeline)
        {
            // this is a graphics pipeline
            if (_currentGraphicsPipeline == vkPipeline) return; // no work to do

            // the graphics pipeline changed, resize everything and bind the pipeline
            var resourceCount = vkPipeline.ResourceSetCount;
            Util.EnsureArrayMinimumSize(ref _currentGraphicsResourceSets, resourceCount);
            Util.EnsureArrayMinimumSize(ref _graphicsResourceSetsChanged, resourceCount);
            ClearSets(_currentGraphicsResourceSets);

            var vertexBufferCount = vkPipeline.VertexLayoutCount;
            Util.EnsureArrayMinimumSize(ref _vertexBindings, vertexBufferCount);
            Util.EnsureArrayMinimumSize(ref _vertexBuffers, vertexBufferCount);
            Util.EnsureArrayMinimumSize(ref _vertexOffsets, vertexBufferCount);

            CmdBindPipeline(_currentCb, VkPipelineBindPoint.PipelineBindPointGraphics, vkPipeline.DevicePipeline);
            _currentGraphicsPipeline = vkPipeline;
        }
        else
        {
            // this is a compute pipeline
            if (_currentComputePipeline == vkPipeline) return; // no work to do

            // the compute pipeline changed, resize everything and bind it
            var resourceCount = vkPipeline.ResourceSetCount;
            Util.EnsureArrayMinimumSize(ref _currentComputeResourceSets, resourceCount);
            Util.EnsureArrayMinimumSize(ref _computeResourceSetsChanged, resourceCount);
            ClearSets(_currentComputeResourceSets);

            CmdBindPipeline(_currentCb, VkPipelineBindPoint.PipelineBindPointCompute, vkPipeline.DevicePipeline);
            _currentComputePipeline = vkPipeline;
        }
    }

    private protected override void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset)
    {
        var vkBuffer = Util.AssertSubtype<DeviceBuffer, VulkanBuffer>(buffer);

        Util.EnsureArrayMinimumSize(ref _vertexBindings, index + 1);
        Util.EnsureArrayMinimumSize(ref _vertexBuffers, index + 1);
        Util.EnsureArrayMinimumSize(ref _vertexOffsets, index + 1);

        var bufferChanged = _vertexBindings[index] != vkBuffer.DeviceBuffer;
        if (bufferChanged || _vertexOffsets[index] != offset)
        {
            _vertexBindingsChanged = true;
            if (bufferChanged)
            {
                _currentStagingInfo.AddResource(vkBuffer);
                _vertexBuffers[index] = vkBuffer;
                _vertexBindings[index] = vkBuffer.DeviceBuffer;
            }

            _vertexOffsets[index] = offset;
            _numVertexBindings = uint.Max(index + 1, _numVertexBindings);
        }
    }

    private protected override void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset)
    {
        var vkBuffer = Util.AssertSubtype<DeviceBuffer, VulkanBuffer>(buffer);
        _currentStagingInfo.AddResource(vkBuffer);
        _indexBuffer = vkBuffer;

        CmdBindIndexBuffer(_currentCb, vkBuffer.DeviceBuffer, offset, Vulkan.VkFormats.VdToVkIndexFormat(format));
    }

    protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, ReadOnlySpan<uint> dynamicOffsets)
    {
        ref BoundResourceSetInfo set = ref _currentGraphicsResourceSets[slot];
        if (!set.Equals(rs, dynamicOffsets))
        {
            set.Offsets.Dispose();
            set = new BoundResourceSetInfo(rs, dynamicOffsets);
            _graphicsResourceSetsChanged[slot] = true;
            Util.AssertSubtype<ResourceSet, VulkanResourceSet>(rs);
        }
    }

    protected override void SetComputeResourceSetCore(uint slot, ResourceSet rs, ReadOnlySpan<uint> dynamicOffsets)
    {
        ref BoundResourceSetInfo set = ref _currentComputeResourceSets[slot];
        if (!set.Equals(rs, dynamicOffsets))
        {
            set.Offsets.Dispose();
            set = new BoundResourceSetInfo(rs, dynamicOffsets);
            _computeResourceSetsChanged[slot] = true;
            Util.AssertSubtype<ResourceSet, VulkanResourceSet>(rs);
        }
    }

    private void EnsureRenderPass()
    {
        if (_framebufferRenderPassInstanceActive)
        {
            // the render pass is already active, nothing to do
            return;
        }

        Debug.Assert(_currentFramebuffer is not null);
        _framebufferRenderPassInstanceActive = true;
        _currentFramebufferEverActive = true;

        _currentFramebuffer.StartRenderPass(this, _currentCb,
            firstBinding: !_currentFramebufferEverActive,
            _depthClearValue, _clearValues, _validClearValues);
        // after we've started a render pass, the clear values are done and we need to make sure to not double-clear
        _validClearValues.AsSpan().Fill(false);
        _depthClearValue = null;
    }

    private bool EnsureNoRenderPass(bool forCreateRenderPass = false)
    {
        if (_framebufferRenderPassInstanceActive)
        {
            // we have a render pass to end, end that
            Debug.Assert(_currentFramebufferEverActive);
            _currentFramebuffer!.EndRenderPass(this, _currentCb);
            _framebufferRenderPassInstanceActive = false;
            return true;
        }

        if (!forCreateRenderPass && !_currentFramebufferEverActive && _currentFramebuffer is not null)
        {
            // we do this to flush color clears
            EnsureRenderPass();
            _framebufferRenderPassInstanceActive = true;
            _currentFramebuffer.EndRenderPass(this, _currentCb);
            _framebufferRenderPassInstanceActive = false;
            return true;
        }

        return false;
    }

    private void SyncBoundResources(BoundResourceSetInfo[] resourceSets, uint resourceCount)
    {
        // sync all bound resources
        for (int i = 0; i < resourceCount; i++)
        {
            BoundResourceSetInfo set = resourceSets[i];
            var vkSet = Util.AssertSubtype<ResourceSet, VulkanResourceSet>(set.Set);

            foreach (var buf in vkSet.Buffers)
            {
                _ = SyncResource(buf.Resource, buf.Request);
            }

            foreach (var buf in vkSet.Textures)
            {
                _ = SyncResource(buf.Resource, buf.Request);
            }
        }
    }

    private void SyncIndexAndVertexBuffers()
    {
        if (_indexBuffer is { } idxBuf)
        {
            _ = SyncResource(idxBuf, new()
            {
                BarrierMasks =
                {
                    StageMask = VkPipelineStageFlagBits.PipelineStageVertexInputBit,
                    AccessMask = VkAccessFlagBits.AccessIndexReadBit,
                }
            });
        }

        foreach (var buf in _vertexBuffers)
        {
            _ = SyncResource(buf, new()
            {
                BarrierMasks =
                {
                    StageMask = VkPipelineStageFlagBits.PipelineStageVertexInputBit,
                    AccessMask = VkAccessFlagBits.AccessVertexAttributeReadBit,
                }
            });
        }
    }

    private void PreDrawCommand()
    {
        if (_viewportsChanged)
        {
            _viewportsChanged = false;

            var count = _viewportCount;
            if (count > 1 && !Device.Features.MultipleViewports)
            {
                count = 1;
            }

            fixed (VkViewport* viewports = _viewports)
            {
                CmdSetViewport(_currentCb, 0, count, viewports);
            }
        }

        if (_scissorRectsChanged)
        {
            _scissorRectsChanged = false;

            var count = _viewportCount;
            if (count > 1 && !Device.Features.MultipleViewports)
            {
                count = 1;
            }

            fixed (VkRect2D* scissorRects = _scissorRects)
            {
                CmdSetScissor(_currentCb, 0, count, scissorRects);
            }
        }

        SyncBoundResources(_currentGraphicsResourceSets, _currentGraphicsPipeline!.ResourceSetCount);
        SyncIndexAndVertexBuffers();

        if (HasPendingBarriers)
        {
            // if we now have pending barriers, make sure we're not in a render pass so we can sync
            EnsureNoRenderPass(forCreateRenderPass: true);
        }

        if (_vertexBindingsChanged)
        {
            _vertexBindingsChanged = false;

            fixed (VkBuffer* vertexBindings = _vertexBindings)
            fixed (ulong* vertexOffsets = _vertexOffsets)
            {
                CmdBindVertexBuffers(
                    _currentCb,
                    0, _numVertexBindings,
                    vertexBindings,
                    vertexOffsets);
            }
        }

        EnsureRenderPass();

        Debug.Assert(!HasPendingBarriers);

        FlushNewResourceSets(
            _currentGraphicsResourceSets,
            _graphicsResourceSetsChanged,
            _currentGraphicsPipeline);
    }

    private void FlushNewResourceSets(
        BoundResourceSetInfo[] resourceSets,
        bool[] resourceSetsChanged,
        VulkanPipeline pipeline)
    {
        int resourceSetCount = (int)pipeline.ResourceSetCount;

        var bindPoint = pipeline.IsComputePipeline
            ? VkPipelineBindPoint.PipelineBindPointCompute
            : VkPipelineBindPoint.PipelineBindPointGraphics;

        // TODO: make sure this is relatively small?
        var descriptorSets = stackalloc VkDescriptorSet[resourceSetCount];
        var dynamicOffsets = stackalloc uint[pipeline.DynamicOffsetsCount];
        var currentBatchCount = 0u;
        var currentBatchFirstSet = 0u;
        var currentBatchDynamicOffsetCount = 0u;

        var sets = resourceSets.AsSpan(0, resourceSetCount);
        var setsChanged = resourceSetsChanged.AsSpan(0, resourceSetCount);

        for (int currentSlot = 0; currentSlot < resourceSetCount; currentSlot++)
        {
            bool batchEnded = !setsChanged[currentSlot] || currentSlot == resourceSetCount - 1;

            if (setsChanged[currentSlot])
            {
                setsChanged[currentSlot] = false;
                ref var resourceSet = ref sets[currentSlot];
                var vkSet = Util.AssertSubtype<ResourceSet, VulkanResourceSet>(resourceSet.Set);
                descriptorSets[currentBatchCount] = vkSet.DescriptorSet;
                currentBatchCount += 1;

                ref SmallFixedOrDynamicArray curSetOffsets = ref resourceSet.Offsets;
                for (uint i = 0; i < curSetOffsets.Count; i++)
                {
                    dynamicOffsets[currentBatchDynamicOffsetCount] = curSetOffsets.Get(i);
                    currentBatchDynamicOffsetCount += 1;
                }

                // Increment ref count on first use of a set.
                _currentStagingInfo.AddResource(vkSet);
                foreach (var rc in vkSet.RefCounts)
                {
                    _currentStagingInfo.AddRefCount(rc);
                }
            }

            if (batchEnded)
            {
                if (currentBatchCount != 0)
                {
                    // Flush current batch.
                    CmdBindDescriptorSets(
                        _currentCb,
                        bindPoint,
                        pipeline.PipelineLayout,
                        currentBatchFirstSet,
                        currentBatchCount,
                        descriptorSets,
                        currentBatchDynamicOffsetCount,
                        dynamicOffsets);
                }

                currentBatchCount = 0;
                currentBatchFirstSet = (uint)(currentSlot + 1);
            }
        }
    }

    private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
    {
        //PushDebugGroupCore($"Draw({vertexCount}, {instanceCount})");

        PreDrawCommand();
        CmdDraw(_currentCb, vertexCount, instanceCount, vertexStart, instanceStart);

        //PopDebugGroupCore();
    }

    private protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        //PushDebugGroupCore($"DrawIndexed({indexCount}, {instanceCount})");

        PreDrawCommand();
        CmdDrawIndexed(_currentCb, indexCount, instanceCount, indexStart, vertexOffset, instanceStart);

        //PopDebugGroupCore();
    }

    protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        var buffer = Util.AssertSubtype<DeviceBuffer, VulkanBuffer>(indirectBuffer);
        _currentStagingInfo.AddResource(buffer);

        //PushDebugGroupCore($"DrawIndirect({indirectBuffer.Name}, {drawCount})");

        _ = SyncResource(buffer, new()
        {
            BarrierMasks =
            {
                StageMask = VkPipelineStageFlagBits.PipelineStageDrawIndirectBit,
                AccessMask = VkAccessFlagBits.AccessIndirectCommandReadBit,
            }
        });

        PreDrawCommand();
        CmdDrawIndirect(_currentCb, buffer.DeviceBuffer, offset, drawCount, stride);

        //PopDebugGroupCore();
    }

    protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        var buffer = Util.AssertSubtype<DeviceBuffer, VulkanBuffer>(indirectBuffer);
        _currentStagingInfo.AddResource(buffer);

        //PushDebugGroupCore($"DrawIndexedIndirect({indirectBuffer.Name}, {drawCount})");

        _ = SyncResource(buffer, new()
        {
            BarrierMasks =
            {
                StageMask = VkPipelineStageFlagBits.PipelineStageDrawIndirectBit,
                AccessMask = VkAccessFlagBits.AccessIndirectCommandReadBit,
            }
        });

        PreDrawCommand();
        CmdDrawIndexedIndirect(_currentCb, buffer.DeviceBuffer, offset, drawCount, stride);

        //PopDebugGroupCore();
    }

    private void PreDispatchCommand()
    {
        EnsureNoRenderPass();

        SyncBoundResources(_currentComputeResourceSets, _currentComputePipeline!.ResourceSetCount);
        EmitQueuedSynchro();

        FlushNewResourceSets(
            _currentComputeResourceSets,
            _computeResourceSetsChanged,
            _currentComputePipeline);
    }

    public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        //PushDebugGroupCore($"Dispatch({groupCountX}, {groupCountY}, {groupCountZ})");

        PreDispatchCommand();
        CmdDispatch(_currentCb, groupCountX, groupCountY, groupCountZ);

        //PopDebugGroupCore();
    }

    protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
    {
        var buffer = Util.AssertSubtype<DeviceBuffer, VulkanBuffer>(indirectBuffer);
        _currentStagingInfo.AddResource(buffer);

        //PushDebugGroupCore($"DispatchIndirect({indirectBuffer.Name})");

        _ = SyncResource(buffer, new()
        {
            BarrierMasks =
            {
                StageMask = VkPipelineStageFlagBits.PipelineStageDrawIndirectBit,
                AccessMask = VkAccessFlagBits.AccessIndirectCommandReadBit,
            }
        });

        PreDispatchCommand();
        CmdDispatchIndirect(_currentCb, buffer.DeviceBuffer, offset);

        //PopDebugGroupCore();
    }
}
