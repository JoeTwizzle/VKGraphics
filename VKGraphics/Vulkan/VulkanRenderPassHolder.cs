using OpenTK.Graphics.Vulkan;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal readonly struct RenderPassCacheKey : IEquatable<RenderPassCacheKey>
{
    public readonly ReadOnlyMemory<RenderPassAttachmentInfo> Attachments;
    public readonly bool HasDepthStencilAttachment;

    public bool IsDefault => Attachments.IsEmpty;

    public RenderPassCacheKey(ReadOnlyMemory<RenderPassAttachmentInfo> attachments, bool hasDepthStencil)
    {
        Attachments = attachments;
        HasDepthStencilAttachment = hasDepthStencil;
    }

    public RenderPassCacheKey ToOwned()
        => new(Attachments.ToArray(), HasDepthStencilAttachment);

    public override int GetHashCode()
    {
        var hc = new HashCode();

        foreach (var att in Attachments.Span)
        {
            hc.Add(att);
        }

        hc.Add(HasDepthStencilAttachment);

        return hc.ToHashCode();
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
        => obj is RenderPassCacheKey other && Equals(other);
    public bool Equals(RenderPassCacheKey other)
        => Attachments.Span.SequenceEqual(other.Attachments.Span)
        && HasDepthStencilAttachment == other.HasDepthStencilAttachment;
}

internal readonly record struct RenderPassAttachmentInfo(VkFormat Format, VkSampleCountFlagBits SampleCount, bool IsShaderRead, bool HasStencil);

internal sealed class VulkanRenderPassHolder : IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly RenderPassCacheKey _cacheKey;
    internal readonly VkRenderPass LoadOpLoad;
    internal readonly VkRenderPass LoadOpDontCare;
    internal readonly VkRenderPass LoadOpClear;

    public ResourceRefCount RefCount { get; }

    private VulkanRenderPassHolder(VulkanGraphicsDevice gd, RenderPassCacheKey key,
        VkRenderPass loadOpLoad, VkRenderPass loadOpDontCare, VkRenderPass loadOpClear)
    {
        _gd = gd;
        _cacheKey = key;
        LoadOpLoad = loadOpLoad;
        LoadOpDontCare = loadOpDontCare;
        LoadOpClear = loadOpClear;

        RefCount = new(this);
    }

    unsafe void IResourceRefCountTarget.RefZeroed()
    {
        _ = _gd._renderPasses.TryRemove(new(_cacheKey, this));

        DestroyRenderPass(_gd.Device, LoadOpLoad, null);
        DestroyRenderPass(_gd.Device, LoadOpDontCare, null);
        DestroyRenderPass(_gd.Device, LoadOpClear, null);
    }

    public void DecRef() => RefCount.Decrement();

    // note: returns with RefCount = 1, when caller is done, they must DecRef()
    internal static VulkanRenderPassHolder GetRenderPassHolder(VulkanGraphicsDevice gd, RenderPassCacheKey cacheKey)
    {
        RenderPassCacheKey owned = default;
        VulkanRenderPassHolder? holder = null;
        bool createdHolder;
        do
        {
            createdHolder = false;
            if (gd._renderPasses.TryGetValue(cacheKey, out holder))
            {
                // got a holder, fall out and make sure it's not closed
            }
            else
            {
                if (owned.IsDefault)
                {
                    owned = cacheKey.ToOwned();
                }

                createdHolder = true;
                var newHolder = CreateRenderPasses(owned, gd);
                holder = gd._renderPasses.GetOrAdd(owned, newHolder);
                if (holder != newHolder)
                {
                    // this means someone else beat us, make sure to clean ourselves up
                    newHolder.DecRef();
                    createdHolder = false;
                }
            }
        }
        while (holder is null || holder.RefCount.IsClosed);

        // once we've selected a holder, increment the refcount (if we weren't the one to create it
        if (!createdHolder)
        {
            holder.RefCount.Increment();
        }
        return holder;
    }


    private static unsafe VulkanRenderPassHolder CreateRenderPasses(RenderPassCacheKey key, VulkanGraphicsDevice gd)
    {
        VkRenderPass rpLoad = default;
        VkRenderPass rpDontCare = default;
        VkRenderPass rpClear = default;

        try
        {
            var attachmentSpan = key.Attachments.Span;
            var totalAtts = attachmentSpan.Length;
            var colorAtts = key.HasDepthStencilAttachment ? totalAtts - 1 : totalAtts;
            var vkAtts = ArrayPool<VkAttachmentDescription>.Shared.Rent(totalAtts);
            var vkAttRefs = ArrayPool<VkAttachmentReference>.Shared.Rent(totalAtts);

            for (var i = 0; i < totalAtts; i++)
            {
                var isDepthStencil = i >= colorAtts;
                var desc = attachmentSpan[i];

                var layout = desc.IsShaderRead
                    ? VkImageLayout.ImageLayoutGeneral
                    : isDepthStencil
                    ? VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal
                    : VkImageLayout.ImageLayoutColorAttachmentOptimal;

                vkAtts[i] = new()
                {
                    format = desc.Format,
                    samples = desc.SampleCount,

                    // first, we'll create the LOAD_OP_LOAD render passes
                    loadOp = VkAttachmentLoadOp.AttachmentLoadOpLoad,
                    storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore,
                    stencilLoadOp = desc.HasStencil ? VkAttachmentLoadOp.AttachmentLoadOpLoad : VkAttachmentLoadOp.AttachmentLoadOpDontCare,
                    stencilStoreOp = desc.HasStencil ? VkAttachmentStoreOp.AttachmentStoreOpStore : VkAttachmentStoreOp.AttachmentStoreOpDontCare,

                    // layouts shouldn't change due to render passes
                    initialLayout = layout,
                    finalLayout = layout,
                };
                vkAttRefs[i] = new()
                {
                    attachment = (uint)i,
                    layout = layout,
                };
            }

            ReadOnlySpan<VkSubpassDependency> subpassDeps = [
                new VkSubpassDependency()
                {
                    srcSubpass = SubpassExternal,
                    srcStageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit,
                    dstStageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit,
                    dstAccessMask = VkAccessFlagBits.AccessColorAttachmentReadBit | VkAccessFlagBits.AccessColorAttachmentWriteBit,
                },
                new VkSubpassDependency()
                {
                    srcSubpass = 0,
                    dstSubpass = 0,
                    srcStageMask =
                        VkPipelineStageFlagBits.PipelineStageFragmentShaderBit |
                        VkPipelineStageFlagBits.PipelineStageEarlyFragmentTestsBit |
                        VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit |
                        VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit,
                    dstStageMask =
                        VkPipelineStageFlagBits.PipelineStageFragmentShaderBit |
                        VkPipelineStageFlagBits.PipelineStageEarlyFragmentTestsBit |
                        VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit |
                        VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit,
                    srcAccessMask =
                        VkAccessFlagBits.AccessColorAttachmentReadBit |
                        VkAccessFlagBits.AccessColorAttachmentWriteBit |
                        VkAccessFlagBits.AccessDepthStencilAttachmentReadBit |
                        VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit |
                        VkAccessFlagBits.AccessShaderReadBit |
                        VkAccessFlagBits.AccessShaderWriteBit,
                    dstAccessMask =
                        VkAccessFlagBits.AccessColorAttachmentReadBit |
                        VkAccessFlagBits.AccessColorAttachmentWriteBit |
                        VkAccessFlagBits.AccessDepthStencilAttachmentReadBit |
                        VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit |
                        VkAccessFlagBits.AccessShaderReadBit |
                        VkAccessFlagBits.AccessShaderWriteBit,

                    dependencyFlags = VkDependencyFlagBits.DependencyByRegionBit, // REQUIRED by Vulkan
                },
            ];

            fixed (VkAttachmentDescription* pVkAtts = vkAtts)
            fixed (VkAttachmentReference* pVkAttRefs = vkAttRefs)
            fixed (VkSubpassDependency* pSubpassDeps = subpassDeps)
            {
                var subpassDesc = new VkSubpassDescription()
                {
                    pipelineBindPoint = VkPipelineBindPoint.PipelineBindPointGraphics,
                    colorAttachmentCount = (uint)colorAtts,
                    pColorAttachments = pVkAttRefs,
                    pDepthStencilAttachment = key.HasDepthStencilAttachment ? pVkAttRefs + colorAtts : null,
                };


                var renderPassCreateInfo = new VkRenderPassCreateInfo()
                {
                    attachmentCount = (uint)totalAtts,
                    pAttachments = pVkAtts,
                    subpassCount = 1,
                    pSubpasses = &subpassDesc,
                    dependencyCount = (uint)subpassDeps.Length,
                    pDependencies = pSubpassDeps,
                };

                // our create info is all set up, now create our first variant, the Load variant
                VulkanUtil.CheckResult(CreateRenderPass(gd.Device, &renderPassCreateInfo, null, &rpLoad));

                // next, create the DONT_CARE variants
                for (var i = 0; i < totalAtts; i++)
                {
                    ref var desc = ref vkAtts[i];
                    desc.loadOp = VkAttachmentLoadOp.AttachmentLoadOpDontCare;
                    desc.stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpDontCare;
                }
                VulkanUtil.CheckResult(CreateRenderPass(gd.Device, &renderPassCreateInfo, null, &rpDontCare));

                // finally, the CLEAR variants
                for (var i = 0; i < totalAtts; i++)
                {
                    ref var desc = ref vkAtts[i];
                    desc.loadOp = VkAttachmentLoadOp.AttachmentLoadOpClear;
                    if (attachmentSpan[i].HasStencil)
                    {
                        desc.stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpClear;
                    }
                }
                VulkanUtil.CheckResult(CreateRenderPass(gd.Device, &renderPassCreateInfo, null, &rpClear));

            }

            var result = new VulkanRenderPassHolder(gd, key, rpLoad, rpDontCare, rpClear);
            rpLoad = default;
            rpDontCare = default;
            rpClear = default;

            ArrayPool<VkAttachmentDescription>.Shared.Return(vkAtts);
            ArrayPool<VkAttachmentReference>.Shared.Return(vkAttRefs);

            return result;
        }
        finally
        {
            if (rpLoad != VkRenderPass.Zero)
            {
                DestroyRenderPass(gd.Device, rpLoad, null);
            }
            if (rpDontCare != VkRenderPass.Zero)
            {
                DestroyRenderPass(gd.Device, rpDontCare, null);
            }
            if (rpClear != VkRenderPass.Zero)
            {
                DestroyRenderPass(gd.Device, rpClear, null);
            }
        }
    }
}
