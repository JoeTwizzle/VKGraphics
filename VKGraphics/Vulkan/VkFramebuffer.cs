using System.Diagnostics;
using static VKGraphics.Vulkan.VulkanUtil;

namespace VKGraphics.Vulkan;

internal unsafe class VkFramebuffer : VkFramebufferBase
{
    public override OpenTK.Graphics.Vulkan.VkFramebuffer CurrentFramebuffer => deviceFramebuffer;
    public override VkRenderPass RenderPassNoClearInit => renderPassNoClear;
    public override VkRenderPass RenderPassNoClearLoad => renderPassNoClearLoad;
    public override VkRenderPass RenderPassClear => renderPassClear;

    public override uint RenderableWidth => Width;
    public override uint RenderableHeight => Height;

    public override uint AttachmentCount { get; }

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
    private readonly OpenTK.Graphics.Vulkan.VkFramebuffer deviceFramebuffer;
    private readonly VkRenderPass renderPassNoClearLoad;
    private readonly VkRenderPass renderPassNoClear;
    private readonly VkRenderPass renderPassClear;
    private readonly List<VkImageView> attachmentViews = new List<VkImageView>();
    private bool destroyed;
    private string name;

    public VkFramebuffer(VkGraphicsDevice gd, ref FramebufferDescription description, bool isPresented)
        : base(description.DepthTarget, description.ColorTargets)
    {
        this.gd = gd;

        var renderPassCi = new VkRenderPassCreateInfo();

        var attachments = new StackList<VkAttachmentDescription>();

        uint colorAttachmentCount = (uint)ColorTargets.Count;
        var colorAttachmentRefs = new StackList<VkAttachmentReference>();

        for (int i = 0; i < colorAttachmentCount; i++)
        {
            var vkColorTex = Util.AssertSubtype<Texture, VkTexture>(ColorTargets[i].Target);
            var colorAttachmentDesc = new VkAttachmentDescription
            {
                format = vkColorTex.VkFormat,
                samples = vkColorTex.VkSampleCount,
                loadOp = VkAttachmentLoadOp.AttachmentLoadOpLoad,
                storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore,
                stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpDontCare,
                stencilStoreOp = VkAttachmentStoreOp.AttachmentStoreOpDontCare,
                initialLayout = isPresented
                    ? VkImageLayout.ImageLayoutPresentSrcKhr
                    : (vkColorTex.Usage & TextureUsage.Sampled) != 0
                        ? VkImageLayout.ImageLayoutShaderReadOnlyOptimal
                        : VkImageLayout.ImageLayoutColorAttachmentOptimal,
                finalLayout = VkImageLayout.ImageLayoutColorAttachmentOptimal
            };
            attachments.Add(colorAttachmentDesc);

            var colorAttachmentRef = new VkAttachmentReference
            {
                attachment = (uint)i,
                layout = VkImageLayout.ImageLayoutColorAttachmentOptimal
            };
            colorAttachmentRefs.Add(colorAttachmentRef);
        }

        var depthAttachmentDesc = new VkAttachmentDescription();
        var depthAttachmentRef = new VkAttachmentReference();

        if (DepthTarget != null)
        {
            var vkDepthTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);
            bool hasStencil = FormatHelpers.IsStencilFormat(vkDepthTex.Format);
            depthAttachmentDesc.format = vkDepthTex.VkFormat;
            depthAttachmentDesc.samples = vkDepthTex.VkSampleCount;
            depthAttachmentDesc.loadOp = VkAttachmentLoadOp.AttachmentLoadOpLoad;
            depthAttachmentDesc.storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore;
            depthAttachmentDesc.stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpDontCare;
            depthAttachmentDesc.stencilStoreOp = hasStencil
                ? VkAttachmentStoreOp.AttachmentStoreOpStore
                : VkAttachmentStoreOp.AttachmentStoreOpDontCare;
            depthAttachmentDesc.initialLayout = (vkDepthTex.Usage & TextureUsage.Sampled) != 0
                ? VkImageLayout.ImageLayoutShaderReadOnlyOptimal
                : VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;
            depthAttachmentDesc.finalLayout = VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;

            depthAttachmentRef.attachment = (uint)description.ColorTargets.Length;
            depthAttachmentRef.layout = VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;
        }

        var subpass = new VkSubpassDescription
        {
            pipelineBindPoint = VkPipelineBindPoint.PipelineBindPointGraphics
        };

        if (ColorTargets.Count > 0)
        {
            subpass.colorAttachmentCount = colorAttachmentCount;
            subpass.pColorAttachments = (VkAttachmentReference*)colorAttachmentRefs.Data;
        }

        if (DepthTarget != null)
        {
            subpass.pDepthStencilAttachment = &depthAttachmentRef;
            attachments.Add(depthAttachmentDesc);
        }

        var subpassDependency = new VkSubpassDependency
        {
            srcSubpass = Vk.SubpassExternal,
            srcStageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit,
            dstStageMask = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit,
            dstAccessMask = VkAccessFlagBits.AccessColorAttachmentReadBit | VkAccessFlagBits.AccessColorAttachmentWriteBit
        };

        renderPassCi.attachmentCount = attachments.Count;
        renderPassCi.pAttachments = (VkAttachmentDescription*)attachments.Data;
        renderPassCi.subpassCount = 1;
        renderPassCi.pSubpasses = &subpass;
        renderPassCi.dependencyCount = 1;
        renderPassCi.pDependencies = &subpassDependency;
        VkRenderPass vkrenderPassNoClear;
        var creationResult = Vk.CreateRenderPass(this.gd.Device, &renderPassCi, null, &vkrenderPassNoClear);
        renderPassNoClear = vkrenderPassNoClear;
        CheckResult(creationResult);

        for (int i = 0; i < colorAttachmentCount; i++)
        {
            attachments[i].loadOp = VkAttachmentLoadOp.AttachmentLoadOpLoad;
            attachments[i].initialLayout = VkImageLayout.ImageLayoutColorAttachmentOptimal;
        }

        if (DepthTarget != null)
        {
            attachments[attachments.Count - 1].loadOp = VkAttachmentLoadOp.AttachmentLoadOpLoad;
            attachments[attachments.Count - 1].initialLayout = VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;
            bool hasStencil = FormatHelpers.IsStencilFormat(DepthTarget.Value.Target.Format);
            if (hasStencil)
            {
                attachments[attachments.Count - 1].stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpLoad;
            }
        }
        VkRenderPass vkrenderPassNoClearLoad;
        creationResult = Vk.CreateRenderPass(this.gd.Device, &renderPassCi, null, &vkrenderPassNoClearLoad);
        renderPassNoClearLoad = vkrenderPassNoClearLoad;
        CheckResult(creationResult);

        // Load version

        if (DepthTarget != null)
        {
            attachments[attachments.Count - 1].loadOp = VkAttachmentLoadOp.AttachmentLoadOpClear;
            attachments[attachments.Count - 1].initialLayout = VkImageLayout.ImageLayoutUndefined;
            bool hasStencil = FormatHelpers.IsStencilFormat(DepthTarget.Value.Target.Format);
            if (hasStencil)
            {
                attachments[attachments.Count - 1].stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpClear;
            }
        }

        for (int i = 0; i < colorAttachmentCount; i++)
        {
            attachments[i].loadOp = VkAttachmentLoadOp.AttachmentLoadOpClear;
            attachments[i].initialLayout = VkImageLayout.ImageLayoutUndefined;
        }

        VkRenderPass vkrenderPassClear;
        creationResult = Vk.CreateRenderPass(this.gd.Device, &renderPassCi, null, &vkrenderPassClear);
        renderPassClear = vkrenderPassClear;
        CheckResult(creationResult);

        var fbCi = new VkFramebufferCreateInfo();
        uint fbAttachmentsCount = (uint)description.ColorTargets.Length;
        if (description.DepthTarget != null)
        {
            fbAttachmentsCount += 1;
        }

        var fbAttachments = stackalloc VkImageView[(int)fbAttachmentsCount];

        for (int i = 0; i < colorAttachmentCount; i++)
        {
            var vkColorTarget = Util.AssertSubtype<Texture, VkTexture>(description.ColorTargets[i].Target);
            var imageViewCi = new VkImageViewCreateInfo();
            imageViewCi.image = vkColorTarget.OptimalDeviceImage;
            imageViewCi.format = vkColorTarget.VkFormat;
            imageViewCi.viewType = VkImageViewType.ImageViewType2d;
            imageViewCi.subresourceRange = new VkImageSubresourceRange(
                VkImageAspectFlagBits.ImageAspectColorBit,
                description.ColorTargets[i].MipLevel,
                1,
                description.ColorTargets[i].ArrayLayer,
                1);
            var dest = fbAttachments + i;
            var result = Vk.CreateImageView(this.gd.Device, &imageViewCi, null, dest);
            CheckResult(result);
            attachmentViews.Add(*dest);
        }

        // Depth
        if (description.DepthTarget != null)
        {
            var vkDepthTarget = Util.AssertSubtype<Texture, VkTexture>(description.DepthTarget.Value.Target);
            bool hasStencil = FormatHelpers.IsStencilFormat(vkDepthTarget.Format);
            var depthViewCi = new VkImageViewCreateInfo();
            depthViewCi.image = vkDepthTarget.OptimalDeviceImage;
            depthViewCi.format = vkDepthTarget.VkFormat;
            depthViewCi.viewType = description.DepthTarget.Value.Target.ArrayLayers == 1
                ? VkImageViewType.ImageViewType2d
                : VkImageViewType.ImageViewType2dArray;
            depthViewCi.subresourceRange = new VkImageSubresourceRange(
                hasStencil ? VkImageAspectFlagBits.ImageAspectDepthBit | VkImageAspectFlagBits.ImageAspectStencilBit : VkImageAspectFlagBits.ImageAspectDepthBit,
                description.DepthTarget.Value.MipLevel,
                1,
                description.DepthTarget.Value.ArrayLayer,
                1);
            var dest = fbAttachments + (fbAttachmentsCount - 1);
            var result = Vk.CreateImageView(this.gd.Device, &depthViewCi, null, dest);
            CheckResult(result);
            attachmentViews.Add(*dest);
        }

        Texture dimTex;
        uint mipLevel;

        if (ColorTargets.Count > 0)
        {
            dimTex = ColorTargets[0].Target;
            mipLevel = ColorTargets[0].MipLevel;
        }
        else
        {
            Debug.Assert(DepthTarget != null);
            dimTex = DepthTarget.Value.Target;
            mipLevel = DepthTarget.Value.MipLevel;
        }

        Util.GetMipDimensions(
            dimTex,
            mipLevel,
            out uint mipWidth,
            out uint mipHeight,
            out _);

        fbCi.width = mipWidth;
        fbCi.height = mipHeight;

        fbCi.attachmentCount = fbAttachmentsCount;
        fbCi.pAttachments = fbAttachments;
        fbCi.layers = 1;
        fbCi.renderPass = renderPassNoClear;
        OpenTK.Graphics.Vulkan.VkFramebuffer vkdeviceFramebuffer;
        creationResult = Vk.CreateFramebuffer(this.gd.Device, &fbCi, null, &vkdeviceFramebuffer);
        deviceFramebuffer = vkdeviceFramebuffer;
        CheckResult(creationResult);

        if (DepthTarget != null)
        {
            AttachmentCount += 1;
        }

        AttachmentCount += (uint)ColorTargets.Count;
    }

    public override void TransitionToIntermediateLayout(VkCommandBuffer cb)
    {
        for (int i = 0; i < ColorTargets.Count; i++)
        {
            var ca = ColorTargets[i];
            var vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);
            vkTex.SetImageLayout(ca.MipLevel, ca.ArrayLayer, VkImageLayout.ImageLayoutColorAttachmentOptimal);
        }

        if (DepthTarget != null)
        {
            var vkTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);
            vkTex.SetImageLayout(
                DepthTarget.Value.MipLevel,
                DepthTarget.Value.ArrayLayer,
                VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal);
        }
    }

    public override void TransitionToFinalLayout(VkCommandBuffer cb)
    {
        for (int i = 0; i < ColorTargets.Count; i++)
        {
            var ca = ColorTargets[i];
            var vkTex = Util.AssertSubtype<Texture, VkTexture>(ca.Target);

            if ((vkTex.Usage & TextureUsage.Sampled) != 0)
            {
                vkTex.TransitionImageLayout(
                    cb,
                    ca.MipLevel, 1,
                    ca.ArrayLayer, 1,
                    VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
            }
        }

        if (DepthTarget != null)
        {
            var vkTex = Util.AssertSubtype<Texture, VkTexture>(DepthTarget.Value.Target);

            if ((vkTex.Usage & TextureUsage.Sampled) != 0)
            {
                vkTex.TransitionImageLayout(
                    cb,
                    DepthTarget.Value.MipLevel, 1,
                    DepthTarget.Value.ArrayLayer, 1,
                    VkImageLayout.ImageLayoutShaderReadOnlyOptimal);
            }
        }
    }

    protected override void DisposeCore()
    {
        if (!destroyed)
        {
            Vk.DestroyFramebuffer(gd.Device, deviceFramebuffer, null);
            Vk.DestroyRenderPass(gd.Device, renderPassNoClear, null);
            Vk.DestroyRenderPass(gd.Device, renderPassNoClearLoad, null);
            Vk.DestroyRenderPass(gd.Device, renderPassClear, null);
            foreach (var view in attachmentViews)
            {
                Vk.DestroyImageView(gd.Device, view, null);
            }

            destroyed = true;
        }
    }
}
