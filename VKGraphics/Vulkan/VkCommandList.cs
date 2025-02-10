using System.Diagnostics;
using System.Text;
using static VKGraphics.Vulkan.VulkanUtil;

namespace VKGraphics.Vulkan;

internal unsafe class VkCommandList : CommandList
{
    public VkCommandPool CommandPool => pool;
    public VkCommandBuffer CommandBuffer { get; private set; }

    public ResourceRefCount RefCount { get; }

    public override bool IsDisposed => destroyed;

    public override string Name
    {
        get => name;
        set
        {
            name = value;
            gd.SetResourceName(this, value);
        }
    }

    private readonly VkGraphicsDevice gd;
    private readonly List<VkTexture> preDrawSampledImages = new List<VkTexture>();

    private readonly object commandBufferListLock = new object();
    private readonly Queue<VkCommandBuffer> availableCommandBuffers = new Queue<VkCommandBuffer>();
    private readonly List<VkCommandBuffer> submittedCommandBuffers = new List<VkCommandBuffer>();
    private readonly object stagingLock = new object();
    private readonly Dictionary<VkCommandBuffer, StagingResourceInfo> submittedStagingInfos = new Dictionary<VkCommandBuffer, StagingResourceInfo>();
    private readonly List<StagingResourceInfo> availableStagingInfos = new List<StagingResourceInfo>();
    private readonly List<VkBuffer> availableStagingBuffers = new List<VkBuffer>();
    private readonly VkCommandPool pool;
    private bool destroyed;

    private bool commandBufferBegun;
    private bool commandBufferEnded;

    private VkClearValue[] clearValues = Array.Empty<VkClearValue>();
    private bool[] validColorClearValues = Array.Empty<bool>();
    private VkClearValue? depthClearValue;

    // Graphics State
    private VkFramebufferBase currentFramebuffer;
    private bool currentFramebufferEverActive;
    private VkRenderPass activeRenderPass;
    private VkPipeline currentGraphicsPipeline;
    private BoundResourceSetInfo[] currentGraphicsResourceSets = Array.Empty<BoundResourceSetInfo>();
    private bool[] graphicsResourceSetsChanged;

    private bool newFramebuffer; // Render pass cycle state

    // Compute State
    private VkPipeline currentComputePipeline;
    private BoundResourceSetInfo[] currentComputeResourceSets = Array.Empty<BoundResourceSetInfo>();
    private bool[] computeResourceSetsChanged;
    private string? name;

    private StagingResourceInfo currentStagingInfo;

    public VkCommandList(VkGraphicsDevice gd, ref CommandListDescription description)
        : base(ref description, gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
    {
        this.gd = gd;
        var poolCi = new VkCommandPoolCreateInfo();
        poolCi.flags = VkCommandPoolCreateFlagBits.CommandPoolCreateResetCommandBufferBit;
        poolCi.queueFamilyIndex = gd.GraphicsQueueIndex;
        VkCommandPool vkpool;
        var result = Vk.CreateCommandPool(this.gd.Device, &poolCi, null, &vkpool);
        pool = vkpool;
        CheckResult(result);

        CommandBuffer = getNextCommandBuffer();
        RefCount = new ResourceRefCount(disposeCore);
    }

    #region Disposal

    public override void Dispose()
    {
        RefCount.Decrement();
    }

    #endregion

    public void CommandBufferSubmitted(VkCommandBuffer cb)
    {
        RefCount.Increment();
        foreach (var rrc in currentStagingInfo.Resources)
        {
            rrc.Increment();
        }

        submittedStagingInfos.Add(cb, currentStagingInfo);
        currentStagingInfo = null;
    }

    public void CommandBufferCompleted(VkCommandBuffer completedCb)
    {
        lock (commandBufferListLock)
        {
            for (int i = 0; i < submittedCommandBuffers.Count; i++)
            {
                var submittedCb = submittedCommandBuffers[i];

                if (submittedCb == completedCb)
                {
                    availableCommandBuffers.Enqueue(completedCb);
                    submittedCommandBuffers.RemoveAt(i);
                    i -= 1;
                }
            }
        }

        lock (stagingLock)
        {
            if (submittedStagingInfos.TryGetValue(completedCb, out var info))
            {
                recycleStagingInfo(info);
                submittedStagingInfos.Remove(completedCb);
            }
        }

        RefCount.Decrement();
    }

    public override void Begin()
    {
        if (commandBufferBegun)
        {
            throw new VeldridException(
                "CommandList must be in its initial state, or End() must have been called, for Begin() to be valid to call.");
        }

        if (commandBufferEnded)
        {
            commandBufferEnded = false;
            CommandBuffer = getNextCommandBuffer();
            if (currentStagingInfo != null)
            {
                recycleStagingInfo(currentStagingInfo);
            }
        }

        currentStagingInfo = getStagingResourceInfo();

        var beginInfo = new VkCommandBufferBeginInfo
        {
            flags = VkCommandBufferUsageFlagBits.CommandBufferUsageOneTimeSubmitBit
        };
        Vk.BeginCommandBuffer(CommandBuffer, &beginInfo);
        commandBufferBegun = true;

        ClearCachedState();
        currentFramebuffer = null;
        currentGraphicsPipeline = null;
        clearSets(currentGraphicsResourceSets);

        currentComputePipeline = null;
        clearSets(currentComputeResourceSets);
    }

    public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        preDispatchCommand();

        Vk.CmdDispatch(CommandBuffer, groupCountX, groupCountY, groupCountZ);
    }

    public override void End()
    {
        if (!commandBufferBegun)
        {
            throw new VeldridException("CommandBuffer must have been started before End() may be called.");
        }

        commandBufferBegun = false;
        commandBufferEnded = true;

        if (!currentFramebufferEverActive && currentFramebuffer != null)
        {
            beginCurrentRenderPass();
        }

        if (activeRenderPass != VkRenderPass.Zero)
        {
            endCurrentRenderPass();
            currentFramebuffer!.TransitionToFinalLayout(CommandBuffer);
        }

        Vk.EndCommandBuffer(CommandBuffer);
        submittedCommandBuffers.Add(CommandBuffer);
    }

    public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
    {
        if (index == 0 || gd.Features.MultipleViewports)
        {
            var scissor = new VkRect2D(new((int)x, (int)y), new(width, height));

            Vk.CmdSetScissor(CommandBuffer, index, 1, &scissor);
        }
    }

    public override void SetViewport(uint index, ref Viewport viewport)
    {
        if (index == 0 || gd.Features.MultipleViewports)
        {
            float vpY = gd.IsClipSpaceYInverted
                ? viewport.Y
                : viewport.Height + viewport.Y;
            float vpHeight = gd.IsClipSpaceYInverted
                ? viewport.Height
                : -viewport.Height;

            var vkViewport = new VkViewport
            {
                x = viewport.X,
                y = vpY,
                width = viewport.Width,
                height = vpHeight,
                minDepth = viewport.MinDepth,
                maxDepth = viewport.MaxDepth
            };

            Vk.CmdSetViewport(CommandBuffer, index, 1, &vkViewport);
        }
    }

    internal static void CopyTextureCore_VkCommandBuffer(
        VkCommandBuffer cb,
        Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount)
    {
        var srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
        var dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);

        bool sourceIsStaging = (source.Usage & TextureUsage.Staging) == TextureUsage.Staging;
        bool destIsStaging = (destination.Usage & TextureUsage.Staging) == TextureUsage.Staging;

        if (!sourceIsStaging && !destIsStaging)
        {
            var srcSubresource = new VkImageSubresourceLayers
            {
                aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                layerCount = layerCount,
                mipLevel = srcMipLevel,
                baseArrayLayer = srcBaseArrayLayer
            };

            var dstSubresource = new VkImageSubresourceLayers
            {
                aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                layerCount = layerCount,
                mipLevel = dstMipLevel,
                baseArrayLayer = dstBaseArrayLayer
            };

            var region = new VkImageCopy
            {
                srcOffset = new VkOffset3D { x = (int)srcX, y = (int)srcY, z = (int)srcZ },
                dstOffset = new VkOffset3D { x = (int)dstX, y = (int)dstY, z = (int)dstZ },
                srcSubresource = srcSubresource,
                dstSubresource = dstSubresource,
                extent = new VkExtent3D { width = width, height = height, depth = depth }
            };

            srcVkTexture.TransitionImageLayout(
                cb,
                srcMipLevel,
                1,
                srcBaseArrayLayer,
                layerCount,
                VkImageLayout.ImageLayoutTransferSrcOptimal);

            dstVkTexture.TransitionImageLayout(
                cb,
                dstMipLevel,
                1,
                dstBaseArrayLayer,
                layerCount,
                VkImageLayout.ImageLayoutTransferDstOptimal);

            Vk.CmdCopyImage(
                cb,
                srcVkTexture.OptimalDeviceImage,
                VkImageLayout.ImageLayoutTransferSrcOptimal,
                dstVkTexture.OptimalDeviceImage,
                VkImageLayout.ImageLayoutTransferDstOptimal,
                1,
                &region);

            if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
            }

            if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
            }
        }
        else if (sourceIsStaging && !destIsStaging)
        {
            var srcBuffer = srcVkTexture.StagingBuffer;
            var srcLayout = srcVkTexture.GetSubresourceLayout(
                srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
            var dstImage = dstVkTexture.OptimalDeviceImage;
            dstVkTexture.TransitionImageLayout(
                cb,
                dstMipLevel,
                1,
                dstBaseArrayLayer,
                layerCount,
                VkImageLayout.ImageLayoutTransferDstOptimal);

            var dstSubresource = new VkImageSubresourceLayers
            {
                aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                layerCount = layerCount,
                mipLevel = dstMipLevel,
                baseArrayLayer = dstBaseArrayLayer
            };

            Util.GetMipDimensions(srcVkTexture, srcMipLevel, out uint mipWidth, out uint mipHeight, out uint _);
            uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
            uint bufferRowLength = Math.Max(mipWidth, blockSize);
            uint bufferImageHeight = Math.Max(mipHeight, blockSize);
            uint compressedX = srcX / blockSize;
            uint compressedY = srcY / blockSize;
            uint blockSizeInBytes = blockSize == 1
                ? FormatSizeHelpers.GetSizeInBytes(srcVkTexture.Format)
                : FormatHelpers.GetBlockSizeInBytes(srcVkTexture.Format);
            uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, srcVkTexture.Format);
            uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, srcVkTexture.Format);

            uint copyWidth = Math.Min(width, mipWidth);
            uint copyheight = Math.Min(height, mipHeight);

            var regions = new VkBufferImageCopy
            {
                bufferOffset = srcLayout.offset
                               + srcZ * depthPitch
                               + compressedY * rowPitch
                               + compressedX * blockSizeInBytes,
                bufferRowLength = bufferRowLength,
                bufferImageHeight = bufferImageHeight,
                imageExtent = new VkExtent3D { width = copyWidth, height = copyheight, depth = depth },
                imageOffset = new VkOffset3D { x = (int)dstX, y = (int)dstY, z = (int)dstZ },
                imageSubresource = dstSubresource
            };

            Vk.CmdCopyBufferToImage(cb, srcBuffer, dstImage, VkImageLayout.ImageLayoutTransferDstOptimal, 1, &regions);

            if ((dstVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                dstVkTexture.TransitionImageLayout(
                    cb,
                    dstMipLevel,
                    1,
                    dstBaseArrayLayer,
                    layerCount,
                    VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
            }
        }
        else if (!sourceIsStaging)
        {
            var srcImage = srcVkTexture.OptimalDeviceImage;
            srcVkTexture.TransitionImageLayout(
                cb,
                srcMipLevel,
                1,
                srcBaseArrayLayer,
                layerCount,
                VkImageLayout.ImageLayoutTransferSrcOptimal);

            var dstBuffer = dstVkTexture.StagingBuffer;

            var aspect = (srcVkTexture.Usage & TextureUsage.DepthStencil) != 0
                ? VkImageAspectFlagBits.ImageAspectDepthBit
                : VkImageAspectFlagBits.ImageAspectColorBit;

            Util.GetMipDimensions(dstVkTexture, dstMipLevel, out uint mipWidth, out uint mipHeight, out uint _);
            uint blockSize = FormatHelpers.IsCompressedFormat(srcVkTexture.Format) ? 4u : 1u;
            uint bufferRowLength = Math.Max(mipWidth, blockSize);
            uint bufferImageHeight = Math.Max(mipHeight, blockSize);
            uint compressedDstX = dstX / blockSize;
            uint compressedDstY = dstY / blockSize;
            uint blockSizeInBytes = blockSize == 1
                ? FormatSizeHelpers.GetSizeInBytes(dstVkTexture.Format)
                : FormatHelpers.GetBlockSizeInBytes(dstVkTexture.Format);
            uint rowPitch = FormatHelpers.GetRowPitch(bufferRowLength, dstVkTexture.Format);
            uint depthPitch = FormatHelpers.GetDepthPitch(rowPitch, bufferImageHeight, dstVkTexture.Format);

            var layers = stackalloc VkBufferImageCopy[(int)layerCount];

            for (uint layer = 0; layer < layerCount; layer++)
            {
                var dstLayout = dstVkTexture.GetSubresourceLayout(
                    dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer + layer));

                var srcSubresource = new VkImageSubresourceLayers
                {
                    aspectMask = aspect,
                    layerCount = 1,
                    mipLevel = srcMipLevel,
                    baseArrayLayer = srcBaseArrayLayer + layer
                };

                var region = new VkBufferImageCopy
                {
                    bufferRowLength = bufferRowLength,
                    bufferImageHeight = bufferImageHeight,
                    bufferOffset = dstLayout.offset
                                   + dstZ * depthPitch
                                   + compressedDstY * rowPitch
                                   + compressedDstX * blockSizeInBytes,
                    imageExtent = new VkExtent3D { width = width, height = height, depth = depth },
                    imageOffset = new VkOffset3D { x = (int)srcX, y = (int)srcY, z = (int)srcZ },
                    imageSubresource = srcSubresource
                };

                layers[layer] = region;
            }

            Vk.CmdCopyImageToBuffer(cb, srcImage, VkImageLayout.ImageLayoutTransferSrcOptimal, dstBuffer, layerCount, layers);

            if ((srcVkTexture.Usage & TextureUsage.Sampled) != 0)
            {
                srcVkTexture.TransitionImageLayout(
                    cb,
                    srcMipLevel,
                    1,
                    srcBaseArrayLayer,
                    layerCount,
                    VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
            }
        }
        else
        {
            Debug.Assert(sourceIsStaging && destIsStaging);
            var srcBuffer = srcVkTexture.StagingBuffer;
            var srcLayout = srcVkTexture.GetSubresourceLayout(
                srcVkTexture.CalculateSubresource(srcMipLevel, srcBaseArrayLayer));
            var dstBuffer = dstVkTexture.StagingBuffer;
            var dstLayout = dstVkTexture.GetSubresourceLayout(
                dstVkTexture.CalculateSubresource(dstMipLevel, dstBaseArrayLayer));

            uint zLimit = Math.Max(depth, layerCount);

            if (!FormatHelpers.IsCompressedFormat(source.Format))
            {
                uint pixelSize = FormatSizeHelpers.GetSizeInBytes(srcVkTexture.Format);

                for (uint zz = 0; zz < zLimit; zz++)
                {
                    for (uint yy = 0; yy < height; yy++)
                    {
                        var region = new VkBufferCopy
                        {
                            srcOffset = srcLayout.offset
                                        + srcLayout.depthPitch * (zz + srcZ)
                                        + srcLayout.rowPitch * (yy + srcY)
                                        + pixelSize * srcX,
                            dstOffset = dstLayout.offset
                                        + dstLayout.depthPitch * (zz + dstZ)
                                        + dstLayout.rowPitch * (yy + dstY)
                                        + pixelSize * dstX,
                            size = width * pixelSize
                        };

                        Vk.CmdCopyBuffer(cb, srcBuffer, dstBuffer, 1, &region);
                    }
                }
            }
            else // IsCompressedFormat
            {
                uint denseRowSize = FormatHelpers.GetRowPitch(width, source.Format);
                uint numRows = FormatHelpers.GetNumRows(height, source.Format);
                uint compressedSrcX = srcX / 4;
                uint compressedSrcY = srcY / 4;
                uint compressedDstX = dstX / 4;
                uint compressedDstY = dstY / 4;
                uint blockSizeInBytes = FormatHelpers.GetBlockSizeInBytes(source.Format);

                for (uint zz = 0; zz < zLimit; zz++)
                {
                    for (uint row = 0; row < numRows; row++)
                    {
                        var region = new VkBufferCopy
                        {
                            srcOffset = srcLayout.offset
                                        + srcLayout.depthPitch * (zz + srcZ)
                                        + srcLayout.rowPitch * (row + compressedSrcY)
                                        + blockSizeInBytes * compressedSrcX,
                            dstOffset = dstLayout.offset
                                        + dstLayout.depthPitch * (zz + dstZ)
                                        + dstLayout.rowPitch * (row + compressedDstY)
                                        + blockSizeInBytes * compressedDstX,
                            size = denseRowSize
                        };

                        Vk.CmdCopyBuffer(cb, srcBuffer, dstBuffer, 1, &region);
                    }
                }
            }
        }
    }

    protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        preDrawCommand();
        var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        currentStagingInfo.Resources.Add(vkBuffer.RefCount);
        Vk.CmdDrawIndirect(CommandBuffer, vkBuffer.DeviceBuffer, offset, drawCount, stride);
    }

    protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        preDrawCommand();
        var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        currentStagingInfo.Resources.Add(vkBuffer.RefCount);
        Vk.CmdDrawIndexedIndirect(CommandBuffer, vkBuffer.DeviceBuffer, offset, drawCount, stride);
    }

    protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
    {
        preDispatchCommand();

        var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(indirectBuffer);
        currentStagingInfo.Resources.Add(vkBuffer.RefCount);
        Vk.CmdDispatchIndirect(CommandBuffer, vkBuffer.DeviceBuffer, offset);
    }

    protected override void ResolveTextureCore(Texture source, Texture destination)
    {
        if (activeRenderPass != VkRenderPass.Zero)
        {
            endCurrentRenderPass();
        }

        var vkSource = Util.AssertSubtype<Texture, VkTexture>(source);
        currentStagingInfo.Resources.Add(vkSource.RefCount);
        var vkDestination = Util.AssertSubtype<Texture, VkTexture>(destination);
        currentStagingInfo.Resources.Add(vkDestination.RefCount);
        var aspectFlags = (source.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil
            ? VkImageAspectFlagBits.ImageAspectDepthBit | VkImageAspectFlagBits.ImageAspectStencilBit
            : VkImageAspectFlagBits.ImageAspectColorBit;
        var region = new VkImageResolve
        {
            extent = new VkExtent3D { width = source.Width, height = source.Height, depth = source.Depth },
            srcSubresource = new VkImageSubresourceLayers { layerCount = 1, aspectMask = aspectFlags },
            dstSubresource = new VkImageSubresourceLayers { layerCount = 1, aspectMask = aspectFlags }
        };

        vkSource.TransitionImageLayout(CommandBuffer, 0, 1, 0, 1, VkImageLayout.ImageLayoutTransferSrcOptimal);
        vkDestination.TransitionImageLayout(CommandBuffer, 0, 1, 0, 1, VkImageLayout.ImageLayoutTransferDstOptimal);

        Vk.CmdResolveImage(
            CommandBuffer,
            vkSource.OptimalDeviceImage,
            VkImageLayout.ImageLayoutTransferSrcOptimal,
            vkDestination.OptimalDeviceImage,
            VkImageLayout.ImageLayoutTransferDstOptimal,
            1,
            &region);

        if ((vkDestination.Usage & TextureUsage.Sampled) != 0)
        {
            vkDestination.TransitionImageLayout(CommandBuffer, 0, 1, 0, 1, VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
        }
    }

    protected override void SetFramebufferCore(Framebuffer fb)
    {
        if (activeRenderPass.Handle != VkRenderPass.Zero.Handle)
        {
            endCurrentRenderPass();
        }
        else if (!currentFramebufferEverActive && currentFramebuffer != null)
        {
            // This forces any queued up texture clears to be emitted.
            beginCurrentRenderPass();
            endCurrentRenderPass();
        }

        currentFramebuffer?.TransitionToFinalLayout(CommandBuffer);

        var vkFb = Util.AssertSubtype<Framebuffer, VkFramebufferBase>(fb);
        currentFramebuffer = vkFb;
        currentFramebufferEverActive = false;
        newFramebuffer = true;
        uint clearValueCount = (uint)vkFb.ColorTargets.Count;
        Util.EnsureArrayMinimumSize(ref clearValues, clearValueCount + 1); // Leave an extra space for the depth value (tracked separately).
        Util.ClearArray(validColorClearValues);
        Util.EnsureArrayMinimumSize(ref validColorClearValues, clearValueCount);
        currentStagingInfo.Resources.Add(vkFb.RefCount);

        //if (fb is VkSwapchainFramebuffer scFb)
        //{
        //    currentStagingInfo.Resources.Add(scFb.Swapchain.RefCount);
        //}
    }

    protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
    {
        if (!currentGraphicsResourceSets[slot].Equals(rs, dynamicOffsetsCount, ref dynamicOffsets))
        {
            currentGraphicsResourceSets[slot].Offsets.Dispose();
            currentGraphicsResourceSets[slot] = new BoundResourceSetInfo(rs, dynamicOffsetsCount, ref dynamicOffsets);
            graphicsResourceSetsChanged[slot] = true;
            Util.AssertSubtype<ResourceSet, VkResourceSet>(rs);
        }
    }

    protected override void SetComputeResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
    {
        if (!currentComputeResourceSets[slot].Equals(rs, dynamicOffsetsCount, ref dynamicOffsets))
        {
            currentComputeResourceSets[slot].Offsets.Dispose();
            currentComputeResourceSets[slot] = new BoundResourceSetInfo(rs, dynamicOffsetsCount, ref dynamicOffsets);
            computeResourceSetsChanged[slot] = true;
            Util.AssertSubtype<ResourceSet, VkResourceSet>(rs);
        }
    }

    protected override void CopyBufferCore(
        DeviceBuffer source,
        uint sourceOffset,
        DeviceBuffer destination,
        uint destinationOffset,
        uint sizeInBytes)
    {
        ensureNoRenderPass();

        var srcVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(source);
        currentStagingInfo.Resources.Add(srcVkBuffer.RefCount);
        var dstVkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(destination);
        currentStagingInfo.Resources.Add(dstVkBuffer.RefCount);

        var region = new VkBufferCopy
        {
            srcOffset = sourceOffset,
            dstOffset = destinationOffset,
            size = sizeInBytes
        };

        Vk.CmdCopyBuffer(CommandBuffer, srcVkBuffer.DeviceBuffer, dstVkBuffer.DeviceBuffer, 1, &region);

        VkMemoryBarrier barrier;
        barrier.sType = VkStructureType.StructureTypeBufferMemoryBarrier;
        barrier.srcAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
        barrier.dstAccessMask = VkAccessFlagBits.AccessVertexAttributeReadBit;
        barrier.pNext = null;
        Vk.CmdPipelineBarrier(
            CommandBuffer,
            VkPipelineStageFlagBits.PipelineStageTransferBit, VkPipelineStageFlagBits.PipelineStageVertexInputBit,
            0,
            1, &barrier,
            0, null,
            0, null);
    }

    protected override void CopyTextureCore(
        Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount)
    {
        ensureNoRenderPass();
        CopyTextureCore_VkCommandBuffer(
            CommandBuffer,
            source, srcX, srcY, srcZ, srcMipLevel, srcBaseArrayLayer,
            destination, dstX, dstY, dstZ, dstMipLevel, dstBaseArrayLayer,
            width, height, depth, layerCount);

        var srcVkTexture = Util.AssertSubtype<Texture, VkTexture>(source);
        currentStagingInfo.Resources.Add(srcVkTexture.RefCount);
        var dstVkTexture = Util.AssertSubtype<Texture, VkTexture>(destination);
        currentStagingInfo.Resources.Add(dstVkTexture.RefCount);
    }

    private VkCommandBuffer getNextCommandBuffer()
    {
        lock (commandBufferListLock)
        {
            if (availableCommandBuffers.Count > 0)
            {
                var cachedCb = availableCommandBuffers.Dequeue();
                var resetResult = Vk.ResetCommandBuffer(cachedCb, 0);
                CheckResult(resetResult);
                return cachedCb;
            }
        }

        var cbAi = new VkCommandBufferAllocateInfo();
        cbAi.commandPool = pool;
        cbAi.commandBufferCount = 1;
        cbAi.level = VkCommandBufferLevel.CommandBufferLevelPrimary;
        VkCommandBuffer cb;
        var result = Vk.AllocateCommandBuffers(gd.Device, &cbAi, &cb);
        CheckResult(result);
        return cb;
    }

    private void preDrawCommand()
    {
        transitionImages(preDrawSampledImages, VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
        preDrawSampledImages.Clear();

        ensureRenderPassActive();

        flushNewResourceSets(
            currentGraphicsResourceSets,
            graphicsResourceSetsChanged,
            currentGraphicsPipeline.ResourceSetCount,
            VkPipelineBindPoint.PipelineBindPointGraphics,
            currentGraphicsPipeline.PipelineLayout);
    }

    private void flushNewResourceSets(
        BoundResourceSetInfo[] resourceSets,
        bool[] resourceSetsChanged,
        uint resourceSetCount,
        VkPipelineBindPoint bindPoint,
        VkPipelineLayout pipelineLayout)
    {
        var pipeline = bindPoint == VkPipelineBindPoint.PipelineBindPointGraphics ? currentGraphicsPipeline : currentComputePipeline;

        var descriptorSets = stackalloc VkDescriptorSet[(int)resourceSetCount];
        uint* dynamicOffsets = stackalloc uint[(int)pipeline.DynamicOffsetsCount];
        uint currentBatchCount = 0;
        uint currentBatchFirstSet = 0;
        uint currentBatchDynamicOffsetCount = 0;

        for (uint currentSlot = 0; currentSlot < resourceSetCount; currentSlot++)
        {
            bool batchEnded = !resourceSetsChanged[currentSlot] || currentSlot == resourceSetCount - 1;

            if (resourceSetsChanged[currentSlot])
            {
                resourceSetsChanged[currentSlot] = false;
                var vkSet = Util.AssertSubtype<ResourceSet, VkResourceSet>(resourceSets[currentSlot].Set);
                descriptorSets[currentBatchCount] = vkSet.DescriptorSet;
                currentBatchCount += 1;

                ref var curSetOffsets = ref resourceSets[currentSlot].Offsets;

                for (uint i = 0; i < curSetOffsets.Count; i++)
                {
                    dynamicOffsets[currentBatchDynamicOffsetCount] = curSetOffsets.Get(i);
                    currentBatchDynamicOffsetCount += 1;
                }

                // Increment ref count on first use of a set.
                currentStagingInfo.Resources.Add(vkSet.RefCount);
                for (int i = 0; i < vkSet.RefCounts.Count; i++)
                {
                    currentStagingInfo.Resources.Add(vkSet.RefCounts[i]);
                }
            }

            if (batchEnded)
            {
                if (currentBatchCount != 0)
                {
                    // Flush current batch.
                    Vk.CmdBindDescriptorSets(
                        CommandBuffer,
                        bindPoint,
                        pipelineLayout,
                        currentBatchFirstSet,
                        currentBatchCount,
                        descriptorSets,
                        currentBatchDynamicOffsetCount,
                        dynamicOffsets);
                }

                currentBatchCount = 0;
                currentBatchFirstSet = currentSlot + 1;
            }
        }
    }

    private void transitionImages(List<VkTexture> sampledTextures, VkImageLayout layout)
    {
        for (int i = 0; i < sampledTextures.Count; i++)
        {
            var tex = sampledTextures[i];
            tex.TransitionImageLayout(CommandBuffer, 0, tex.MipLevels, 0, tex.ActualArrayLayers, layout);
        }
    }

    private void preDispatchCommand()
    {
        ensureNoRenderPass();

        for (uint currentSlot = 0; currentSlot < currentComputePipeline.ResourceSetCount; currentSlot++)
        {
            var vkSet = Util.AssertSubtype<ResourceSet, VkResourceSet>(
                currentComputeResourceSets[currentSlot].Set);

            transitionImages(vkSet.SampledTextures, VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
            transitionImages(vkSet.StorageTextures, VkImageLayout.ImageLayoutGeneral);

            for (int texIdx = 0; texIdx < vkSet.StorageTextures.Count; texIdx++)
            {
                var storageTex = vkSet.StorageTextures[texIdx];
                if ((storageTex.Usage & TextureUsage.Sampled) != 0)
                {
                    preDrawSampledImages.Add(storageTex);
                }
            }
        }

        flushNewResourceSets(
            currentComputeResourceSets,
            computeResourceSetsChanged,
            currentComputePipeline.ResourceSetCount,
            VkPipelineBindPoint.PipelineBindPointCompute,
            currentComputePipeline.PipelineLayout);
    }

    private void ensureRenderPassActive()
    {
        if (activeRenderPass == VkRenderPass.Zero)
        {
            beginCurrentRenderPass();
        }
    }

    private void ensureNoRenderPass()
    {
        if (activeRenderPass != VkRenderPass.Zero)
        {
            endCurrentRenderPass();
        }
    }

    private void beginCurrentRenderPass()
    {
        Debug.Assert(activeRenderPass == VkRenderPass.Zero);
        Debug.Assert(currentFramebuffer != null);
        currentFramebufferEverActive = true;

        uint attachmentCount = currentFramebuffer.AttachmentCount;
        bool haveAnyAttachments = currentFramebuffer.ColorTargets.Count > 0 || currentFramebuffer.DepthTarget != null;
        bool haveAllClearValues = depthClearValue.HasValue || currentFramebuffer.DepthTarget == null;
        bool haveAnyClearValues = depthClearValue.HasValue;

        for (int i = 0; i < currentFramebuffer.ColorTargets.Count; i++)
        {
            if (!validColorClearValues[i])
            {
                haveAllClearValues = false;
            }
            else
            {
                haveAnyClearValues = true;
            }
        }

        var renderPassBi = new VkRenderPassBeginInfo();
        renderPassBi.renderArea = new VkRect2D(new(), new(currentFramebuffer.RenderableWidth, currentFramebuffer.RenderableHeight));
        renderPassBi.framebuffer = currentFramebuffer.CurrentFramebuffer;

        if (!haveAnyAttachments || !haveAllClearValues)
        {
            renderPassBi.renderPass = newFramebuffer
                ? currentFramebuffer.RenderPassNoClearInit
                : currentFramebuffer.RenderPassNoClearLoad;
            Vk.CmdBeginRenderPass(CommandBuffer, &renderPassBi, VkSubpassContents.SubpassContentsInline);
            activeRenderPass = renderPassBi.renderPass;

            if (haveAnyClearValues)
            {
                if (depthClearValue.HasValue)
                {
                    ClearDepthStencilCore(depthClearValue.Value.depthStencil.depth, (byte)depthClearValue.Value.depthStencil.stencil);
                    depthClearValue = null;
                }

                for (uint i = 0; i < currentFramebuffer.ColorTargets.Count; i++)
                {
                    if (validColorClearValues[i])
                    {
                        validColorClearValues[i] = false;
                        var vkClearValue = clearValues[i];
                        var clearColor = new RgbaFloat(
                            vkClearValue.color.float32[0],
                            vkClearValue.color.float32[1],
                            vkClearValue.color.float32[2],
                            vkClearValue.color.float32[3]);
                        ClearColorTarget(i, clearColor);
                    }
                }
            }
        }
        else
        {
            // We have clear values for every attachment.
            renderPassBi.renderPass = currentFramebuffer.RenderPassClear;

            fixed (VkClearValue* clearValuesPtr = &clearValues[0])
            {
                renderPassBi.clearValueCount = attachmentCount;
                renderPassBi.pClearValues = clearValuesPtr;

                if (depthClearValue.HasValue)
                {
                    clearValues[currentFramebuffer.ColorTargets.Count] = depthClearValue.Value;
                    depthClearValue = null;
                }

                Vk.CmdBeginRenderPass(CommandBuffer, &renderPassBi, VkSubpassContents.SubpassContentsInline);
                activeRenderPass = currentFramebuffer.RenderPassClear;
                Util.ClearArray(validColorClearValues);
            }
        }

        newFramebuffer = false;
    }

    private void endCurrentRenderPass()
    {
        Debug.Assert(activeRenderPass != VkRenderPass.Zero);
        Vk.CmdEndRenderPass(CommandBuffer);
        currentFramebuffer.TransitionToIntermediateLayout(CommandBuffer);
        activeRenderPass = VkRenderPass.Zero;

        // Place a barrier between RenderPasses, so that color / depth outputs
        // can be read in subsequent passes.
        Vk.CmdPipelineBarrier(
            CommandBuffer,
            VkPipelineStageFlagBits.PipelineStageBottomOfPipeBit,
            VkPipelineStageFlagBits.PipelineStageTopOfPipeBit,
            0,
            0,
            null,
            0,
            null,
            0,
            null);
    }

    private void clearSets(BoundResourceSetInfo[] boundSets)
    {
        foreach (var boundSetInfo in boundSets)
        {
            boundSetInfo.Offsets.Dispose();
        }

        Util.ClearArray(boundSets);
    }

    [Conditional("DEBUG")]
    private void debugFullPipelineBarrier()
    {
        var memoryBarrier = new VkMemoryBarrier();
        memoryBarrier.srcAccessMask = VkAccessFlagBits.AccessIndirectCommandReadBit |
                                      VkAccessFlagBits.AccessIndexReadBit |
                                      VkAccessFlagBits.AccessVertexAttributeReadBit |
                                      VkAccessFlagBits.AccessUniformReadBit |
                                      VkAccessFlagBits.AccessInputAttachmentReadBit |
                                      VkAccessFlagBits.AccessShaderReadBit |
                                      VkAccessFlagBits.AccessShaderWriteBit |
                                      VkAccessFlagBits.AccessColorAttachmentReadBit |
                                      VkAccessFlagBits.AccessColorAttachmentWriteBit |
                                      VkAccessFlagBits.AccessDepthStencilAttachmentReadBit |
                                      VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit |
                                      VkAccessFlagBits.AccessTransferReadBit |
                                      VkAccessFlagBits.AccessTransferWriteBit |
                                      VkAccessFlagBits.AccessHostReadBit |
                                      VkAccessFlagBits.AccessHostWriteBit;
        memoryBarrier.dstAccessMask = VkAccessFlagBits.AccessIndirectCommandReadBit |
                                      VkAccessFlagBits.AccessIndexReadBit |
                                      VkAccessFlagBits.AccessVertexAttributeReadBit |
                                      VkAccessFlagBits.AccessUniformReadBit |
                                      VkAccessFlagBits.AccessInputAttachmentReadBit |
                                      VkAccessFlagBits.AccessShaderReadBit |
                                      VkAccessFlagBits.AccessShaderWriteBit |
                                      VkAccessFlagBits.AccessColorAttachmentReadBit |
                                      VkAccessFlagBits.AccessColorAttachmentWriteBit |
                                      VkAccessFlagBits.AccessDepthStencilAttachmentReadBit |
                                      VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit |
                                      VkAccessFlagBits.AccessTransferReadBit |
                                      VkAccessFlagBits.AccessTransferWriteBit |
                                      VkAccessFlagBits.AccessHostReadBit |
                                      VkAccessFlagBits.AccessHostWriteBit;

        Vk.CmdPipelineBarrier(
            CommandBuffer,
            VkPipelineStageFlagBits.PipelineStageAllCommandsBit, // srcStageMask
            VkPipelineStageFlagBits.PipelineStageAllCommandsBit, // dstStageMask
            0,
            1, // memoryBarrierCount
            &memoryBarrier, // pMemoryBarriers
            0, null,
            0, null);
    }

    private VkBuffer getStagingBuffer(uint size)
    {
        lock (stagingLock)
        {
            VkBuffer ret = null;

            foreach (var buffer in availableStagingBuffers)
            {
                if (buffer.SizeInBytes >= size)
                {
                    ret = buffer;
                    availableStagingBuffers.Remove(buffer);
                    break;
                }
            }

            if (ret == null)
            {
                ret = (VkBuffer)gd.ResourceFactory.CreateBuffer(new BufferDescription(size, BufferUsage.Staging));
                ret.Name = $"Staging Buffer (CommandList {name})";
            }

            currentStagingInfo.BuffersUsed.Add(ret);
            return ret;
        }
    }

    private void disposeCore()
    {
        if (!destroyed)
        {
            destroyed = true;
            Vk.DestroyCommandPool(gd.Device, pool, null);

            Debug.Assert(submittedStagingInfos.Count == 0);

            foreach (var buffer in availableStagingBuffers)
            {
                buffer.Dispose();
            }
        }
    }

    private StagingResourceInfo getStagingResourceInfo()
    {
        lock (stagingLock)
        {
            StagingResourceInfo ret;
            int availableCount = availableStagingInfos.Count;

            if (availableCount > 0)
            {
                ret = availableStagingInfos[availableCount - 1];
                availableStagingInfos.RemoveAt(availableCount - 1);
            }
            else
            {
                ret = new StagingResourceInfo();
            }

            return ret;
        }
    }

    private void recycleStagingInfo(StagingResourceInfo info)
    {
        lock (stagingLock)
        {
            foreach (var buffer in info.BuffersUsed)
            {
                availableStagingBuffers.Add(buffer);
            }

            foreach (var rrc in info.Resources)
            {
                rrc.Decrement();
            }

            info.Clear();

            availableStagingInfos.Add(info);
        }
    }

    private protected override void ClearColorTargetCore(uint index, RgbaFloat clearColor)
    {
        var color = new VkClearColorValue();
        color.float32[0] = clearColor.R;
        color.float32[1] = clearColor.G;
        color.float32[2] = clearColor.B;
        color.float32[3] = clearColor.A;

        var clearValue = new VkClearValue
        {
            color = color
        };

        if (activeRenderPass != VkRenderPass.Zero)
        {
            var clearAttachment = new VkClearAttachment
            {
                colorAttachment = index,
                aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                clearValue = clearValue
            };

            var colorTex = currentFramebuffer.ColorTargets[(int)index].Target;
            var clearRect = new VkClearRect
            {
                baseArrayLayer = 0,
                layerCount = 1,
                rect = new VkRect2D(new(0, 0), new(colorTex.Width, colorTex.Height))
            };

            Vk.CmdClearAttachments(CommandBuffer, 1, &clearAttachment, 1, &clearRect);
        }
        else
        {
            // Queue up the clear value for the next RenderPass.
            clearValues[index] = clearValue;
            validColorClearValues[index] = true;
        }
    }

    private protected override void ClearDepthStencilCore(float depth, byte stencil)
    {
        var clearValue = new VkClearValue { depthStencil = new VkClearDepthStencilValue(depth, stencil) };

        if (activeRenderPass != VkRenderPass.Zero)
        {
            var aspect = currentFramebuffer.DepthTarget is FramebufferAttachment depthAttachment && FormatHelpers.IsStencilFormat(depthAttachment.Target.Format)
                ? VkImageAspectFlagBits.ImageAspectDepthBit | VkImageAspectFlagBits.ImageAspectStencilBit
                : VkImageAspectFlagBits.ImageAspectDepthBit;

            var clearAttachment = new VkClearAttachment
            {
                aspectMask = aspect,
                clearValue = clearValue
            };

            uint renderableWidth = currentFramebuffer.RenderableWidth;
            uint renderableHeight = currentFramebuffer.RenderableHeight;

            if (renderableWidth > 0 && renderableHeight > 0)
            {
                var clearRect = new VkClearRect
                {
                    baseArrayLayer = 0,
                    layerCount = 1,
                    rect = new VkRect2D(new(0, 0), new(renderableWidth, renderableHeight))
                };

                Vk.CmdClearAttachments(CommandBuffer, 1, &clearAttachment, 1, &clearRect);
            }
        }
        else
        {
            // Queue up the clear value for the next RenderPass.
            depthClearValue = clearValue;
        }
    }

    private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
    {
        preDrawCommand();
        Vk.CmdDraw(CommandBuffer, vertexCount, instanceCount, vertexStart, instanceStart);
    }

    private protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        preDrawCommand();
        Vk.CmdDrawIndexed(CommandBuffer, indexCount, instanceCount, indexStart, vertexOffset, instanceStart);
    }

    private protected override void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset)
    {
        var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
        var deviceBuffer = vkBuffer.DeviceBuffer;
        ulong offset64 = offset;
        Vk.CmdBindVertexBuffers(CommandBuffer, index, 1, &deviceBuffer, &offset64);
        currentStagingInfo.Resources.Add(vkBuffer.RefCount);
    }

    private protected override void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset)
    {
        var vkBuffer = Util.AssertSubtype<DeviceBuffer, VkBuffer>(buffer);
        Vk.CmdBindIndexBuffer(CommandBuffer, vkBuffer.DeviceBuffer, offset, VkFormats.VdToVkIndexFormat(format));
        currentStagingInfo.Resources.Add(vkBuffer.RefCount);
    }

    private protected override void SetPipelineCore(Pipeline pipeline)
    {
        var vkPipeline = Util.AssertSubtype<Pipeline, VkPipeline>(pipeline);

        if (!pipeline.IsComputePipeline && currentGraphicsPipeline != pipeline)
        {
            Util.EnsureArrayMinimumSize(ref currentGraphicsResourceSets, vkPipeline.ResourceSetCount);
            clearSets(currentGraphicsResourceSets);
            Util.EnsureArrayMinimumSize(ref graphicsResourceSetsChanged, vkPipeline.ResourceSetCount);
            Vk.CmdBindPipeline(CommandBuffer, VkPipelineBindPoint.PipelineBindPointGraphics, vkPipeline.DevicePipeline);
            currentGraphicsPipeline = vkPipeline;
        }
        else if (pipeline.IsComputePipeline && currentComputePipeline != pipeline)
        {
            Util.EnsureArrayMinimumSize(ref currentComputeResourceSets, vkPipeline.ResourceSetCount);
            clearSets(currentComputeResourceSets);
            Util.EnsureArrayMinimumSize(ref computeResourceSetsChanged, vkPipeline.ResourceSetCount);
            Vk.CmdBindPipeline(CommandBuffer, VkPipelineBindPoint.PipelineBindPointCompute, vkPipeline.DevicePipeline);
            currentComputePipeline = vkPipeline;
        }

        currentStagingInfo.Resources.Add(vkPipeline.RefCount);
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        var stagingBuffer = getStagingBuffer(sizeInBytes);
        gd.UpdateBuffer(stagingBuffer, 0, source, sizeInBytes);
        CopyBuffer(stagingBuffer, 0, buffer, bufferOffsetInBytes, sizeInBytes);
    }

    private protected override void GenerateMipmapsCore(Texture texture)
    {
        ensureNoRenderPass();
        var vkTex = Util.AssertSubtype<Texture, VkTexture>(texture);
        currentStagingInfo.Resources.Add(vkTex.RefCount);

        uint layerCount = vkTex.ArrayLayers;
        if ((vkTex.Usage & TextureUsage.Cubemap) != 0)
        {
            layerCount *= 6;
        }

        uint width = vkTex.Width;
        uint height = vkTex.Height;
        uint depth = vkTex.Depth;

        for (uint level = 1; level < vkTex.MipLevels; level++)
        {
            vkTex.TransitionImageLayoutNonmatching(CommandBuffer, level - 1, 1, 0, layerCount, VkImageLayout.ImageLayoutTransferSrcOptimal);
            vkTex.TransitionImageLayoutNonmatching(CommandBuffer, level, 1, 0, layerCount, VkImageLayout.ImageLayoutTransferDstOptimal);

            var deviceImage = vkTex.OptimalDeviceImage;
            uint mipWidth = Math.Max(width >> 1, 1);
            uint mipHeight = Math.Max(height >> 1, 1);
            uint mipDepth = Math.Max(depth >> 1, 1);
            VkImageBlit region = new();

            region.srcSubresource = new VkImageSubresourceLayers
            {
                aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                baseArrayLayer = 0,
                layerCount = layerCount,
                mipLevel = level - 1
            };
            region.srcOffsets[0] = new VkOffset3D();
            region.srcOffsets[1] = new VkOffset3D { x = (int)width, y = (int)height, z = (int)depth };
            region.dstOffsets[0] = new VkOffset3D();
            region.dstSubresource = new VkImageSubresourceLayers
            {
                aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                baseArrayLayer = 0,
                layerCount = layerCount,
                mipLevel = level
            };
            region.dstOffsets[1] = new VkOffset3D { x = (int)mipWidth, y = (int)mipHeight, z = (int)mipDepth };



            Vk.CmdBlitImage(
                CommandBuffer,
                deviceImage, VkImageLayout.ImageLayoutTransferSrcOptimal,
                deviceImage, VkImageLayout.ImageLayoutTransferDstOptimal,
                1, &region,
                gd.GetFormatFilter(vkTex.VkFormat));

            width = mipWidth;
            height = mipHeight;
            depth = mipDepth;
        }

        if ((vkTex.Usage & TextureUsage.Sampled) != 0)
        {
            vkTex.TransitionImageLayoutNonmatching(CommandBuffer, 0, vkTex.MipLevels, 0, layerCount, VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
        }
    }

    private protected override void PushDebugGroupCore(string name)
    {
        var func = gd.MarkerBegin;
        if (func == null)
        {
            return;
        }

        var markerInfo = new VkDebugMarkerMarkerInfoEXT();

        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];
        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }

        utf8Ptr[byteCount] = 0;

        markerInfo.pMarkerName = utf8Ptr;

        func(CommandBuffer, &markerInfo);
    }

    private protected override void PopDebugGroupCore()
    {
        var func = gd.MarkerEnd;

        func?.Invoke(CommandBuffer);
    }

    private protected override void InsertDebugMarkerCore(string name)
    {
        var func = gd.MarkerInsert;
        if (func == null)
        {
            return;
        }

        var markerInfo = new VkDebugMarkerMarkerInfoEXT();

        int byteCount = Encoding.UTF8.GetByteCount(name);
        byte* utf8Ptr = stackalloc byte[byteCount + 1];
        fixed (char* namePtr = name)
        {
            Encoding.UTF8.GetBytes(namePtr, name.Length, utf8Ptr, byteCount);
        }

        utf8Ptr[byteCount] = 0;

        markerInfo.pMarkerName = utf8Ptr;

        func(CommandBuffer, &markerInfo);
    }

    private class StagingResourceInfo
    {
        public List<VkBuffer> BuffersUsed { get; } = new List<VkBuffer>();
        public HashSet<ResourceRefCount> Resources { get; } = new HashSet<ResourceRefCount>();

        public void Clear()
        {
            BuffersUsed.Clear();
            Resources.Clear();
        }
    }
}
