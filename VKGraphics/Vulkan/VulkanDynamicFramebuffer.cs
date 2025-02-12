using System;
using System.Diagnostics;
using System.Buffers;

using OpenTK.Graphics.Vulkan;
using static OpenTK.Graphics.Vulkan.VkStructureType;

namespace VKGraphics.Vulkan;

// VulkanDynamicFramebuffer uses Vulkan's new dynamic_rendering APIs, which doesn't require the construction of explicit render passes
// or framebuffer objects. Using this enables framebuffers to be much cheaper to construct, when available.

internal sealed class VulkanDynamicFramebuffer : VulkanFramebuffer, IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly VulkanTextureView? _depthTargetView;
    private readonly VulkanTextureView[] _colorTargetViews;

    // we don't actually have a backing object for the name
    public override string? Name { get; set; }

    public override ResourceRefCount RefCount { get; }

    internal unsafe VulkanDynamicFramebuffer(VulkanGraphicsDevice gd, in FramebufferDescription description,
        VulkanTextureView? depthTargetView, VulkanTextureView[] colorTextureViews)
        : base(description.DepthTarget, description.ColorTargets)
    {
        _gd = gd;
        _depthTargetView = depthTargetView;
        _colorTargetViews = colorTextureViews;

        Debug.Assert(gd._deviceCreateState.HasDynamicRendering);
        Debug.Assert(gd.CmdBeginRendering is not null);
        Debug.Assert(gd.CmdEndRendering is not null);

        RefCount = new(this);
    }

    void IResourceRefCountTarget.RefZeroed()
    {
        // we are the unique owners of these image views
        _depthTargetView?.Dispose();
        if (_colorTargetViews is not null)
        {
            foreach (var target in _colorTargetViews)
            {
                target?.Dispose();
            }
        }
    }

    public override unsafe void StartRenderPass(VulkanCommandList cl, VkCommandBuffer cb, bool firstBinding,
        VkClearValue? depthClear, ReadOnlySpan<VkClearValue> colorTargetClear, ReadOnlySpan<bool> setColorClears)
    {
        // we'll also put the depth and stencil in here, for convenience
        var attachments = ArrayPool<VkRenderingAttachmentInfo>.Shared.Rent(_colorTargetViews.Length + 2);

        var hasDepthTarget = false;
        var hasStencil = false;
        if (_depthTargetView is { } depthTarget)
        {
            hasDepthTarget = true;
            hasStencil = FormatHelpers.IsStencilFormat(depthTarget.Format);

            var targetLayout =
                (depthTarget.Target.Usage & TextureUsage.Sampled) != 0
                ? VkImageLayout.ImageLayoutGeneral // TODO: it might be possible to do better
                : VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;

            var loadOp = depthClear is not null
                ? VkAttachmentLoadOp.AttachmentLoadOpClear
                : firstBinding
                ? VkAttachmentLoadOp.AttachmentLoadOpDontCare
                : VkAttachmentLoadOp.AttachmentLoadOpLoad;

            cl.SyncResource(depthTarget, new()
            {
                Layout = targetLayout,
                BarrierMasks = new()
                {
                    AccessMask = (loadOp == VkAttachmentLoadOp.AttachmentLoadOpLoad
                        ? VkAccessFlagBits.AccessDepthStencilAttachmentReadBit
                        : 0) | VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit,
                    StageMask = 
                    VkPipelineStageFlagBits.PipelineStageFragmentShaderBit
                    | VkPipelineStageFlagBits.PipelineStageEarlyFragmentTestsBit
                    | VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit,
                }
            });

            // [0] is depth
            attachments[0] = new()
            {
                imageView = depthTarget.ImageView,
                imageLayout = targetLayout,
                resolveMode = VkResolveModeFlagBits.ResolveModeNone, // do not resolve
                loadOp = loadOp is VkAttachmentLoadOp.AttachmentLoadOpDontCare ? VkAttachmentLoadOp.AttachmentLoadOpLoad : loadOp,
                storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore,
                clearValue = depthClear.GetValueOrDefault(),
            };

            if (hasStencil)
            {
                // [1] is stencil
                attachments[1] = new()
                {
                    imageView = depthTarget.ImageView,
                    imageLayout = targetLayout,
                    resolveMode = VkResolveModeFlagBits.ResolveModeNone, // do not resolve
                    loadOp = loadOp,
                    storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore,
                    clearValue = depthClear.GetValueOrDefault(),
                };
            }
        }

        // now the color targets
        for (var i = 0; i < _colorTargetViews.Length; i++)
        {
            var target = _colorTargetViews[i];

            var targetLayout =
                (target.Target.Usage & TextureUsage.Sampled) != 0
                ? VkImageLayout.ImageLayoutGeneral // TODO: it should definitely be possible to do better
                : VkImageLayout.ImageLayoutColorAttachmentOptimal;

            var loadOp = setColorClears[i] ? VkAttachmentLoadOp.AttachmentLoadOpClear : VkAttachmentLoadOp.AttachmentLoadOpLoad;

            cl.SyncResource(target, new()
            {
                Layout = targetLayout,
                BarrierMasks = new()
                {
                    StageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit,
                    AccessMask = (loadOp == VkAttachmentLoadOp.AttachmentLoadOpLoad
                        ? VkAccessFlagBits.AccessColorAttachmentReadBit
                        : 0) | VkAccessFlagBits.AccessColorAttachmentWriteBit,
                }
            });

            attachments[2 + i] = new()
            {
                imageView = target.ImageView,
                imageLayout = targetLayout,
                resolveMode = VkResolveModeFlagBits.ResolveModeNone,
                loadOp = loadOp,
                storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore,
                clearValue = colorTargetClear[i],
            };
        }

        // emit sunchro before actually starting the render pass
        // render passes will be SHORT, and pretty much only single dispatches/dispatch sets, so we can avoid the problem of emitting synchro inside the render pass
        cl.EmitQueuedSynchro();

        fixed (VkRenderingAttachmentInfo* pAttachments = attachments)
        {
            var renderingInfo = new VkRenderingInfo()
            {
                flags = 0,
                renderArea = new()
                {
                    offset = default,
                    extent = RenderableExtent
                },
                layerCount = 1,
                viewMask = 0,
                colorAttachmentCount = (uint)_colorTargetViews.Length,
                pColorAttachments = pAttachments + 2, // [2] is the first color attachment
                pDepthAttachment = hasDepthTarget ? pAttachments + 0 : null, // [0] is depth
                pStencilAttachment = hasStencil ? pAttachments + 1 : null, // [1] is stencil
            };

            _gd.CmdBeginRendering(cb, &renderingInfo);
        }

        ArrayPool<VkRenderingAttachmentInfo>.Shared.Return(attachments);
    }

    public override unsafe void EndRenderPass(VulkanCommandList cl, VkCommandBuffer cb)
    {
        _gd.CmdEndRendering(cb);
    }
}
