using OpenTK.Graphics.Vulkan;
using System.Buffers;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal sealed class VulkanRenderPassFramebuffer : VulkanFramebuffer, IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly VulkanRenderPassHolder _rpHolder;
    private readonly VkFramebuffer _framebuffer;
    private readonly VulkanTextureView[]? _colorAttachments;
    private readonly VulkanTextureView? _depthAttachment;
    private string? _name;

    public override ResourceRefCount RefCount { get; }

    internal VulkanRenderPassFramebuffer(VulkanGraphicsDevice gd, FramebufferDescription description,
        VkFramebuffer framebuffer, VulkanRenderPassHolder holder,
        VulkanTextureView[]? attachments, VulkanTextureView? depthAttachment)
        : base(description.DepthTarget, description.ColorTargets)
    {
        _gd = gd;
        _framebuffer = framebuffer;
        _rpHolder = holder;
        _colorAttachments = attachments;
        _depthAttachment = depthAttachment;

        RefCount = new(this);
    }

    public override void Dispose() => RefCount?.DecrementDispose();

    unsafe void IResourceRefCountTarget.RefZeroed()
    {
        DestroyFramebuffer(_gd.Device, _framebuffer, null);
        _rpHolder.DecRef();

        foreach (var att in _colorAttachments.AsSpan())
        {
            att.Dispose();
        }
        _depthAttachment?.Dispose();
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeFramebufferExt, _framebuffer.Handle, value);
        }
    }

    public override unsafe void StartRenderPass(VulkanCommandList cl, VkCommandBuffer cb, bool firstBinding,
        VkClearValue? depthClear, ReadOnlySpan<VkClearValue> colorTargetClear, ReadOnlySpan<bool> setColorClears)
    {
        var haveAnyAttachments = false;
        var haveDepthAttachment = false;

        if (_depthAttachment is { } depthTarget)
        {
            haveAnyAttachments = true;
            haveDepthAttachment = true;

            var targetLayout =
                (depthTarget.Target.Usage & TextureUsage.Sampled) != 0
                ? VkImageLayout.ImageLayoutGeneral // TODO: it might be possible to do better
                : VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;

            cl.SyncResource(depthTarget, new()
            {
                Layout = targetLayout,
                BarrierMasks = new()
                {
                    AccessMask = (!depthClear.HasValue
                        ? VkAccessFlagBits.AccessDepthStencilAttachmentReadBit
                        : 0) | VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit,
                    StageMask =
                    VkPipelineStageFlagBits.PipelineStageFragmentShaderBit
                    | VkPipelineStageFlagBits.PipelineStageEarlyFragmentTestsBit
                    | VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit,
                }
            });
        }

        // now the color targets
        var colorAttSpan = _colorAttachments.AsSpan();
        for (var i = 0; i < colorAttSpan.Length; i++)
        {
            haveAnyAttachments = true;
            var target = colorAttSpan[i];

            var targetLayout =
                (target.Target.Usage & TextureUsage.Sampled) != 0
                ? VkImageLayout.ImageLayoutGeneral // TODO: it should definitely be possible to do better
                : VkImageLayout.ImageLayoutColorAttachmentOptimal;

            cl.SyncResource(target, new()
            {
                Layout = targetLayout,
                BarrierMasks = new()
                {
                    StageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit,
                    AccessMask = (!setColorClears[i]
                        ? VkAccessFlagBits.AccessColorAttachmentReadBit
                        : 0) | VkAccessFlagBits.AccessColorAttachmentWriteBit,
                }
            });
        }

        // emit sunchro before actually starting the render pass
        // render passes will be SHORT, and pretty much only single dispatches/dispatch sets, so we can avoid the problem of emitting synchro inside the render pass
        cl.EmitQueuedSynchro();

        var haveAllClearValues = depthClear.HasValue;
        var haveAnyClearValues = depthClear.HasValue;
        foreach (var hasClear in setColorClears)
        {
            if (hasClear)
            {
                haveAnyClearValues = true;
            }
            else
            {
                haveAllClearValues = false;
            }
        }

        var beginInfo = new VkRenderPassBeginInfo()
        {
            framebuffer = _framebuffer,
            renderArea = new()
            {
                offset = default,
                extent = RenderableExtent,
            }
        };

        if (!haveAnyAttachments || !haveAllClearValues)
        {
            beginInfo.renderPass = firstBinding ? _rpHolder.LoadOpDontCare : _rpHolder.LoadOpLoad;
            CmdBeginRenderPass(cb, &beginInfo, VkSubpassContents.SubpassContentsInline);

            if (haveAnyClearValues)
            {
                if (depthClear is { } depthClearValue)
                {
                    var att = new VkClearAttachment()
                    {
                        aspectMask = VkImageAspectFlagBits.ImageAspectDepthBit
                            | (FormatHelpers.IsStencilFormat(_depthAttachment!.Format) ? VkImageAspectFlagBits.ImageAspectStencilBit : 0),
                        clearValue = depthClearValue,
                        colorAttachment = (uint)colorAttSpan.Length,
                    };

                    var rect = new VkClearRect()
                    {
                        baseArrayLayer = _depthAttachment!.BaseArrayLayer,
                        layerCount = _depthAttachment!.RealArrayLayers,
                        rect = new()
                        {
                            offset = default,
                            extent = RenderableExtent,
                        }
                    };
                    CmdClearAttachments(cb, 1, &att, 1, &rect);
                }

                for (var i = 0u; i < colorAttSpan.Length; i++)
                {
                    if (setColorClears[(int)i])
                    {
                        var att = new VkClearAttachment()
                        {
                            aspectMask = VkImageAspectFlagBits.ImageAspectColorBit,
                            clearValue = colorTargetClear[(int)i],
                            colorAttachment = i,
                        };

                        var rect = new VkClearRect()
                        {
                            baseArrayLayer = _depthAttachment!.BaseArrayLayer,
                            layerCount = _depthAttachment!.RealArrayLayers,
                            rect = new()
                            {
                                offset = default,
                                extent = RenderableExtent,
                            }
                        };
                        CmdClearAttachments(cb, 1, &att, 1, &rect);
                    }
                }
            }

        }
        else
        {
            cl.EmitQueuedSynchro();

            // we have clear values for every attachment, use the clear LoadOp RenderPass
            beginInfo.renderPass = _rpHolder.LoadOpClear;
            if (haveDepthAttachment)
            {
                // we have a depth attachment, we need more space than we have in colorTargetClear
                var clearValues = ArrayPool<VkClearValue>.Shared.Rent(colorAttSpan.Length + 1);
                beginInfo.clearValueCount = (uint)colorAttSpan.Length + 1;
                colorTargetClear.CopyTo(clearValues);
                clearValues[colorAttSpan.Length] = depthClear!.Value;

                fixed (VkClearValue* pClearValues = clearValues)
                {
                    beginInfo.pClearValues = pClearValues;
                    CmdBeginRenderPass(cb, &beginInfo, VkSubpassContents.SubpassContentsInline);
                }

                ArrayPool<VkClearValue>.Shared.Return(clearValues);
            }
            else
            {
                // we don't have a depth attachment, we can just use the passed-in span
                beginInfo.clearValueCount = (uint)colorAttSpan.Length;
                fixed (VkClearValue* pClearValues = colorTargetClear)
                {
                    beginInfo.pClearValues = pClearValues;
                    CmdBeginRenderPass(cb, &beginInfo, VkSubpassContents.SubpassContentsInline);
                }
            }
        }
    }

    public override void EndRenderPass(VulkanCommandList cl, VkCommandBuffer cb)
    {
        CmdEndRenderPass(cb);
    }
}
