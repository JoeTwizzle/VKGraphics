using System.Runtime.CompilerServices;
using static VKGraphics.Vulkan.VulkanUtil;

namespace VKGraphics.Vulkan;

internal unsafe class VkPipeline : Pipeline
{
    public OpenTK.Graphics.Vulkan.VkPipeline DevicePipeline => devicePipeline;

    public VkPipelineLayout PipelineLayout => pipelineLayout;

    public uint ResourceSetCount { get; }
    public uint DynamicOffsetsCount { get; }
    public bool ScissorTestEnabled { get; }

    public override bool IsComputePipeline { get; }

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
    private readonly OpenTK.Graphics.Vulkan.VkPipeline devicePipeline;
    private readonly VkPipelineLayout pipelineLayout;
    private readonly VkRenderPass renderPass;
    private bool destroyed;
    private string? name;

    public VkPipeline(VkGraphicsDevice gd, ref GraphicsPipelineDescription description)
        : base(ref description)
    {
        this.gd = gd;
        IsComputePipeline = false;
        RefCount = new ResourceRefCount(disposeCore);

        var pipelineCi = new VkGraphicsPipelineCreateInfo();

        // Blend State
        var blendStateCi = new VkPipelineColorBlendStateCreateInfo();
        int attachmentsCount = description.BlendState.AttachmentStates.Length;
        var attachmentsPtr
            = stackalloc VkPipelineColorBlendAttachmentState[attachmentsCount];

        for (int i = 0; i < attachmentsCount; i++)
        {
            var vdDesc = description.BlendState.AttachmentStates[i];
            var attachmentState = new VkPipelineColorBlendAttachmentState
            {
                srcColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceColorFactor),
                dstColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationColorFactor),
                colorBlendOp = VkFormats.VdToVkBlendOp(vdDesc.ColorFunction),
                srcAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceAlphaFactor),
                dstAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationAlphaFactor),
                alphaBlendOp = VkFormats.VdToVkBlendOp(vdDesc.AlphaFunction),
                colorWriteMask = VkFormats.VdToVkColorWriteMask(vdDesc.ColorWriteMask.GetOrDefault()),
                blendEnable = vdDesc.BlendEnabled ? 1 : 0
            };
            attachmentsPtr[i] = attachmentState;
        }

        blendStateCi.attachmentCount = (uint)attachmentsCount;
        blendStateCi.pAttachments = attachmentsPtr;
        var blendFactor = description.BlendState.BlendFactor;
        blendStateCi.blendConstants[0] = blendFactor.R;
        blendStateCi.blendConstants[1] = blendFactor.G;
        blendStateCi.blendConstants[2] = blendFactor.B;
        blendStateCi.blendConstants[3] = blendFactor.A;

        pipelineCi.pColorBlendState = &blendStateCi;

        // Rasterizer State
        var rsDesc = description.RasterizerState;
        var rsCi = new VkPipelineRasterizationStateCreateInfo();
        rsCi.cullMode = VkFormats.VdToVkCullMode(rsDesc.CullMode);
        rsCi.polygonMode = VkFormats.VdToVkPolygonMode(rsDesc.FillMode);
        rsCi.depthClampEnable = (!rsDesc.DepthClipEnabled ? 1 : 0);
        rsCi.frontFace = rsDesc.FrontFace == FrontFace.Clockwise ? VkFrontFace.FrontFaceClockwise : VkFrontFace.FrontFaceCounterClockwise;
        rsCi.lineWidth = 1f;

        pipelineCi.pRasterizationState = &rsCi;

        ScissorTestEnabled = rsDesc.ScissorTestEnabled;

        // Dynamic State
        var dynamicStateCi = new VkPipelineDynamicStateCreateInfo();
        var dynamicStates = stackalloc VkDynamicState[2];
        dynamicStates[0] = VkDynamicState.DynamicStateViewport;
        dynamicStates[1] = VkDynamicState.DynamicStateScissor;
        dynamicStateCi.dynamicStateCount = 2;
        dynamicStateCi.pDynamicStates = dynamicStates;

        pipelineCi.pDynamicState = &dynamicStateCi;

        // Depth Stencil State
        var vdDssDesc = description.DepthStencilState;
        var dssCi = new VkPipelineDepthStencilStateCreateInfo();
        dssCi.depthWriteEnable = vdDssDesc.DepthWriteEnabled ? 1 : 0;
        dssCi.depthTestEnable = vdDssDesc.DepthTestEnabled ? 1 : 0;
        dssCi.depthCompareOp = VkFormats.VdToVkCompareOp(vdDssDesc.DepthComparison);
        dssCi.stencilTestEnable = vdDssDesc.StencilTestEnabled ? 1 : 0;

        dssCi.front.failOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.Fail);
        dssCi.front.passOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.Pass);
        dssCi.front.depthFailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.DepthFail);
        dssCi.front.compareOp = VkFormats.VdToVkCompareOp(vdDssDesc.StencilFront.Comparison);
        dssCi.front.compareMask = vdDssDesc.StencilReadMask;
        dssCi.front.writeMask = vdDssDesc.StencilWriteMask;
        dssCi.front.reference = vdDssDesc.StencilReference;

        dssCi.back.failOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.Fail);
        dssCi.back.passOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.Pass);
        dssCi.back.depthFailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.DepthFail);
        dssCi.back.compareOp = VkFormats.VdToVkCompareOp(vdDssDesc.StencilBack.Comparison);
        dssCi.back.compareMask = vdDssDesc.StencilReadMask;
        dssCi.back.writeMask = vdDssDesc.StencilWriteMask;
        dssCi.back.reference = vdDssDesc.StencilReference;

        pipelineCi.pDepthStencilState = &dssCi;

        // Multisample
        var multisampleCi = new VkPipelineMultisampleStateCreateInfo();
        var vkSampleCount = VkFormats.VdToVkSampleCount(description.Outputs.SampleCount);
        multisampleCi.rasterizationSamples = vkSampleCount;
        multisampleCi.alphaToCoverageEnable = description.BlendState.AlphaToCoverageEnabled ? 1 : 0;

        pipelineCi.pMultisampleState = &multisampleCi;

        // Input Assembly
        var inputAssemblyCi = new VkPipelineInputAssemblyStateCreateInfo
        {
            topology = VkFormats.VdToVkPrimitiveTopology(description.PrimitiveTopology)
        };

        pipelineCi.pInputAssemblyState = &inputAssemblyCi;

        // Vertex Input State
        var vertexInputCi = new VkPipelineVertexInputStateCreateInfo();

        ReadOnlySpan<VertexLayoutDescription> inputDescriptions = description.ShaderSet.VertexLayouts;
        uint bindingCount = (uint)inputDescriptions.Length;
        uint attributeCount = 0;
        for (int i = 0; i < inputDescriptions.Length; i++)
        {
            attributeCount += (uint)inputDescriptions[i].Elements.Length;
        }

        var bindingDescs = stackalloc VkVertexInputBindingDescription[(int)bindingCount];
        var attributeDescs = stackalloc VkVertexInputAttributeDescription[(int)attributeCount];

        int targetIndex = 0;
        int targetLocation = 0;

        for (int binding = 0; binding < inputDescriptions.Length; binding++)
        {
            var inputDesc = inputDescriptions[binding];
            bindingDescs[binding] = new VkVertexInputBindingDescription
            {
                binding = (uint)binding,
                inputRate = inputDesc.InstanceStepRate != 0 ? VkVertexInputRate.VertexInputRateInstance : VkVertexInputRate.VertexInputRateVertex,
                stride = inputDesc.Stride
            };

            uint currentOffset = 0;

            for (int location = 0; location < inputDesc.Elements.Length; location++)
            {
                var inputElement = inputDesc.Elements[location];

                attributeDescs[targetIndex] = new VkVertexInputAttributeDescription
                {
                    format = VkFormats.VdToVkVertexElementFormat(inputElement.Format),
                    binding = (uint)binding,
                    location = (uint)(targetLocation + location),
                    offset = inputElement.Offset != 0 ? inputElement.Offset : currentOffset
                };

                targetIndex += 1;
                currentOffset += FormatSizeHelpers.GetSizeInBytes(inputElement.Format);
            }

            targetLocation += inputDesc.Elements.Length;
        }

        vertexInputCi.vertexBindingDescriptionCount = bindingCount;
        vertexInputCi.pVertexBindingDescriptions = bindingDescs;
        vertexInputCi.vertexAttributeDescriptionCount = attributeCount;
        vertexInputCi.pVertexAttributeDescriptions = attributeDescs;

        pipelineCi.pVertexInputState = &vertexInputCi;

        // Shader Stage

        VkSpecializationInfo specializationInfo;
        var specDescs = description.ShaderSet.Specializations;

        if (specDescs != null)
        {
            uint specDataSize = 0;
            foreach (var spec in specDescs)
            {
                specDataSize += VkFormats.GetSpecializationConstantSize(spec.Type);
            }

            byte* fullSpecData = stackalloc byte[(int)specDataSize];
            int specializationCount = specDescs.Length;
            var mapEntries = stackalloc VkSpecializationMapEntry[specializationCount];
            uint specOffset = 0;

            for (int i = 0; i < specializationCount; i++)
            {
                ulong data = specDescs[i].Data;
                byte* srcData = (byte*)&data;
                uint dataSize = VkFormats.GetSpecializationConstantSize(specDescs[i].Type);
                Unsafe.CopyBlock(fullSpecData + specOffset, srcData, dataSize);
                mapEntries[i].constantID = specDescs[i].ID;
                mapEntries[i].offset = specOffset;
                mapEntries[i].size = dataSize;
                specOffset += dataSize;
            }

            specializationInfo.dataSize = specDataSize;
            specializationInfo.pData = fullSpecData;
            specializationInfo.mapEntryCount = (uint)specializationCount;
            specializationInfo.pMapEntries = mapEntries;
        }

        var shaders = description.ShaderSet.Shaders;
        var stages = new StackList<VkPipelineShaderStageCreateInfo>();

        foreach (var shader in shaders)
        {
            var vkShader = Util.AssertSubtype<Shader, VkShader>(shader);
            var stageCi = new VkPipelineShaderStageCreateInfo
            {
                module = vkShader.ShaderModule,
                stage = VkFormats.VdToVkShaderStages(shader.Stage),
                // stageCI.pName = CommonStrings.main; // Meh
                pName = new FixedUtf8String(shader.EntryPoint), // TODO: DONT ALLOCATE HERE
                pSpecializationInfo = &specializationInfo
            };
            stages.Add(stageCi);
        }

        pipelineCi.stageCount = stages.Count;
        pipelineCi.pStages = (VkPipelineShaderStageCreateInfo*)stages.Data;

        // ViewportState
        var viewportStateCi = new VkPipelineViewportStateCreateInfo
        {
            viewportCount = 1,
            scissorCount = 1
        };

        pipelineCi.pViewportState = &viewportStateCi;

        // Pipeline Layout
        var resourceLayouts = description.ResourceLayouts;
        var pipelineLayoutCi = new VkPipelineLayoutCreateInfo
        {
            setLayoutCount = (uint)resourceLayouts.Length
        };
        var dsls = stackalloc VkDescriptorSetLayout[resourceLayouts.Length];
        for (int i = 0; i < resourceLayouts.Length; i++)
        {
            dsls[i] = Util.AssertSubtype<ResourceLayout, VkResourceLayout>(resourceLayouts[i]).DescriptorSetLayout;
        }

        pipelineLayoutCi.pSetLayouts = dsls;
        VkPipelineLayout vkpipelineLayout;
        Vk.CreatePipelineLayout(this.gd.Device, &pipelineLayoutCi, null, &vkpipelineLayout);
        pipelineLayout = vkpipelineLayout;
        pipelineCi.layout = pipelineLayout;

        // Create fake RenderPass for compatibility.

        var renderPassCi = new VkRenderPassCreateInfo();
        var outputDesc = description.Outputs;
        var attachments = new StackList<VkAttachmentDescription, Size512Bytes>();

        // TODO: A huge portion of this next part is duplicated in VkFramebuffer.cs.

        var colorAttachmentDescs = new StackList<VkAttachmentDescription>();
        var colorAttachmentRefs = new StackList<VkAttachmentReference>();

        for (uint i = 0; i < outputDesc.ColorAttachments.Length; i++)
        {
            colorAttachmentDescs[i].format = VkFormats.VdToVkPixelFormat(outputDesc.ColorAttachments[i].Format);
            colorAttachmentDescs[i].samples = vkSampleCount;
            colorAttachmentDescs[i].loadOp = VkAttachmentLoadOp.AttachmentLoadOpDontCare;
            colorAttachmentDescs[i].storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore;
            colorAttachmentDescs[i].stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpDontCare;
            colorAttachmentDescs[i].stencilStoreOp = VkAttachmentStoreOp.AttachmentStoreOpDontCare;
            colorAttachmentDescs[i].initialLayout = VkImageLayout.ImageLayoutUndefined;
            colorAttachmentDescs[i].finalLayout = VkImageLayout.ImageLayoutShaderReadOnlyOptimal;
            attachments.Add(colorAttachmentDescs[i]);

            colorAttachmentRefs[i].attachment = i;
            colorAttachmentRefs[i].layout = VkImageLayout.ImageLayoutColorAttachmentOptimal;
        }

        var depthAttachmentDesc = new VkAttachmentDescription();
        var depthAttachmentRef = new VkAttachmentReference();

        if (outputDesc.DepthAttachment is OutputAttachmentDescription depthAttachment)
        {
            var depthFormat = depthAttachment.Format;
            bool hasStencil = FormatHelpers.IsStencilFormat(depthFormat);
            depthAttachmentDesc.format = VkFormats.VdToVkPixelFormat(depthAttachment.Format, true);
            depthAttachmentDesc.samples = vkSampleCount;
            depthAttachmentDesc.loadOp = VkAttachmentLoadOp.AttachmentLoadOpDontCare;
            depthAttachmentDesc.storeOp = VkAttachmentStoreOp.AttachmentStoreOpStore;
            depthAttachmentDesc.stencilLoadOp = VkAttachmentLoadOp.AttachmentLoadOpDontCare;
            depthAttachmentDesc.stencilStoreOp = hasStencil ? VkAttachmentStoreOp.AttachmentStoreOpStore : VkAttachmentStoreOp.AttachmentStoreOpDontCare;
            depthAttachmentDesc.initialLayout = VkImageLayout.ImageLayoutUndefined;
            depthAttachmentDesc.finalLayout = VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;

            depthAttachmentRef.attachment = (uint)outputDesc.ColorAttachments.Length;
            depthAttachmentRef.layout = VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;
        }

        var subpass = new VkSubpassDescription
        {
            pipelineBindPoint = VkPipelineBindPoint.PipelineBindPointGraphics,
            colorAttachmentCount = (uint)outputDesc.ColorAttachments.Length,
            pColorAttachments = (VkAttachmentReference*)colorAttachmentRefs.Data
        };
        for (int i = 0; i < colorAttachmentDescs.Count; i++)
        {
            attachments.Add(colorAttachmentDescs[i]);
        }

        if (outputDesc.DepthAttachment != null)
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
        VkRenderPass vkrenderPass;
        var creationResult = Vk.CreateRenderPass(this.gd.Device, &renderPassCi, null, &vkrenderPass);
        renderPass = vkrenderPass;
        CheckResult(creationResult);

        pipelineCi.renderPass = renderPass;
        OpenTK.Graphics.Vulkan.VkPipeline vkdevicePipeline;
        var result = Vk.CreateGraphicsPipelines(this.gd.Device, VkPipelineCache.Zero, 1, &pipelineCi, null, &vkdevicePipeline);
        devicePipeline = vkdevicePipeline;
        CheckResult(result);

        ResourceSetCount = (uint)description.ResourceLayouts.Length;
        DynamicOffsetsCount = 0;
        foreach (ResourceLayout layout in description.ResourceLayouts)
        {
            DynamicOffsetsCount += layout.DynamicBufferCount;
        }
    }

    public VkPipeline(VkGraphicsDevice gd, ref ComputePipelineDescription description)
        : base(ref description)
    {
        this.gd = gd;
        IsComputePipeline = true;
        RefCount = new ResourceRefCount(disposeCore);

        var pipelineCi = new VkComputePipelineCreateInfo();

        // Pipeline Layout
        var resourceLayouts = description.ResourceLayouts;
        var pipelineLayoutCi = new VkPipelineLayoutCreateInfo();
        pipelineLayoutCi.setLayoutCount = (uint)resourceLayouts.Length;
        var dsls = stackalloc VkDescriptorSetLayout[resourceLayouts.Length];
        for (int i = 0; i < resourceLayouts.Length; i++)
        {
            dsls[i] = Util.AssertSubtype<ResourceLayout, VkResourceLayout>(resourceLayouts[i]).DescriptorSetLayout;
        }

        pipelineLayoutCi.pSetLayouts = dsls;
        VkPipelineLayout vkpipelineLayout;
        Vk.CreatePipelineLayout(this.gd.Device, &pipelineLayoutCi, null, &vkpipelineLayout);
        pipelineLayout = vkpipelineLayout;
        pipelineCi.layout = pipelineLayout;

        // Shader Stage

        VkSpecializationInfo specializationInfo;
        var specDescs = description.Specializations;

        if (specDescs != null)
        {
            uint specDataSize = 0;
            foreach (var spec in specDescs)
            {
                specDataSize += VkFormats.GetSpecializationConstantSize(spec.Type);
            }

            byte* fullSpecData = stackalloc byte[(int)specDataSize];
            int specializationCount = specDescs.Length;
            var mapEntries = stackalloc VkSpecializationMapEntry[specializationCount];
            uint specOffset = 0;

            for (int i = 0; i < specializationCount; i++)
            {
                ulong data = specDescs[i].Data;
                byte* srcData = (byte*)&data;
                uint dataSize = VkFormats.GetSpecializationConstantSize(specDescs[i].Type);
                Unsafe.CopyBlock(fullSpecData + specOffset, srcData, dataSize);
                mapEntries[i].constantID = specDescs[i].ID;
                mapEntries[i].offset = specOffset;
                mapEntries[i].size = dataSize;
                specOffset += dataSize;
            }

            specializationInfo.dataSize = specDataSize;
            specializationInfo.pData = fullSpecData;
            specializationInfo.mapEntryCount = (uint)specializationCount;
            specializationInfo.pMapEntries = mapEntries;
        }

        var shader = description.ComputeShader;
        var vkShader = Util.AssertSubtype<Shader, VkShader>(shader);
        var stageCi = new VkPipelineShaderStageCreateInfo
        {
            module = vkShader.ShaderModule,
            stage = VkFormats.VdToVkShaderStages(shader.Stage),
            pName = CommonStrings.Main, // Meh
            pSpecializationInfo = &specializationInfo
        };
        pipelineCi.stage = stageCi;
        OpenTK.Graphics.Vulkan.VkPipeline vkdevicePipeline;
        var result = Vk.CreateComputePipelines(
            this.gd.Device,
            VkPipelineCache.Zero,
            1,
            &pipelineCi,
            null,
            &vkdevicePipeline);
        devicePipeline = vkdevicePipeline;

        CheckResult(result);

        ResourceSetCount = (uint)description.ResourceLayouts.Length;
        DynamicOffsetsCount = 0;
        foreach (ResourceLayout layout in description.ResourceLayouts)
        {
            DynamicOffsetsCount += layout.DynamicBufferCount;
        }
    }

    #region Disposal

    public override void Dispose()
    {
        RefCount.Decrement();
    }

    #endregion

    private void disposeCore()
    {
        if (!destroyed)
        {
            destroyed = true;
            Vk.DestroyPipelineLayout(gd.Device, pipelineLayout, null);
            Vk.DestroyPipeline(gd.Device, devicePipeline, null);
            if (!IsComputePipeline)
            {
                Vk.DestroyRenderPass(gd.Device, renderPass, null);
            }
        }
    }
}
