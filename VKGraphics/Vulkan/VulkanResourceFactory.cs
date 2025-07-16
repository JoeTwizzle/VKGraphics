using OpenTK.Graphics.Vulkan;
using OpenTK.Platform;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using static OpenTK.Graphics.Vulkan.Vk;


namespace VKGraphics.Vulkan;

internal sealed class VulkanResourceFactory : ResourceFactory
{
    private readonly VulkanGraphicsDevice _gd;

    public VulkanResourceFactory(VulkanGraphicsDevice gd) : base(gd.Features)
    {
        _gd = gd;
    }

    public override GraphicsBackend BackendType => GraphicsBackend.Vulkan;

    public unsafe override VulkanFence CreateFence(bool signaled)
    {
        VkFence fence = default;
        try
        {
            var createInfo = new VkFenceCreateInfo()
            {
                flags = signaled ? VkFenceCreateFlagBits.FenceCreateSignaledBit : 0,
            };

            VulkanUtil.CheckResult(Vk.CreateFence(_gd.Device, &createInfo, null, &fence));

            var result = new VulkanFence(_gd, fence);
            fence = default; // transfer ownership
            return result;
        }
        finally
        {
            if (fence != VkFence.Zero)
            {
                DestroyFence(_gd.Device, fence, null);
            }
        }
    }

    public unsafe override VulkanCommandList CreateCommandList(in CommandListDescription description)
    {
        VkCommandPool pool = default;
        try
        {
            pool = _gd.CreateCommandPool(description.Transient);

            var result = new VulkanCommandList(_gd, pool, description);
            pool = default; // transfer ownership
            return result;
        }
        finally
        {
            if (pool != VkCommandPool.Zero)
            {
                DestroyCommandPool(_gd.Device, pool, null);
            }
        }
    }

    public unsafe override VulkanSampler CreateSampler(in SamplerDescription description)
    {
        ValidateSampler(description);

        VkFormats.GetFilterParams(description.Filter, out var minFilter, out var magFilter, out var mipmapMode);

        var createInfo = new VkSamplerCreateInfo()
        {
            addressModeU = VkFormats.VdToVkSamplerAddressMode(description.AddressModeU),
            addressModeV = VkFormats.VdToVkSamplerAddressMode(description.AddressModeV),
            addressModeW = VkFormats.VdToVkSamplerAddressMode(description.AddressModeW),
            minFilter = minFilter,
            magFilter = magFilter,
            mipmapMode = mipmapMode,
            compareEnable = (VkBool32)(description.ComparisonKind is not null),
            compareOp = description.ComparisonKind is { } compareKind
                ? VkFormats.VdToVkCompareOp(compareKind)
                : VkCompareOp.CompareOpNever,
            anisotropyEnable = (VkBool32)(description.Filter is SamplerFilter.Anisotropic),
            maxAnisotropy = description.MaximumAnisotropy,
            minLod = description.MinimumLod,
            maxLod = description.MaximumLod,
            mipLodBias = description.LodBias,
            borderColor = VkFormats.VdToVkSamplerBorderColor(description.BorderColor),
        };

        VkSampler sampler;
        VulkanUtil.CheckResult(Vk.CreateSampler(_gd.Device, &createInfo, null, &sampler));

        return new VulkanSampler(_gd, sampler);
    }

    private unsafe VkMemoryRequirements GetBufferMemoryRequirements(VkBuffer buffer, out VkBool32 useDedicatedAllocation)
    {
        VkMemoryRequirements memoryRequirements;
        if (_gd.GetBufferMemoryRequirements2 is not null)
        {
            var memReqInfo2 = new VkBufferMemoryRequirementsInfo2()
            {
                buffer = buffer,
            };
            var dediReqs = new VkMemoryDedicatedRequirements()
            {
            };
            var memReqs2 = new VkMemoryRequirements2()
            {
                pNext = &dediReqs,
            };
            _gd.GetBufferMemoryRequirements2(_gd.Device, &memReqInfo2, &memReqs2);
            memoryRequirements = memReqs2.memoryRequirements;
            useDedicatedAllocation = dediReqs.prefersDedicatedAllocation | dediReqs.requiresDedicatedAllocation;
        }
        else
        {
            Vk.GetBufferMemoryRequirements(_gd.Device, buffer, &memoryRequirements);
            useDedicatedAllocation = false;
        }

        return memoryRequirements;
    }

    private unsafe VkMemoryRequirements GetImageMemoryRequirements(VkImage image, out VkBool32 useDedicatedAllocation)
    {
        VkMemoryRequirements memoryRequirements;
        if (_gd.GetImageMemoryRequirements2 is not null)
        {
            var memReqInfo2 = new VkImageMemoryRequirementsInfo2()
            {
                image = image,
            };
            var dediReqs = new VkMemoryDedicatedRequirements()
            {
            };
            var memReqs2 = new VkMemoryRequirements2()
            {
                pNext = &dediReqs,
            };
            _gd.GetImageMemoryRequirements2(_gd.Device, &memReqInfo2, &memReqs2);
            memoryRequirements = memReqs2.memoryRequirements;
            useDedicatedAllocation = dediReqs.prefersDedicatedAllocation | dediReqs.requiresDedicatedAllocation;
        }
        else
        {
            Vk.GetImageMemoryRequirements(_gd.Device, image, &memoryRequirements);
            useDedicatedAllocation = false;
        }

        return memoryRequirements;
    }

    public unsafe override VulkanBuffer CreateBuffer(in BufferDescription description)
    {
        ValidateBuffer(description);

        var Usage = VkBufferUsageFlagBits.BufferUsageTransferSrcBit | VkBufferUsageFlagBits.BufferUsageTransferDstBit;
        if ((description.Usage & BufferUsage.VertexBuffer) != 0)
        {
            Usage |= VkBufferUsageFlagBits.BufferUsageVertexBufferBit;
        }
        if ((description.Usage & BufferUsage.IndexBuffer) != 0)
        {
            Usage |= VkBufferUsageFlagBits.BufferUsageIndexBufferBit;
        }
        if ((description.Usage & BufferUsage.UniformBuffer) != 0)
        {
            Usage |= VkBufferUsageFlagBits.BufferUsageUniformBufferBit;
        }
        if ((description.Usage & (BufferUsage.StructuredBufferReadOnly | BufferUsage.StructuredBufferReadWrite)) != 0)
        {
            Usage |= VkBufferUsageFlagBits.BufferUsageStorageBufferBit;
        }
        if ((description.Usage & BufferUsage.IndirectBuffer) != 0)
        {
            Usage |= VkBufferUsageFlagBits.BufferUsageIndirectBufferBit;
        }

        VkBuffer buffer = default;
        VkMemoryBlock memoryBlock = default;
        VulkanBuffer result;

        try
        {
            var bufferCreateInfo = new VkBufferCreateInfo()
            {
                size = description.SizeInBytes,
                usage = Usage,
            };
            VulkanUtil.CheckResult(Vk.CreateBuffer(_gd.Device, &bufferCreateInfo, null, &buffer));

            var memoryRequirements = GetBufferMemoryRequirements(buffer, out var useDedicatedAllocation);

            var isStaging = (description.Usage & BufferUsage.StagingReadWrite) != 0;
            var isDynamic = (description.Usage & BufferUsage.DynamicReadWrite) != 0;
            var hostVisible = isStaging || isDynamic;

            var memPropFlags = hostVisible
                ? VkMemoryPropertyFlagBits.MemoryPropertyHostVisibleBit
                : VkMemoryPropertyFlagBits.MemoryPropertyDeviceLocalBit;

            if ((description.Usage & BufferUsage.StagingRead) != 0)
            {
                // Use "host cached" memory for staging when available, for better performance of GPU -> CPU transfers
                var hostCachedAvailable = VulkanUtil.TryFindMemoryType(
                    _gd._deviceCreateState.PhysicalDeviceMemoryProperties,
                    memoryRequirements.memoryTypeBits,
                    memPropFlags | VkMemoryPropertyFlagBits.MemoryPropertyHostCachedBit,
                    out _);

                if (hostCachedAvailable)
                {
                    memPropFlags |= VkMemoryPropertyFlagBits.MemoryPropertyHostCachedBit;
                }
            }

            if (isStaging)
            {
                if (VulkanUtil.TryFindMemoryType(
                    _gd._deviceCreateState.PhysicalDeviceMemoryProperties,
                    memoryRequirements.memoryTypeBits,
                    memPropFlags | VkMemoryPropertyFlagBits.MemoryPropertyHostCoherentBit,
                    out _))
                {
                    // if a host-coherent variation is available and we're allocating a staging RW buffer, use it
                    memPropFlags |= VkMemoryPropertyFlagBits.MemoryPropertyHostCoherentBit;
                }
            }

            memoryBlock = _gd.MemoryManager.Allocate(
                _gd._deviceCreateState.PhysicalDeviceMemoryProperties,
                memoryRequirements.memoryTypeBits,
                memPropFlags,
                hostVisible,
                memoryRequirements.size,
                memoryRequirements.alignment,
                useDedicatedAllocation,
                dedicatedImage: default,
                buffer);

            VulkanUtil.CheckResult(BindBufferMemory(_gd.Device, buffer, memoryBlock.DeviceMemory, memoryBlock.Offset));

            result = new VulkanBuffer(_gd, description, buffer, memoryBlock);
            buffer = default; // ownership is transferred
            memoryBlock = default;
        }
        finally
        {
            if (buffer != VkBuffer.Zero)
            {
                DestroyBuffer(_gd.Device, buffer, null);
            }

            if (memoryBlock.DeviceMemory != VkDeviceMemory.Zero)
            {
                _gd.MemoryManager.Free(memoryBlock);
            }
        }

        // once we've created the buffer, populate it if initial data was specified
        if (description.InitialData != 0)
        {
            _gd.UpdateBuffer(result, 0, description.InitialData, description.SizeInBytes);
        }

        return result;
    }

    public unsafe override VulkanTexture CreateTexture(in TextureDescription description)
    {
        var isStaging = (description.Usage & TextureUsage.Staging) != 0;

        VkBuffer buffer = default;
        VkImage image = default;
        VkMemoryBlock memoryBlock = default;

        try
        {
            if (!isStaging)
            {
                // regular texture, using an actual VkImage
                var imageCreateInfo = new VkImageCreateInfo()
                {
                    mipLevels = description.MipLevels,
                    imageType = VkFormats.VdToVkTextureType(description.Type),
                    extent = new()
                    {
                        width = description.Width,
                        height = description.Height,
                        depth = description.Depth,
                    },
                    initialLayout = VkImageLayout.ImageLayoutPreinitialized,
                    usage = VkFormats.VdToVkTextureUsage(description.Usage),
                    tiling = VkImageTiling.ImageTilingOptimal,
                    format = VkFormats.VdToVkPixelFormat(description.Format, description.Usage),
                    flags = VkImageCreateFlagBits.ImageCreateMutableFormatBit,
                    samples = VkFormats.VdToVkSampleCount(description.SampleCount),
                };

                var actualArrayLayers = description.ArrayLayers;
                if ((description.Usage & TextureUsage.Cubemap) != 0)
                {
                    imageCreateInfo.flags |= VkImageCreateFlagBits.ImageCreateCubeCompatibleBit;
                    actualArrayLayers = 6 * description.ArrayLayers;
                }
                imageCreateInfo.arrayLayers = actualArrayLayers;

                var subresourceCount = description.MipLevels * description.Depth * actualArrayLayers;

                VulkanUtil.CheckResult(CreateImage(_gd.Device, &imageCreateInfo, null, &image));

                var memoryRequirements = GetImageMemoryRequirements(image, out var useDedicatedAllocation);

                memoryBlock = _gd.MemoryManager.Allocate(
                    _gd._deviceCreateState.PhysicalDeviceMemoryProperties,
                    memoryRequirements.memoryTypeBits,
                    VkMemoryPropertyFlagBits.MemoryPropertyDeviceLocalBit,
                    persistentMapped: false,
                    memoryRequirements.size,
                    memoryRequirements.alignment,
                    useDedicatedAllocation,
                    image,
                    dedicatedBuffer: default);

                VulkanUtil.CheckResult(BindImageMemory(_gd.Device, image, memoryBlock.DeviceMemory, memoryBlock.Offset));
            }
            else
            {
                // staging buffer, isn't backed by an actual VkImage
                var depthPitch = FormatHelpers.GetDepthPitch(
                    FormatHelpers.GetRowPitch(description.Width, description.Format),
                    description.Height,
                    description.Format);
                var stagingSize = depthPitch * description.Depth;

                for (var level = 1u; level < description.MipLevels; level++)
                {
                    var mipWidth = Util.GetDimension(description.Width, level);
                    var mipHeight = Util.GetDimension(description.Height, level);
                    var mipDepth = Util.GetDimension(description.Depth, level);

                    depthPitch = FormatHelpers.GetDepthPitch(
                        FormatHelpers.GetRowPitch(mipWidth, description.Format),
                        mipHeight,
                        description.Format);

                    stagingSize += depthPitch * mipDepth;
                }
                stagingSize *= description.ArrayLayers;

                var bufferCreateInfo = new VkBufferCreateInfo()
                {
                    usage = VkBufferUsageFlagBits.BufferUsageTransferSrcBit | VkBufferUsageFlagBits.BufferUsageTransferDstBit,
                    size = stagingSize,
                };
                VulkanUtil.CheckResult(Vk.CreateBuffer(_gd.Device, &bufferCreateInfo, null, &buffer));

                var memoryRequirements = GetBufferMemoryRequirements(buffer, out var useDedicatedAllocation);

                // Use "host cached" memory when available, for better performance of GPU -> CPU transfers
                var propertyFlags =
                    VkMemoryPropertyFlagBits.MemoryPropertyHostVisibleBit |
                    VkMemoryPropertyFlagBits.MemoryPropertyHostCoherentBit |
                    VkMemoryPropertyFlagBits.MemoryPropertyHostCachedBit;

                if (!VulkanUtil.TryFindMemoryType(
                    _gd._deviceCreateState.PhysicalDeviceMemoryProperties,
                    memoryRequirements.memoryTypeBits, propertyFlags, out _))
                {
                    propertyFlags ^= VkMemoryPropertyFlagBits.MemoryPropertyHostCachedBit;
                }

                memoryBlock = _gd.MemoryManager.Allocate(
                    _gd._deviceCreateState.PhysicalDeviceMemoryProperties,
                    memoryRequirements.memoryTypeBits,
                    propertyFlags,
                    persistentMapped: true,
                    memoryRequirements.size,
                    memoryRequirements.alignment,
                    useDedicatedAllocation,
                    dedicatedImage: default,
                    buffer);

                VulkanUtil.CheckResult(BindBufferMemory(_gd.Device, buffer, memoryBlock.DeviceMemory, memoryBlock.Offset));
            }

            var result = new VulkanTexture(
                _gd, in description,
                image, memoryBlock, buffer,
                parentFramebuffer: null,
                leaveOpen: false);
            image = default; // ownership transfer into the new object
            buffer = default;
            memoryBlock = default;

            // now make sure the current image layout is set
            // the texture is either:
            // a) a buffer, so the image layout doesn't matter, or
            // b) an image, created with an initial layout of PREINITIALIZED
            if (!isStaging)
            {
                // while it is not necessary for correctness, to avoid emitting a spurious buffer barrier,
                // we don't want to track image layout for staging images
                result.AllSyncStates.Fill(new() { CurrentImageLayout = VkImageLayout.ImageLayoutPreinitialized });
            }

            return result;
        }
        finally
        {
            if (buffer != VkBuffer.Zero)
            {
                DestroyBuffer(_gd.Device, buffer, null);
            }

            if (image != VkImage.Zero)
            {
                DestroyImage(_gd.Device, image, null);
            }

            if (memoryBlock.DeviceMemory != VkDeviceMemory.Zero)
            {
                _gd.MemoryManager.Free(memoryBlock);
            }
        }
    }

    public override VulkanTexture CreateTexture(ulong nativeTexture, in TextureDescription description)
    {
        var image = new VkImage(nativeTexture);

        var result = new VulkanTexture(_gd, description,
            image, default, default,
            parentFramebuffer: null,
            leaveOpen: true);

        // we don't know what the initial layout is, so we assume UNDEFINED
        result.AllSyncStates.Fill(default);

        return result;
    }

    public unsafe override VulkanTextureView CreateTextureView(in TextureViewDescription description)
    {
        var tex = Util.AssertSubtype<Texture, VulkanTexture>(description.Target);

        VkImageView imageView = default;

        try
        {
            var aspectFlags =
                (description.Target.Usage & TextureUsage.DepthStencil) != 0
                ? VkImageAspectFlagBits.ImageAspectDepthBit
                : VkImageAspectFlagBits.ImageAspectColorBit;

            var imageViewCreateInfo = new VkImageViewCreateInfo()
            {
                image = tex.DeviceImage,
                format = VkFormats.VdToVkPixelFormat(description.Format ?? tex.Format, tex.Usage),
                subresourceRange = new()
                {
                    aspectMask = aspectFlags,
                    baseMipLevel = description.BaseMipLevel,
                    levelCount = description.MipLevels,
                    baseArrayLayer = description.BaseArrayLayer,
                    layerCount = description.ArrayLayers,
                }
            };

            if ((tex.Usage & TextureUsage.Cubemap) != 0)
            {
                imageViewCreateInfo.viewType = description.ArrayLayers == 1
                    ? VkImageViewType.ImageViewTypeCube
                    : VkImageViewType.ImageViewTypeCubeArray;
                imageViewCreateInfo.subresourceRange.layerCount *= 6;
            }
            else
            {
                switch (tex.Type)
                {
                    case TextureType.Texture1D:
                        imageViewCreateInfo.viewType = description.ArrayLayers == 1
                            ? VkImageViewType.ImageViewType1d
                            : VkImageViewType.ImageViewType1dArray;
                        break;
                    case TextureType.Texture2D:
                        imageViewCreateInfo.viewType = description.ArrayLayers == 1
                            ? VkImageViewType.ImageViewType2d
                            : VkImageViewType.ImageViewType2dArray;
                        break;
                    case TextureType.Texture3D:
                        imageViewCreateInfo.viewType = VkImageViewType.ImageViewType3d;
                        break;
                }
            }

            VulkanUtil.CheckResult(CreateImageView(_gd.Device, &imageViewCreateInfo, null, &imageView));

            var result = new VulkanTextureView(_gd, description, imageView);
            imageView = default; // ownership transfer
            return result;
        }
        finally
        {
            if (imageView != VkImageView.Zero)
            {
                DestroyImageView(_gd.Device, imageView, null);
            }
        }
    }

    public override VulkanFramebuffer CreateFramebuffer(in FramebufferDescription description)
    {
        if (_gd._deviceCreateState.HasDynamicRendering)
        {
            return CreateDynamicFramebuffer(description);
        }
        else
        {
            return CreateRenderPassFramebuffer(description);
        }
    }

    private unsafe VulkanDynamicFramebuffer CreateDynamicFramebuffer(in FramebufferDescription description)
    {
        VulkanTextureView? depthTarget = null;
        VulkanTextureView[]? colorTargets = null;

        try
        {
            CreateFramebufferTextureViews(description, ref depthTarget, ref colorTargets);

            var result = new VulkanDynamicFramebuffer(_gd, description, depthTarget, colorTargets ?? []);
            depthTarget = null; // ownership transfer
            colorTargets = null;

            return result;
        }
        finally
        {
            if (depthTarget is not null)
            {
                depthTarget.Dispose();
            }

            if (colorTargets is not null)
            {
                foreach (var target in colorTargets)
                {
                    if (target is not null)
                    {
                        target.Dispose();
                    }
                }
            }
        }
    }

    private unsafe VulkanRenderPassFramebuffer CreateRenderPassFramebuffer(in FramebufferDescription description)
    {
        VkFramebuffer framebuffer = default;
        VulkanRenderPassHolder? rpHolder = null;
        VulkanTextureView? depthTarget = null;
        VulkanTextureView[]? colorTargets = null;

        try
        {
            // first, we want to create the render passes based on the textures
            var colorTargetDescs = description.ColorTargets.AsSpan();

            CreateFramebufferTextureViews(description, ref depthTarget, ref colorTargets);

            var totalAttCount = colorTargetDescs.Length;
            var rpAttInfo = ArrayPool<RenderPassAttachmentInfo>.Shared.Rent(colorTargetDescs.Length + 1); // + 1 for depth
            var fbTexViews = ArrayPool<VkImageView>.Shared.Rent(colorTargetDescs.Length + 1);

            for (var i = 0; i < colorTargetDescs.Length; i++)
            {
                var targetInfo = colorTargetDescs[i];
                var targetTex = Util.AssertSubtype<Texture, VulkanTexture>(targetInfo.Target);

                rpAttInfo[i] = new(targetTex.VkFormat, targetTex.VkSampleCount,
                    (targetTex.Usage & TextureUsage.Sampled) != 0, FormatHelpers.IsStencilFormat(targetTex.Format));
                fbTexViews[i] = colorTargets![i].ImageView;
            }

            if (description.DepthTarget is { } depthTargetDesc)
            {
                var targetTex = Util.AssertSubtype<Texture, VulkanTexture>(depthTargetDesc.Target);

                rpAttInfo[colorTargetDescs.Length] = new(targetTex.VkFormat, targetTex.VkSampleCount,
                    (targetTex.Usage & TextureUsage.Sampled) != 0, FormatHelpers.IsStencilFormat(targetTex.Format));
                fbTexViews[colorTargetDescs.Length] = depthTarget!.ImageView;
                totalAttCount++;
            }

            // get the render passes for this framebuffer
            rpHolder = VulkanRenderPassHolder.GetRenderPassHolder(_gd, new(rpAttInfo.AsMemory(0, totalAttCount), description.DepthTarget.HasValue));

            fixed (VkImageView* pTexViews = fbTexViews)
            {
                Texture dimTex;
                uint mipLevel;
                if (colorTargetDescs.Length > 0)
                {
                    dimTex = colorTargetDescs[0].Target;
                    mipLevel = colorTargetDescs[0].MipLevel;
                }
                else
                {
                    Debug.Assert(description.DepthTarget != null);
                    dimTex = description.DepthTarget.Value.Target;
                    mipLevel = description.DepthTarget.Value.MipLevel;
                }

                Util.GetMipDimensions(
                    dimTex,
                    mipLevel,
                    out uint mipWidth,
                    out uint mipHeight);

                // create the actual FBO
                var fbCreateInfo = new VkFramebufferCreateInfo()
                {
                    width = mipWidth,
                    height = mipHeight,
                    attachmentCount = (uint)totalAttCount,
                    pAttachments = pTexViews,
                    layers = 1,
                    renderPass = rpHolder.LoadOpDontCare,
                };
                VulkanUtil.CheckResult(Vk.CreateFramebuffer(_gd.Device, &fbCreateInfo, null, &framebuffer));
            }

            // create result object
            var result = new VulkanRenderPassFramebuffer(_gd, description, framebuffer, rpHolder, colorTargets, depthTarget);
            depthTarget = null; // ownership transfer
            colorTargets = null;
            rpHolder = null;
            framebuffer = default;

            ArrayPool<RenderPassAttachmentInfo>.Shared.Return(rpAttInfo);
            ArrayPool<VkImageView>.Shared.Return(fbTexViews);

            return result;
        }
        finally
        {
            if (framebuffer != VkFramebuffer.Zero)
            {
                DestroyFramebuffer(_gd.Device, framebuffer, null);
            }

            rpHolder?.DecRef();

            if (depthTarget is not null)
            {
                depthTarget.Dispose();
            }

            if (colorTargets is not null)
            {
                foreach (var target in colorTargets)
                {
                    if (target is not null)
                    {
                        target.Dispose();
                    }
                }
            }
        }
    }

    private unsafe void CreateFramebufferTextureViews(in FramebufferDescription description, ref VulkanTextureView? depthTarget, ref VulkanTextureView[]? colorTargets)
    {
        VkImageView imageView = default;
        try
        {
            if (description.ColorTargets is not null)
            {
                colorTargets = new VulkanTextureView[description.ColorTargets.Length];
                for (var i = 0; i < description.ColorTargets.Length; i++)
                {
                    var targetDesc = description.ColorTargets[i];
                    var target = Util.AssertSubtype<Texture, VulkanTexture>(targetDesc.Target);

                    var imageViewCreateInfo = new VkImageViewCreateInfo()
                    {
                        image = target.DeviceImage,
                        format = target.VkFormat,
                        viewType = VkImageViewType.ImageViewType2d,
                        subresourceRange = new()
                        {
                            aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                            baseMipLevel = targetDesc.MipLevel,
                            levelCount = 1,
                            baseArrayLayer = targetDesc.ArrayLayer,
                            layerCount = 1,
                        }
                    };

                    imageView = default;
                    VulkanUtil.CheckResult(CreateImageView(_gd.Device, &imageViewCreateInfo, null, &imageView));

                    colorTargets[i] = new VulkanTextureView(_gd,
                        new(target, targetDesc.MipLevel, 1, targetDesc.ArrayLayer, 1),
                        imageView);
                    imageView = default;
                }
            }

            if (description.DepthTarget is { } depthTargetDesc)
            {
                var target = Util.AssertSubtype<Texture, VulkanTexture>(depthTargetDesc.Target);

                var imageViewCreateInfo = new VkImageViewCreateInfo()
                {
                    image = target.DeviceImage,
                    format = target.VkFormat,
                    viewType = VkImageViewType.ImageViewType2d,
                    subresourceRange = new()
                    {
                        aspectMask = VkImageAspectFlagBits.ImageAspectDepthBit,
                        baseMipLevel = depthTargetDesc.MipLevel,
                        levelCount = 1,
                        baseArrayLayer = depthTargetDesc.ArrayLayer,
                        layerCount = 1,
                    }
                };

                imageView = default;
                VulkanUtil.CheckResult(CreateImageView(_gd.Device, &imageViewCreateInfo, null, &imageView));

                depthTarget = new VulkanTextureView(_gd,
                    new(target, depthTargetDesc.MipLevel, 1, depthTargetDesc.ArrayLayer, 1),
                    imageView);
                imageView = default;
            }
        }
        finally
        {
            if (imageView != VkImageView.Zero)
            {
                DestroyImageView(_gd.Device, imageView, null);
            }
        }
    }

    public unsafe override VulkanSwapchain CreateSwapchain(in SwapchainDescription description)
    {
        VkSurfaceKHR surface = default;

        try
        {
            VulkanUtil.CheckResult(Toolkit.Vulkan.CreateWindowSurface(_gd._deviceCreateState.Instance, description.Source, null, out surface));

            var presentQueueIndex = _gd._deviceCreateState.QueueFamilyInfo.PresentFamilyIdx;
            if (presentQueueIndex == -1)
            {
                // no present family was identified during startup, since we only support one queue it should just be the main graphics family
                presentQueueIndex = _gd._deviceCreateState.QueueFamilyInfo.MainGraphicsFamilyIdx;
            }

            // we need to make sure that the queue that we've selected is capable of presenting to the created surface
            int supported;
            VulkanUtil.CheckResult(GetPhysicalDeviceSurfaceSupportKHR(
                _gd._deviceCreateState.PhysicalDevice, (uint)presentQueueIndex, surface, &supported));
            if (!(VkBool32)supported)
            {
                // we can't present to the queue, unable to create swapchain
                throw new VeldridException("Cannot create swapchain (selected VkQueue cannot present to target surface)");
            }

            return new VulkanSwapchain(_gd, in description, ref surface, presentQueueIndex);
        }
        finally
        {
            if (surface != VkSurfaceKHR.Zero)
            {
                DestroySurfaceKHR(_gd._deviceCreateState.Instance, surface, null);
            }
        }
    }

    public unsafe override VulkanShader CreateShader(in ShaderDescription description)
    {
        ValidateShader(description);

        VkShaderModule shader = default;
        try
        {
            fixed (byte* codePtr = description.ShaderBytes)
            {
                var createInfo = new VkShaderModuleCreateInfo()
                {
                    codeSize = (nuint)description.ShaderBytes.Length,
                    pCode = (uint*)codePtr,
                };

                VulkanUtil.CheckResult(CreateShaderModule(_gd.Device, &createInfo, null, &shader));
            }

            var result = new VulkanShader(_gd, description, shader);
            shader = default;
            return result;
        }
        finally
        {
            if (shader != VkShaderModule.Zero)
            {
                DestroyShaderModule(_gd.Device, shader, null);
            }
        }
    }

    public unsafe override VulkanResourceLayout CreateResourceLayout(in ResourceLayoutDescription description)
    {
        VkDescriptorSetLayout dsl = default;
        try
        {
            var elements = description.Elements;
            var types = new VkDescriptorType[elements.Length];
            var stages = new VkShaderStageFlagBits[elements.Length];
            var access = new VkAccessFlagBits[elements.Length];
            var bindings = ArrayPool<VkDescriptorSetLayoutBinding>.Shared.Rent(elements.Length);

            var dynamicBufferCount = 0;
            var uniformBufferCount = 0u;
            var uniformBufferDynamicCount = 0u;
            var sampledImageCount = 0u;
            var samplerCount = 0u;
            var storageBufferCount = 0u;
            var storageBufferDynamicCount = 0u;
            var storageImageCount = 0u;

            for (var i = 0u; i < elements.Length; i++)
            {
                ref var element = ref elements[i];
                ref var binding = ref bindings[i];

                binding.binding = i;
                binding.descriptorCount = 1;
                var descriptorType = VkFormats.VdToVkDescriptorType(element.Kind, element.Options);
                binding.descriptorType = descriptorType;
                var shaderStages = VkFormats.VdToVkShaderStages(element.Stages);
                binding.stageFlags = shaderStages;

                types[i] = descriptorType;
                stages[i] = shaderStages;
                access[i] = VkFormats.VdToVkAccess(element.Kind);

                if ((element.Options & ResourceLayoutElementOptions.DynamicBinding) != 0)
                {
                    dynamicBufferCount++;
                }

                switch (descriptorType)
                {
                    case VkDescriptorType.DescriptorTypeSampler:
                        samplerCount += 1;
                        break;
                    case VkDescriptorType.DescriptorTypeSampledImage:
                        sampledImageCount += 1;
                        break;
                    case VkDescriptorType.DescriptorTypeStorageImage:
                        storageImageCount += 1;
                        break;
                    case VkDescriptorType.DescriptorTypeUniformBuffer:
                        uniformBufferCount += 1;
                        break;
                    case VkDescriptorType.DescriptorTypeUniformBufferDynamic:
                        uniformBufferDynamicCount += 1;
                        break;
                    case VkDescriptorType.DescriptorTypeStorageBuffer:
                        storageBufferCount += 1;
                        break;
                    case VkDescriptorType.DescriptorTypeStorageBufferDynamic:
                        storageBufferDynamicCount += 1;
                        break;
                }
            }

            fixed (VkDescriptorSetLayoutBinding* pBindings = bindings)
            {
                var createInfo = new VkDescriptorSetLayoutCreateInfo()
                {
                    bindingCount = (uint)elements.Length,
                    pBindings = pBindings
                };

                VulkanUtil.CheckResult(CreateDescriptorSetLayout(_gd.Device, &createInfo, null, &dsl));
            }

            ArrayPool<VkDescriptorSetLayoutBinding>.Shared.Return(bindings);

            var resourceCounts = new DescriptorResourceCounts(
                uniformBufferCount, uniformBufferDynamicCount,
                sampledImageCount, samplerCount,
                storageBufferCount, storageBufferDynamicCount,
                storageImageCount);

            var result = new VulkanResourceLayout(_gd, description, dsl, types, stages, access, resourceCounts, dynamicBufferCount);
            dsl = default;
            return result;
        }
        finally
        {
            if (dsl != VkDescriptorSetLayout.Zero)
            {
                DestroyDescriptorSetLayout(_gd.Device, dsl, null);
            }
        }
    }

    public unsafe override VulkanResourceSet CreateResourceSet(in ResourceSetDescription description)
    {
        var layout = Util.AssertSubtype<ResourceLayout, VulkanResourceLayout>(description.Layout);
        var resourceCounts = layout.ResourceCounts;

        DescriptorAllocationToken allocToken = default;
        VulkanResourceSet? result = null;
        try
        {
            allocToken = _gd.DescriptorPoolManager.Allocate(resourceCounts, layout.DescriptorSetLayout);
            result = new(_gd, description, allocToken);
            allocToken = default;

            var boundResources = description.BoundResources;

            var descriptorWrites = ArrayPool<VkWriteDescriptorSet>.Shared.Rent(boundResources.Length);
            var bufferInfos = ArrayPool<VkDescriptorBufferInfo>.Shared.Rent(boundResources.Length);
            var imageInfos = ArrayPool<VkDescriptorImageInfo>.Shared.Rent(boundResources.Length);

            fixed (VkWriteDescriptorSet* pDescriptorWrites = descriptorWrites)
            fixed (VkDescriptorBufferInfo* pBufferInfos = bufferInfos)
            fixed (VkDescriptorImageInfo* pImageInfos = imageInfos)
            {
                for (var i = 0; i < boundResources.Length; i++)
                {
                    var type = layout.DescriptorTypes[i];
                    ref var descWrite = ref descriptorWrites[i];
                    descWrite = new()
                    {
                        dstSet = result.DescriptorSet,
                        dstBinding = (uint)i,
                        descriptorType = type,
                        descriptorCount = 1,
                    };

                    var access = layout.AccessFlags[i];
                    var pipelineStage = VkFormats.ShaderStagesToPipelineStages(layout.ShaderStages[i]);

                    switch (type)
                    {
                        case VkDescriptorType.DescriptorTypeUniformBuffer:
                        case VkDescriptorType.DescriptorTypeUniformBufferDynamic:
                        case VkDescriptorType.DescriptorTypeStorageBuffer:
                        case VkDescriptorType.DescriptorTypeStorageBufferDynamic:
                            {
                                var range = Util.GetBufferRange(boundResources[i], 0);
                                var buffer = Util.AssertSubtype<DeviceBuffer, VulkanBuffer>(range.Buffer);
                                bufferInfos[i] = new()
                                {
                                    buffer = buffer.DeviceBuffer,
                                    offset = range.Offset,
                                    range = range.SizeInBytes,
                                };
                                descWrite.pBufferInfo = pBufferInfos + i;
                                result.RefCounts.Add(buffer.RefCount);
                                result.Buffers.Add(new(buffer, new()
                                {
                                    BarrierMasks =
                                {
                                    StageMask = pipelineStage,
                                    AccessMask = access,
                                }
                                }));
                            }
                            break;

                        case VkDescriptorType.DescriptorTypeSampledImage:
                            {
                                var vdTexView = Util.GetTextureView(_gd, boundResources[i]);
                                var texView = Util.AssertSubtype<TextureView, VulkanTextureView>(vdTexView);
                                imageInfos[i] = new()
                                {
                                    imageView = texView.ImageView,
                                    imageLayout = VkImageLayout.ImageLayoutShaderReadOnlyOptimal,
                                };
                                descWrite.pImageInfo = pImageInfos + i;
                                result.RefCounts.Add(texView.RefCount);
                                result.Textures.Add(new(texView, new()
                                {
                                    Layout = VkImageLayout.ImageLayoutShaderReadOnlyOptimal,
                                    BarrierMasks =
                                {
                                    StageMask = pipelineStage,
                                    AccessMask = access,
                                }
                                }));
                            }
                            break;

                        case VkDescriptorType.DescriptorTypeStorageImage:
                            {
                                var vdTexView = Util.GetTextureView(_gd, boundResources[i]);
                                var texView = Util.AssertSubtype<TextureView, VulkanTextureView>(vdTexView);
                                imageInfos[i] = new()
                                {
                                    imageView = texView.ImageView,
                                    imageLayout = VkImageLayout.ImageLayoutGeneral,
                                };
                                descWrite.pImageInfo = pImageInfos + i;
                                result.RefCounts.Add(texView.RefCount);
                                result.Textures.Add(new(texView, new()
                                {
                                    Layout = VkImageLayout.ImageLayoutGeneral,
                                    BarrierMasks =
                                {
                                    StageMask = pipelineStage,
                                    AccessMask = access,
                                }
                                }));
                            }
                            break;

                        case VkDescriptorType.DescriptorTypeSampler:
                            {
                                var sampler = Util.AssertSubtype<Sampler, VulkanSampler>(boundResources[i].GetSampler());
                                imageInfos[i] = new()
                                {
                                    sampler = sampler.DeviceSampler,
                                };
                                descWrite.pImageInfo = pImageInfos + i;
                                result.RefCounts.Add(sampler.RefCount);
                            }
                            break;
                    }
                }

                UpdateDescriptorSets(_gd.Device, (uint)boundResources.Length, pDescriptorWrites, 0, null);
            }

            ArrayPool<VkWriteDescriptorSet>.Shared.Return(descriptorWrites);
            ArrayPool<VkDescriptorBufferInfo>.Shared.Return(bufferInfos);
            ArrayPool<VkDescriptorImageInfo>.Shared.Return(imageInfos);

            var realResult = result;
            result = null;
            return realResult;
        }
        finally
        {
            if (allocToken.Set != VkDescriptorSet.Zero)
            {
                _gd.DescriptorPoolManager.Free(allocToken, resourceCounts);
            }

            result?.Dispose();
        }
    }

    private unsafe VkPipelineLayout CreatePipelineLayout(ResourceLayout[] resourceLayouts)
    {
        var dsls = ArrayPool<VkDescriptorSetLayout>.Shared.Rent(resourceLayouts.Length);
        for (var i = 0; i < resourceLayouts.Length; i++)
        {
            dsls[i] = Util.AssertSubtype<ResourceLayout, VulkanResourceLayout>(resourceLayouts[i]).DescriptorSetLayout;
        }

        VkPipelineLayout result;

        fixed (VkDescriptorSetLayout* pDsls = dsls)
        {
            var createInfo = new VkPipelineLayoutCreateInfo()
            {
                setLayoutCount = (uint)resourceLayouts.Length,
                pSetLayouts = pDsls,
            };

            VulkanUtil.CheckResult(Vk.CreatePipelineLayout(_gd.Device, &createInfo, null, &result));
        }

        ArrayPool<VkDescriptorSetLayout>.Shared.Return(dsls);

        return result;
    }

    private static void SetupSpecializationData(SpecializationConstant[] specDescs,
        out uint specializationDataSize, out byte[] specializationData,
        out uint specializationMapCount, out VkSpecializationMapEntry[] specializationMapEntries)
    {
        specializationDataSize = 0;
        foreach (var spec in specDescs)
        {
            specializationDataSize += VkFormats.GetSpecializationConstantSize(spec.Type);
        }

        specializationData = ArrayPool<byte>.Shared.Rent((int)specializationDataSize);

        specializationMapCount = (uint)specDescs.Length;
        specializationMapEntries = ArrayPool<VkSpecializationMapEntry>.Shared.Rent(specDescs.Length);

        var offset = 0u;
        for (var i = 0; i < specDescs.Length; i++)
        {
            var data = specDescs[i].Data;
            var size = VkFormats.GetSpecializationConstantSize(specDescs[i].Type);
            MemoryMarshal.AsBytes(new Span<ulong>(ref data)).Slice(0, (int)size).CopyTo(specializationData.AsSpan().Slice((int)offset));
            specializationMapEntries[i] = new()
            {
                constantID = specDescs[i].ID,
                offset = offset,
                size = size,
            };
            offset += size;
        }
    }

    public unsafe override VulkanPipeline CreateGraphicsPipeline(in GraphicsPipelineDescription description)
    {
        ValidateGraphicsPipeline(description);

        VkPipeline pipeline = default;
        VkPipelineLayout pipelineLayout = default;
        VulkanRenderPassHolder? rpHolder = null;

        try
        {
            VkGraphicsPipelineCreateInfo pipelineCreateInfo = new()
            {
            };

            // used only for when we have DynamicRendering
            VkPipelineRenderingCreateInfo renderingCreateInfo = new()
            {
            };

            // Pipeline Layout
            pipelineLayout = CreatePipelineLayout(description.ResourceLayouts);
            pipelineCreateInfo.layout = pipelineLayout;

            // Multisample
            var SampleCount = VkFormats.VdToVkSampleCount(description.Outputs.SampleCount);
            var multisampleCI = new VkPipelineMultisampleStateCreateInfo()
            {
                rasterizationSamples = SampleCount,
                alphaToCoverageEnable = (VkBool32)description.BlendState.AlphaToCoverageEnabled
            };

            pipelineCreateInfo.pMultisampleState = &multisampleCI;

            // Input Assembly
            var inputAssemblyCI = new VkPipelineInputAssemblyStateCreateInfo()
            {
                topology = VkFormats.VdToVkPrimitiveTopology(description.PrimitiveTopology)
            };

            pipelineCreateInfo.pInputAssemblyState = &inputAssemblyCI;

            VkFormat[]? colorAttachmentFormats = null;

            if (_gd._deviceCreateState.HasDynamicRendering) // if dynamic rendering is available, we don't actually need a render pass
            {
                // we need to pass in a VkPipelineRenderingCreateInfo
                renderingCreateInfo.pNext = pipelineCreateInfo.pNext;
                pipelineCreateInfo.pNext = &renderingCreateInfo;

                var outputDesc = description.Outputs;
                if (outputDesc.DepthAttachment is { } depthAtt)
                {
                    renderingCreateInfo.depthAttachmentFormat = VkFormats.VdToVkPixelFormat(depthAtt.Format, TextureUsage.DepthStencil);
                    renderingCreateInfo.stencilAttachmentFormat = VkFormat.FormatUndefined;
                    if (FormatHelpers.IsStencilFormat(depthAtt.Format))
                    {
                        renderingCreateInfo.stencilAttachmentFormat = renderingCreateInfo.depthAttachmentFormat;
                    }
                }

                var colorAttDescs = outputDesc.ColorAttachments.AsSpan();
                colorAttachmentFormats = ArrayPool<VkFormat>.Shared.Rent(colorAttDescs.Length);
                for (var i = 0; i < colorAttDescs.Length; i++)
                {
                    colorAttachmentFormats[i] = VkFormats.VdToVkPixelFormat(colorAttDescs[i].Format, default);
                }

                renderingCreateInfo.colorAttachmentCount = (uint)colorAttDescs.Length;
            }
            else
            {
                // Fake Render Pass (for compat)
                var outputDesc = description.Outputs;
                var colorAttDescs = outputDesc.ColorAttachments.AsSpan();

                var totalAttCount = colorAttDescs.Length;
                var rpAttInfo = ArrayPool<RenderPassAttachmentInfo>.Shared.Rent(colorAttDescs.Length + 1); // + 1 for depth

                for (var i = 0; i < colorAttDescs.Length; i++)
                {
                    var targetInfo = colorAttDescs[i];

                    rpAttInfo[i] = new(
                        VkFormats.VdToVkPixelFormat(targetInfo.Format, TextureUsage.RenderTarget), VkSampleCountFlagBits.SampleCount1Bit,
                        false, FormatHelpers.IsStencilFormat(targetInfo.Format));
                }

                if (outputDesc.DepthAttachment is { } depthTargetDesc)
                {
                    rpAttInfo[colorAttDescs.Length] = new(
                        VkFormats.VdToVkPixelFormat(depthTargetDesc.Format, TextureUsage.DepthStencil), VkSampleCountFlagBits.SampleCount1Bit,
                        false, FormatHelpers.IsStencilFormat(depthTargetDesc.Format));
                    totalAttCount++;
                }

                // get the render passes for this framebuffer
                rpHolder = VulkanRenderPassHolder.GetRenderPassHolder(_gd, new(rpAttInfo.AsMemory(0, totalAttCount), outputDesc.DepthAttachment.HasValue));

                ArrayPool<RenderPassAttachmentInfo>.Shared.Return(rpAttInfo);

                pipelineCreateInfo.renderPass = rpHolder.LoadOpLoad;
            }

            // Rasterizer State
            var rasterDesc = description.RasterizerState;
            var rasterizerCreateInfo = new VkPipelineRasterizationStateCreateInfo()
            {
                cullMode = VkFormats.VdToVkCullMode(rasterDesc.CullMode),
                polygonMode = VkFormats.VdToVkPolygonMode(rasterDesc.FillMode),
                depthClampEnable = (VkBool32)!rasterDesc.DepthClipEnabled,
                frontFace = rasterDesc.FrontFace is FrontFace.Clockwise ? VkFrontFace.FrontFaceClockwise : VkFrontFace.FrontFaceCounterClockwise,
                lineWidth = 1f,
            };
            pipelineCreateInfo.pRasterizationState = &rasterizerCreateInfo;

            // Dynamic State
            var pDynamicStates = stackalloc VkDynamicState[2]
            {
                VkDynamicState.DynamicStateViewport,
                VkDynamicState.DynamicStateScissor,
            };
            var dynamicStateCreateInfo = new VkPipelineDynamicStateCreateInfo()
            {
                dynamicStateCount = 2,
                pDynamicStates = pDynamicStates,
            };

            pipelineCreateInfo.pDynamicState = &dynamicStateCreateInfo;

            // Depth Stencil State
            var dssDesc = description.DepthStencilState;
            var dssCreateInfo = new VkPipelineDepthStencilStateCreateInfo()
            {
                depthWriteEnable = (VkBool32)dssDesc.DepthWriteEnabled,
                depthTestEnable = (VkBool32)dssDesc.DepthTestEnabled,
                depthCompareOp = VkFormats.VdToVkCompareOp(dssDesc.DepthComparison),
                stencilTestEnable = (VkBool32)dssDesc.StencilTestEnabled,
                front = new VkStencilOpState()
                {
                    failOp = VkFormats.VdToVkStencilOp(dssDesc.StencilFront.Fail),
                    passOp = VkFormats.VdToVkStencilOp(dssDesc.StencilFront.Pass),
                    depthFailOp = VkFormats.VdToVkStencilOp(dssDesc.StencilFront.DepthFail),
                    compareOp = VkFormats.VdToVkCompareOp(dssDesc.StencilFront.Comparison),
                    compareMask = dssDesc.StencilReadMask,
                    writeMask = dssDesc.StencilWriteMask,
                    reference = dssDesc.StencilReference
                },
                back = new VkStencilOpState()
                {
                    failOp = VkFormats.VdToVkStencilOp(dssDesc.StencilBack.Fail),
                    passOp = VkFormats.VdToVkStencilOp(dssDesc.StencilBack.Pass),
                    depthFailOp = VkFormats.VdToVkStencilOp(dssDesc.StencilBack.DepthFail),
                    compareOp = VkFormats.VdToVkCompareOp(dssDesc.StencilBack.Comparison),
                    compareMask = dssDesc.StencilReadMask,
                    writeMask = dssDesc.StencilWriteMask,
                    reference = dssDesc.StencilReference
                }
            };

            pipelineCreateInfo.pDepthStencilState = &dssCreateInfo;

            // Viewport State
            var viewportStateCreateInfo = new VkPipelineViewportStateCreateInfo()
            {
                viewportCount = 1,
                scissorCount = 1,
            };

            pipelineCreateInfo.pViewportState = &viewportStateCreateInfo;

            // Blend State
            var blendAttachmentCount = description.BlendState.AttachmentStates.Length;
            var blendAttachments = ArrayPool<VkPipelineColorBlendAttachmentState>.Shared.Rent(blendAttachmentCount);
            for (var i = 0; i < blendAttachmentCount; i++)
            {
                var desc = description.BlendState.AttachmentStates[i];
                blendAttachments[i] = new()
                {
                    srcColorBlendFactor = VkFormats.VdToVkBlendFactor(desc.SourceColorFactor),
                    dstColorBlendFactor = VkFormats.VdToVkBlendFactor(desc.DestinationColorFactor),
                    colorBlendOp = VkFormats.VdToVkBlendOp(desc.ColorFunction),

                    srcAlphaBlendFactor = VkFormats.VdToVkBlendFactor(desc.SourceAlphaFactor),
                    dstAlphaBlendFactor = VkFormats.VdToVkBlendFactor(desc.DestinationAlphaFactor),
                    alphaBlendOp = VkFormats.VdToVkBlendOp(desc.AlphaFunction),

                    blendEnable = (VkBool32)desc.BlendEnabled,
                    colorWriteMask = VkFormats.VdToVkColorWriteMask(desc.ColorWriteMask.GetOrDefault()),
                };
            }

            // Vertex Input State
            var inputDescs = description.ShaderSet.VertexLayouts.AsSpan();
            var bindingCount = (uint)inputDescs.Length;
            var attribCount = 0u;
            for (var i = 0; i < inputDescs.Length; i++)
            {
                attribCount += (uint)inputDescs[i].Elements.Length;
            }

            var bindingDescs = ArrayPool<VkVertexInputBindingDescription>.Shared.Rent((int)bindingCount);
            var attribDescs = ArrayPool<VkVertexInputAttributeDescription>.Shared.Rent((int)attribCount);

            var targetIndex = 0;
            var targetLocation = 0;
            for (var binding = 0; binding < inputDescs.Length; binding++)
            {
                var inputDesc = inputDescs[binding];
                bindingDescs[binding] = new()
                {
                    binding = (uint)binding,
                    inputRate = inputDesc.InstanceStepRate != 0 ? VkVertexInputRate.VertexInputRateInstance : VkVertexInputRate.VertexInputRateVertex,
                    stride = inputDesc.Stride,
                };

                // TODO: if InstanceStepRate > 1, try to use VK_KHR/EXT_vertex_attribute_divisor

                var currentOffset = 0u;
                for (var location = 0; location < inputDesc.Elements.Length; location++)
                {
                    var element = inputDesc.Elements[location];

                    attribDescs[targetIndex] = new()
                    {
                        format = VkFormats.VdToVkVertexElementFormat(element.Format),
                        binding = (uint)binding,
                        location = (uint)(targetLocation + location),
                        offset = element.Offset != 0 ? element.Offset : currentOffset,
                    };

                    targetIndex++;
                    currentOffset += FormatSizeHelpers.GetSizeInBytes(element.Format);
                }

                targetLocation += inputDesc.Elements.Length;
            }

            // Set up shader specialization data
            var specializationDataSize = 0u;
            byte[]? specializationData = null;
            var specializationMapCount = 0u;
            VkSpecializationMapEntry[]? specializationMapEntries = null;
            if (description.ShaderSet.Specializations is { } specDescs)
            {
                SetupSpecializationData(specDescs, out specializationDataSize, out specializationData, out specializationMapCount, out specializationMapEntries);
            }

            // Allocate shader create info array
            var shaderStages = ArrayPool<VkPipelineShaderStageCreateInfo>.Shared.Rent(description.ShaderSet.Shaders.Length);

            var stringHolder = new List<FixedUtf8String>();

            fixed (VkFormat* pColorAttachmentFormats = colorAttachmentFormats)
            fixed (VkPipelineColorBlendAttachmentState* pBlendAttachments = blendAttachments)
            fixed (VkVertexInputBindingDescription* pBindingDescs = bindingDescs)
            fixed (VkVertexInputAttributeDescription* pAttribDescs = attribDescs)
            fixed (byte* pSpecializationData = specializationData)
            fixed (VkSpecializationMapEntry* pSpecializationMapEntries = specializationMapEntries)
            fixed (VkPipelineShaderStageCreateInfo* pShaderStages = shaderStages)
            {
                renderingCreateInfo.pColorAttachmentFormats = pColorAttachmentFormats;

                // actually initialize the blend state CI
                var blendStateCreateInfo = new VkPipelineColorBlendStateCreateInfo()
                {
                    attachmentCount = (uint)blendAttachmentCount,
                    pAttachments = pBlendAttachments,
                };

                var blendFactor = description.BlendState.BlendFactor;
                blendStateCreateInfo.blendConstants[0] = blendFactor.R;
                blendStateCreateInfo.blendConstants[1] = blendFactor.G;
                blendStateCreateInfo.blendConstants[2] = blendFactor.B;
                blendStateCreateInfo.blendConstants[3] = blendFactor.A;

                pipelineCreateInfo.pColorBlendState = &blendStateCreateInfo;

                // and do the same for the vertex input CI
                var vertexInputCreateInfo = new VkPipelineVertexInputStateCreateInfo()
                {
                    vertexBindingDescriptionCount = bindingCount,
                    pVertexBindingDescriptions = pBindingDescs,
                    vertexAttributeDescriptionCount = attribCount,
                    pVertexAttributeDescriptions = pAttribDescs,
                };

                pipelineCreateInfo.pVertexInputState = &vertexInputCreateInfo;

                // shader stages
                var specializationInfo = new VkSpecializationInfo()
                {
                    dataSize = specializationDataSize,
                    pData = pSpecializationData,
                    mapEntryCount = specializationMapCount,
                    pMapEntries = pSpecializationMapEntries,
                };
                for (int i = 0; i < description.ShaderSet.Shaders.Length; i++)
                {
                    var shader = description.ShaderSet.Shaders[i];
                    var Shader = Util.AssertSubtype<Shader, VulkanShader>(shader);

                    FixedUtf8String name;
                    if (Shader.EntryPoint is "main")
                    {
                        name = Vulkan.CommonStrings.main;
                    }
                    else
                    {
                        // this MUST be added to the list so that the GC cannot collect it out from under us
                        stringHolder.Add(name = new FixedUtf8String(Shader.EntryPoint));
                    }

                    pShaderStages[i] = new()
                    {
                        module = Shader.ShaderModule,
                        stage = VkFormats.VdToVkShaderStages(Shader.Stage),
                        pSpecializationInfo = &specializationInfo,
                        pName = name
                    };
                }

                pipelineCreateInfo.stageCount = (uint)description.ShaderSet.Shaders.Length;
                pipelineCreateInfo.pStages = pShaderStages;

                // and now everything is set up so that we can create our pipeline
                VulkanUtil.CheckResult(CreateGraphicsPipelines(_gd.Device, default, 1, &pipelineCreateInfo, null, &pipeline));
            }

            GC.KeepAlive(stringHolder);

            ArrayPool<VkPipelineColorBlendAttachmentState>.Shared.Return(blendAttachments);
            ArrayPool<VkVertexInputBindingDescription>.Shared.Return(bindingDescs);
            ArrayPool<VkVertexInputAttributeDescription>.Shared.Return(attribDescs);
            ArrayPool<VkPipelineShaderStageCreateInfo>.Shared.Return(shaderStages);
            if (specializationData is not null)
            {
                ArrayPool<byte>.Shared.Return(specializationData);
            }
            if (specializationMapEntries is not null)
            {
                ArrayPool<VkSpecializationMapEntry>.Shared.Return(specializationMapEntries);
            }

            // we now have our Vulkan pipeline object, lets create our wrapper object
            return new VulkanPipeline(_gd, description, ref pipeline, ref pipelineLayout);
        }
        finally
        {
            if (pipeline != VkPipeline.Zero)
            {
                DestroyPipeline(_gd.Device, pipeline, null);
            }

            if (pipelineLayout != VkPipelineLayout.Zero)
            {
                DestroyPipelineLayout(_gd.Device, pipelineLayout, null);
            }

            rpHolder?.DecRef();
        }
    }

    public unsafe override VulkanPipeline CreateComputePipeline(in ComputePipelineDescription description)
    {
        VkPipeline pipeline = default;
        VkPipelineLayout pipelineLayout = default;

        try
        {
            // Pipeline Layout
            pipelineLayout = CreatePipelineLayout(description.ResourceLayouts);

            // Set up shader specialization data
            var specializationDataSize = 0u;
            byte[]? specializationData = null;
            var specializationMapCount = 0u;
            VkSpecializationMapEntry[]? specializationMapEntries = null;
            if (description.Specializations is { } specDescs)
            {
                SetupSpecializationData(specDescs, out specializationDataSize, out specializationData, out specializationMapCount, out specializationMapEntries);
            }

            fixed (byte* pSpecializationData = specializationData)
            fixed (VkSpecializationMapEntry* pSpecializationMapEntries = specializationMapEntries)
            {
                var specInfo = new VkSpecializationInfo()
                {
                    dataSize = specializationDataSize,
                    pData = pSpecializationData,
                    mapEntryCount = specializationMapCount,
                    pMapEntries = pSpecializationMapEntries,
                };

                var shader = Util.AssertSubtype<Shader, VulkanShader>(description.ComputeShader);
                var nameHolder = shader.EntryPoint is "main" ? Vulkan.CommonStrings.main : new FixedUtf8String(shader.EntryPoint);

                var shaderCreateInfo = new VkPipelineShaderStageCreateInfo()
                {
                    module = shader.ShaderModule,
                    stage = VkFormats.VdToVkShaderStages(shader.Stage),
                    pSpecializationInfo = &specInfo,
                    pName = nameHolder
                };

                var pipelineCreateInfo = new VkComputePipelineCreateInfo()
                {
                    stage = shaderCreateInfo,
                    layout = pipelineLayout,
                };

                VulkanUtil.CheckResult(CreateComputePipelines(_gd.Device, default, 1, &pipelineCreateInfo, null, &pipeline));

                GC.KeepAlive(nameHolder);
            }

            // we now have the pipeline, create our wrapper
            return new VulkanPipeline(_gd, description, ref pipeline, ref pipelineLayout);
        }
        finally
        {
            if (pipeline != VkPipeline.Zero)
            {
                DestroyPipeline(_gd.Device, pipeline, null);
            }

            if (pipelineLayout != VkPipelineLayout.Zero)
            {
                DestroyPipelineLayout(_gd.Device, pipelineLayout, null);
            }
        }
    }
}
