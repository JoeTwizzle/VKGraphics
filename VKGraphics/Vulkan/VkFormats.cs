namespace VKGraphics.Vulkan;

internal static partial class VkFormats
{
    internal static VkSamplerAddressMode VdToVkSamplerAddressMode(SamplerAddressMode mode)
    {
        return mode switch
        {
            SamplerAddressMode.Wrap => VkSamplerAddressMode.SamplerAddressModeRepeat,
            SamplerAddressMode.Mirror => VkSamplerAddressMode.SamplerAddressModeMirroredRepeat,
            SamplerAddressMode.Clamp => VkSamplerAddressMode.SamplerAddressModeClampToEdge,
            SamplerAddressMode.Border => VkSamplerAddressMode.SamplerAddressModeClampToBorder,
            _ => throw Illegal.Value<SamplerAddressMode>(),
        };
    }

    internal static void GetFilterParams(
        SamplerFilter filter,
        out VkFilter minFilter,
        out VkFilter magFilter,
        out VkSamplerMipmapMode mipmapMode)
    {
        switch (filter)
        {
            case SamplerFilter.Anisotropic:
                minFilter = VkFilter.FilterLinear;
                magFilter = VkFilter.FilterLinear;
                mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeLinear;
                break;

            case SamplerFilter.MinPointMagPointMipPoint:
                minFilter = VkFilter.FilterNearest;
                magFilter = VkFilter.FilterNearest;
                mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeNearest;
                break;

            case SamplerFilter.MinPointMagPointMipLinear:
                minFilter = VkFilter.FilterNearest;
                magFilter = VkFilter.FilterNearest;
                mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeLinear;
                break;

            case SamplerFilter.MinPointMagLinearMipPoint:
                minFilter = VkFilter.FilterNearest;
                magFilter = VkFilter.FilterLinear;
                mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeNearest;
                break;

            case SamplerFilter.MinPointMagLinearMipLinear:
                minFilter = VkFilter.FilterNearest;
                magFilter = VkFilter.FilterLinear;
                mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeLinear;
                break;

            case SamplerFilter.MinLinearMagPointMipPoint:
                minFilter = VkFilter.FilterLinear;
                magFilter = VkFilter.FilterNearest;
                mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeNearest;
                break;

            case SamplerFilter.MinLinearMagPointMipLinear:
                minFilter = VkFilter.FilterLinear;
                magFilter = VkFilter.FilterNearest;
                mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeLinear;
                break;

            case SamplerFilter.MinLinearMagLinearMipPoint:
                minFilter = VkFilter.FilterLinear;
                magFilter = VkFilter.FilterLinear;
                mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeNearest;
                break;

            case SamplerFilter.MinLinearMagLinearMipLinear:
                minFilter = VkFilter.FilterLinear;
                magFilter = VkFilter.FilterLinear;
                mipmapMode = VkSamplerMipmapMode.SamplerMipmapModeLinear;
                break;

            default:
                throw Illegal.Value<SamplerFilter>();
        }
    }

    internal static VkImageUsageFlagBits VdToVkTextureUsage(TextureUsage vdUsage)
    {
        var vkUsage = VkImageUsageFlagBits.ImageUsageTransferDstBit | VkImageUsageFlagBits.ImageUsageTransferSrcBit;
        bool isDepthStencil = (vdUsage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil;

        if ((vdUsage & TextureUsage.Sampled) == TextureUsage.Sampled)
        {
            vkUsage |= VkImageUsageFlagBits.ImageUsageSampledBit;
        }

        if (isDepthStencil)
        {
            vkUsage |= VkImageUsageFlagBits.ImageUsageDepthStencilAttachmentBit;
        }

        if ((vdUsage & TextureUsage.RenderTarget) == TextureUsage.RenderTarget)
        {
            vkUsage |= VkImageUsageFlagBits.ImageUsageColorAttachmentBit;
        }

        if ((vdUsage & TextureUsage.Storage) == TextureUsage.Storage)
        {
            vkUsage |= VkImageUsageFlagBits.ImageUsageStorageBit;
        }

        return vkUsage;
    }

    internal static VkImageType VdToVkTextureType(TextureType type)
    {
        switch (type)
        {
            case TextureType.Texture1D:
                return VkImageType.ImageType1d;

            case TextureType.Texture2D:
                return VkImageType.ImageType2d;

            case TextureType.Texture3D:
                return VkImageType.ImageType3d;

            default:
                throw Illegal.Value<TextureType>();
        }
    }

    internal static VkDescriptorType VdToVkDescriptorType(ResourceKind kind, ResourceLayoutElementOptions options)
    {
        bool dynamicBinding = (options & ResourceLayoutElementOptions.DynamicBinding) != 0;

        return kind switch
        {
            ResourceKind.UniformBuffer => dynamicBinding ? VkDescriptorType.DescriptorTypeUniformBufferDynamic : VkDescriptorType.DescriptorTypeUniformBuffer,
            ResourceKind.StructuredBufferReadWrite or ResourceKind.StructuredBufferReadOnly => dynamicBinding ? VkDescriptorType.DescriptorTypeStorageBufferDynamic : VkDescriptorType.DescriptorTypeStorageBuffer,
            ResourceKind.TextureReadOnly => VkDescriptorType.DescriptorTypeSampledImage,
            ResourceKind.TextureReadWrite => VkDescriptorType.DescriptorTypeStorageImage,
            ResourceKind.Sampler => VkDescriptorType.DescriptorTypeSampler,
            _ => throw Illegal.Value<ResourceKind>(),
        };
    }

    internal static VkSampleCountFlagBits VdToVkSampleCount(TextureSampleCount sampleCount)
    {
        return sampleCount switch
        {
            TextureSampleCount.Count1 => VkSampleCountFlagBits.SampleCount1Bit,
            TextureSampleCount.Count2 => VkSampleCountFlagBits.SampleCount2Bit,
            TextureSampleCount.Count4 => VkSampleCountFlagBits.SampleCount4Bit,
            TextureSampleCount.Count8 => VkSampleCountFlagBits.SampleCount8Bit,
            TextureSampleCount.Count16 => VkSampleCountFlagBits.SampleCount16Bit,
            TextureSampleCount.Count32 => VkSampleCountFlagBits.SampleCount32Bit,
            _ => throw Illegal.Value<TextureSampleCount>(),
        };
    }

    internal static VkStencilOp VdToVkStencilOp(StencilOperation op)
    {
        return op switch
        {
            StencilOperation.Keep => VkStencilOp.StencilOpKeep,
            StencilOperation.Zero => VkStencilOp.StencilOpZero,
            StencilOperation.Replace => VkStencilOp.StencilOpReplace,
            StencilOperation.IncrementAndClamp => VkStencilOp.StencilOpIncrementAndClamp,
            StencilOperation.DecrementAndClamp => VkStencilOp.StencilOpDecrementAndClamp,
            StencilOperation.Invert => VkStencilOp.StencilOpInvert,
            StencilOperation.IncrementAndWrap => VkStencilOp.StencilOpIncrementAndWrap,
            StencilOperation.DecrementAndWrap => VkStencilOp.StencilOpDecrementAndWrap,
            _ => throw Illegal.Value<StencilOperation>(),
        };
    }

    internal static VkPolygonMode VdToVkPolygonMode(PolygonFillMode fillMode)
    {
        return fillMode switch
        {
            PolygonFillMode.Solid => VkPolygonMode.PolygonModeFill,
            PolygonFillMode.Wireframe => VkPolygonMode.PolygonModeLine,
            _ => throw Illegal.Value<PolygonFillMode>(),
        };
    }

    internal static VkCullModeFlagBits VdToVkCullMode(FaceCullMode cullMode)
    {
        return cullMode switch
        {
            FaceCullMode.Back => VkCullModeFlagBits.CullModeBackBit,
            FaceCullMode.Front => VkCullModeFlagBits.CullModeFrontBit,
            FaceCullMode.None => VkCullModeFlagBits.CullModeNone,
            _ => throw Illegal.Value<FaceCullMode>(),
        };
    }

    internal static VkBlendOp VdToVkBlendOp(BlendFunction func)
    {
        return func switch
        {
            BlendFunction.Add => VkBlendOp.BlendOpAdd,
            BlendFunction.Subtract => VkBlendOp.BlendOpSubtract,
            BlendFunction.ReverseSubtract => VkBlendOp.BlendOpReverseSubtract,
            BlendFunction.Minimum => VkBlendOp.BlendOpMin,
            BlendFunction.Maximum => VkBlendOp.BlendOpMax,
            _ => throw Illegal.Value<BlendFunction>(),
        };
    }

    internal static VkColorComponentFlagBits VdToVkColorWriteMask(ColorWriteMask mask)
    {
        VkColorComponentFlagBits flags = 0;

        if ((mask & ColorWriteMask.Red) == ColorWriteMask.Red)
        {
            flags |= VkColorComponentFlagBits.ColorComponentRBit;
        }

        if ((mask & ColorWriteMask.Green) == ColorWriteMask.Green)
        {
            flags |= VkColorComponentFlagBits.ColorComponentGBit;
        }

        if ((mask & ColorWriteMask.Blue) == ColorWriteMask.Blue)
        {
            flags |= VkColorComponentFlagBits.ColorComponentBBit;
        }

        if ((mask & ColorWriteMask.Alpha) == ColorWriteMask.Alpha)
        {
            flags |= VkColorComponentFlagBits.ColorComponentABit;
        }

        return flags;
    }

    internal static VkPrimitiveTopology VdToVkPrimitiveTopology(PrimitiveTopology topology)
    {
        return topology switch
        {
            PrimitiveTopology.TriangleList => VkPrimitiveTopology.PrimitiveTopologyTriangleList,
            PrimitiveTopology.TriangleStrip => VkPrimitiveTopology.PrimitiveTopologyTriangleStrip,
            PrimitiveTopology.LineList => VkPrimitiveTopology.PrimitiveTopologyLineList,
            PrimitiveTopology.LineStrip => VkPrimitiveTopology.PrimitiveTopologyLineStrip,
            PrimitiveTopology.PointList => VkPrimitiveTopology.PrimitiveTopologyPointList,
            _ => throw Illegal.Value<PrimitiveTopology>(),
        };
    }

    internal static uint GetSpecializationConstantSize(ShaderConstantType type)
    {
        return type switch
        {
            ShaderConstantType.Bool => 4,
            ShaderConstantType.UInt16 => 2,
            ShaderConstantType.Int16 => 2,
            ShaderConstantType.UInt32 => 4,
            ShaderConstantType.Int32 => 4,
            ShaderConstantType.UInt64 => 8,
            ShaderConstantType.Int64 => 8,
            ShaderConstantType.Float => 4,
            ShaderConstantType.Double => 8,
            _ => throw Illegal.Value<ShaderConstantType>(),
        };
    }

    internal static VkBlendFactor VdToVkBlendFactor(BlendFactor factor)
    {
        return factor switch
        {
            BlendFactor.Zero => VkBlendFactor.BlendFactorZero,
            BlendFactor.One => VkBlendFactor.BlendFactorOne,
            BlendFactor.SourceAlpha => VkBlendFactor.BlendFactorSrcAlpha,
            BlendFactor.InverseSourceAlpha => VkBlendFactor.BlendFactorOneMinusSrcAlpha,
            BlendFactor.DestinationAlpha => VkBlendFactor.BlendFactorDstAlpha,
            BlendFactor.InverseDestinationAlpha => VkBlendFactor.BlendFactorOneMinusDstAlpha,
            BlendFactor.SourceColor => VkBlendFactor.BlendFactorSrcColor,
            BlendFactor.InverseSourceColor => VkBlendFactor.BlendFactorOneMinusSrcColor,
            BlendFactor.DestinationColor => VkBlendFactor.BlendFactorDstColor,
            BlendFactor.InverseDestinationColor => VkBlendFactor.BlendFactorOneMinusDstColor,
            BlendFactor.BlendFactor => VkBlendFactor.BlendFactorConstantColor,
            BlendFactor.InverseBlendFactor => VkBlendFactor.BlendFactorOneMinusConstantColor,
            _ => throw Illegal.Value<BlendFactor>(),
        };
    }

    internal static VkFormat VdToVkVertexElementFormat(VertexElementFormat format)
    {
        return format switch
        {
            VertexElementFormat.Float1 => VkFormat.FormatR32Sfloat,
            VertexElementFormat.Float2 => VkFormat.FormatR32g32Sfloat,
            VertexElementFormat.Float3 => VkFormat.FormatR32g32b32Sfloat,
            VertexElementFormat.Float4 => VkFormat.FormatR32g32b32a32Sfloat,
            VertexElementFormat.Byte2Norm => VkFormat.FormatR8g8Unorm,
            VertexElementFormat.Byte2 => VkFormat.FormatR8g8Uint,
            VertexElementFormat.Byte4Norm => VkFormat.FormatR8g8b8a8Unorm,
            VertexElementFormat.Byte4 => VkFormat.FormatR8g8b8a8Uint,
            VertexElementFormat.SByte2Norm => VkFormat.FormatR8g8Snorm,
            VertexElementFormat.SByte2 => VkFormat.FormatR8g8Sint,
            VertexElementFormat.SByte4Norm => VkFormat.FormatR8g8b8a8Snorm,
            VertexElementFormat.SByte4 => VkFormat.FormatR8g8b8a8Sint,
            VertexElementFormat.UShort2Norm => VkFormat.FormatR16g16Unorm,
            VertexElementFormat.UShort2 => VkFormat.FormatR16g16Uint,
            VertexElementFormat.UShort4Norm => VkFormat.FormatR16g16b16a16Unorm,
            VertexElementFormat.UShort4 => VkFormat.FormatR16g16b16a16Uint,
            VertexElementFormat.Short2Norm => VkFormat.FormatR16g16Snorm,
            VertexElementFormat.Short2 => VkFormat.FormatR16g16Sint,
            VertexElementFormat.Short4Norm => VkFormat.FormatR16g16b16a16Snorm,
            VertexElementFormat.Short4 => VkFormat.FormatR16g16b16a16Sint,
            VertexElementFormat.UInt1 => VkFormat.FormatR32Uint,
            VertexElementFormat.UInt2 => VkFormat.FormatR32g32Uint,
            VertexElementFormat.UInt3 => VkFormat.FormatR32g32b32Uint,
            VertexElementFormat.UInt4 => VkFormat.FormatR32g32b32a32Uint,
            VertexElementFormat.Int1 => VkFormat.FormatR32Sint,
            VertexElementFormat.Int2 => VkFormat.FormatR32g32Sint,
            VertexElementFormat.Int3 => VkFormat.FormatR32g32b32Sint,
            VertexElementFormat.Int4 => VkFormat.FormatR32g32b32a32Sint,
            VertexElementFormat.Half1 => VkFormat.FormatR16Sfloat,
            VertexElementFormat.Half2 => VkFormat.FormatR16g16Sfloat,
            VertexElementFormat.Half4 => VkFormat.FormatR16g16b16a16Sfloat,
            _ => throw Illegal.Value<VertexElementFormat>(),
        };
    }

    internal static VkShaderStageFlagBits VdToVkShaderStages(ShaderStages stage)
    {
        VkShaderStageFlagBits ret = 0;

        if ((stage & ShaderStages.Vertex) == ShaderStages.Vertex)
        {
            ret |= VkShaderStageFlagBits.ShaderStageVertexBit;
        }

        if ((stage & ShaderStages.Geometry) == ShaderStages.Geometry)
        {
            ret |= VkShaderStageFlagBits.ShaderStageGeometryBit;
        }

        if ((stage & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
        {
            ret |= VkShaderStageFlagBits.ShaderStageTessellationControlBit;
        }

        if ((stage & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
        {
            ret |= VkShaderStageFlagBits.ShaderStageTessellationEvaluationBit;
        }

        if ((stage & ShaderStages.Fragment) == ShaderStages.Fragment)
        {
            ret |= VkShaderStageFlagBits.ShaderStageFragmentBit;
        }

        if ((stage & ShaderStages.Compute) == ShaderStages.Compute)
        {
            ret |= VkShaderStageFlagBits.ShaderStageComputeBit;
        }

        return ret;
    }

    internal static VkBorderColor VdToVkSamplerBorderColor(SamplerBorderColor borderColor)
    {
        return borderColor switch
        {
            SamplerBorderColor.TransparentBlack => VkBorderColor.BorderColorFloatTransparentBlack,
            SamplerBorderColor.OpaqueBlack => VkBorderColor.BorderColorFloatOpaqueBlack,
            SamplerBorderColor.OpaqueWhite => VkBorderColor.BorderColorFloatOpaqueWhite,
            _ => throw Illegal.Value<SamplerBorderColor>(),
        };
    }

    internal static VkIndexType VdToVkIndexFormat(IndexFormat format)
    {
        switch (format)
        {
            case IndexFormat.UInt16:
                return VkIndexType.IndexTypeUint16;

            case IndexFormat.UInt32:
                return VkIndexType.IndexTypeUint32;

            default:
                throw Illegal.Value<IndexFormat>();
        }
    }

    internal static VkCompareOp VdToVkCompareOp(ComparisonKind comparisonKind)
    {
        switch (comparisonKind)
        {
            case ComparisonKind.Never:
                return VkCompareOp.CompareOpNever;

            case ComparisonKind.Less:
                return VkCompareOp.CompareOpLess;

            case ComparisonKind.Equal:
                return VkCompareOp.CompareOpEqual;

            case ComparisonKind.LessEqual:
                return VkCompareOp.CompareOpLessOrEqual;

            case ComparisonKind.Greater:
                return VkCompareOp.CompareOpGreater;

            case ComparisonKind.NotEqual:
                return VkCompareOp.CompareOpNotEqual;

            case ComparisonKind.GreaterEqual:
                return VkCompareOp.CompareOpGreaterOrEqual;

            case ComparisonKind.Always:
                return VkCompareOp.CompareOpAlways;

            default:
                throw Illegal.Value<ComparisonKind>();
        }
    }

    internal static PixelFormat VkToVdPixelFormat(VkFormat vkFormat)
    {
        switch (vkFormat)
        {
            case VkFormat.FormatR8Unorm:
                return PixelFormat.R8UNorm;

            case VkFormat.FormatR8Snorm:
                return PixelFormat.R8SNorm;

            case VkFormat.FormatR8Uint:
                return PixelFormat.R8UInt;

            case VkFormat.FormatR8Sint:
                return PixelFormat.R8SInt;

            case VkFormat.FormatR16Unorm:
                return PixelFormat.R16UNorm;

            case VkFormat.FormatR16Snorm:
                return PixelFormat.R16SNorm;

            case VkFormat.FormatR16Uint:
                return PixelFormat.R16UInt;

            case VkFormat.FormatR16Sint:
                return PixelFormat.R16SInt;

            case VkFormat.FormatR16Sfloat:
                return PixelFormat.R16Float;

            case VkFormat.FormatR32Uint:
                return PixelFormat.R32UInt;

            case VkFormat.FormatR32Sint:
                return PixelFormat.R32SInt;

            case VkFormat.FormatR32Sfloat:
            case VkFormat.FormatD32Sfloat:
                return PixelFormat.R32Float;

            case VkFormat.FormatR8g8Unorm:
                return PixelFormat.R8G8UNorm;

            case VkFormat.FormatR8g8Snorm:
                return PixelFormat.R8G8SNorm;

            case VkFormat.FormatR8g8Uint:
                return PixelFormat.R8G8UInt;

            case VkFormat.FormatR8g8Sint:
                return PixelFormat.R8G8SInt;

            case VkFormat.FormatR16g16Unorm:
                return PixelFormat.R16G16UNorm;

            case VkFormat.FormatR16g16Snorm:
                return PixelFormat.R16G16SNorm;

            case VkFormat.FormatR16g16Uint:
                return PixelFormat.R16G16UInt;

            case VkFormat.FormatR16g16Sint:
                return PixelFormat.R16G16SInt;

            case VkFormat.FormatR16g16Sfloat:
                return PixelFormat.R16G16Float;

            case VkFormat.FormatR32g32Uint:
                return PixelFormat.R32G32UInt;

            case VkFormat.FormatR32g32Sint:
                return PixelFormat.R32G32SInt;

            case VkFormat.FormatR32g32Sfloat:
                return PixelFormat.R32G32Float;

            case VkFormat.FormatR8g8b8a8Unorm:
                return PixelFormat.R8G8B8A8UNorm;

            case VkFormat.FormatR8g8b8a8Srgb:
                return PixelFormat.R8G8B8A8UNormSRgb;

            case VkFormat.FormatB8g8r8a8Unorm:
                return PixelFormat.B8G8R8A8UNorm;

            case VkFormat.FormatB8g8r8a8Srgb:
                return PixelFormat.B8G8R8A8UNormSRgb;

            case VkFormat.FormatR8g8b8a8Snorm:
                return PixelFormat.R8G8B8A8SNorm;

            case VkFormat.FormatR8g8b8a8Uint:
                return PixelFormat.R8G8B8A8UInt;

            case VkFormat.FormatR8g8b8a8Sint:
                return PixelFormat.R8G8B8A8SInt;

            case VkFormat.FormatR16g16b16a16Unorm:
                return PixelFormat.R16G16B16A16UNorm;

            case VkFormat.FormatR16g16b16a16Snorm:
                return PixelFormat.R16G16B16A16SNorm;

            case VkFormat.FormatR16g16b16a16Uint:
                return PixelFormat.R16G16B16A16UInt;

            case VkFormat.FormatR16g16b16a16Sint:
                return PixelFormat.R16G16B16A16SInt;

            case VkFormat.FormatR16g16b16a16Sfloat:
                return PixelFormat.R16G16B16A16Float;

            case VkFormat.FormatR32g32b32a32Uint:
                return PixelFormat.R32G32B32A32UInt;

            case VkFormat.FormatR32g32b32a32Sint:
                return PixelFormat.R32G32B32A32SInt;

            case VkFormat.FormatR32g32b32a32Sfloat:
                return PixelFormat.R32G32B32A32Float;

            case VkFormat.FormatBc1RgbUnormBlock:
                return PixelFormat.Bc1RgbUNorm;

            case VkFormat.FormatBc1RgbSrgbBlock:
                return PixelFormat.Bc1RgbUNormSRgb;

            case VkFormat.FormatBc1RgbaUnormBlock:
                return PixelFormat.Bc1RgbaUNorm;

            case VkFormat.FormatBc1RgbaSrgbBlock:
                return PixelFormat.Bc1RgbaUNormSRgb;

            case VkFormat.FormatBc2UnormBlock:
                return PixelFormat.Bc2UNorm;

            case VkFormat.FormatBc2SrgbBlock:
                return PixelFormat.Bc2UNormSRgb;

            case VkFormat.FormatBc3UnormBlock:
                return PixelFormat.Bc3UNorm;

            case VkFormat.FormatBc3SrgbBlock:
                return PixelFormat.Bc3UNormSRgb;

            case VkFormat.FormatBc4UnormBlock:
                return PixelFormat.Bc4UNorm;

            case VkFormat.FormatBc4SnormBlock:
                return PixelFormat.Bc4SNorm;

            case VkFormat.FormatBc5UnormBlock:
                return PixelFormat.Bc5UNorm;

            case VkFormat.FormatBc5SnormBlock:
                return PixelFormat.Bc5SNorm;

            case VkFormat.FormatBc7UnormBlock:
                return PixelFormat.Bc7UNorm;

            case VkFormat.FormatBc7SrgbBlock:
                return PixelFormat.Bc7UNormSRgb;

            case VkFormat.FormatA2b10g10r10UnormPack32:
                return PixelFormat.R10G10B10A2UNorm;

            case VkFormat.FormatA2b10g10r10UintPack32:
                return PixelFormat.R10G10B10A2UInt;

            case VkFormat.FormatB10g11r11UfloatPack32:
                return PixelFormat.R11G11B10Float;

            default:
                throw Illegal.Value<VkFormat>();
        }
    }
}
