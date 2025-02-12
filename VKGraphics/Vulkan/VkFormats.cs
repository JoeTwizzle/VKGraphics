using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.Vulkan;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

using static VkBlendFactor;
using static VkBlendOp;
using static VkBorderColor;
using static VkCompareOp;
using static VkCullModeFlagBits;
using static VkDescriptorType;
using static VkFilter;
using static VkFormat;
using static VkImageType;
using static VkIndexType;
using static VkPolygonMode;
using static VkPrimitiveTopology;
using static VkSampleCountFlagBits;
using static VkSamplerAddressMode;
using static VkSamplerMipmapMode;
using static VkShaderStageFlagBits;
using static VkStencilOp;

internal static partial class VkFormats
{
    internal static VkSamplerAddressMode VdToVkSamplerAddressMode(SamplerAddressMode mode)
    {
        return mode switch
        {
            SamplerAddressMode.Wrap => SamplerAddressModeRepeat,
            SamplerAddressMode.Mirror => SamplerAddressModeMirroredRepeat,
            SamplerAddressMode.Clamp => SamplerAddressModeClampToEdge,
            SamplerAddressMode.Border => SamplerAddressModeClampToBorder,
            _ => Illegal.Handle<SamplerAddressMode, VkSamplerAddressMode>(),
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
                minFilter = FilterLinear;
                magFilter = FilterLinear;
                mipmapMode = SamplerMipmapModeLinear;
                break;
            case SamplerFilter.MinPoint_MagPoint_MipPoint:
                minFilter = FilterNearest;
                magFilter = FilterNearest;
                mipmapMode = SamplerMipmapModeNearest;
                break;
            case SamplerFilter.MinPoint_MagPoint_MipLinear:
                minFilter = FilterNearest;
                magFilter = FilterNearest;
                mipmapMode = SamplerMipmapModeLinear;
                break;
            case SamplerFilter.MinPoint_MagLinear_MipPoint:
                minFilter = FilterNearest;
                magFilter = FilterLinear;
                mipmapMode = SamplerMipmapModeNearest;
                break;
            case SamplerFilter.MinPoint_MagLinear_MipLinear:
                minFilter = FilterNearest;
                magFilter = FilterLinear;
                mipmapMode = SamplerMipmapModeLinear;
                break;
            case SamplerFilter.MinLinear_MagPoint_MipPoint:
                minFilter = FilterLinear;
                magFilter = FilterNearest;
                mipmapMode = SamplerMipmapModeNearest;
                break;
            case SamplerFilter.MinLinear_MagPoint_MipLinear:
                minFilter = FilterLinear;
                magFilter = FilterNearest;
                mipmapMode = SamplerMipmapModeLinear;
                break;
            case SamplerFilter.MinLinear_MagLinear_MipPoint:
                minFilter = FilterLinear;
                magFilter = FilterLinear;
                mipmapMode = SamplerMipmapModeNearest;
                break;
            case SamplerFilter.MinLinear_MagLinear_MipLinear:
                minFilter = FilterLinear;
                magFilter = FilterLinear;
                mipmapMode = SamplerMipmapModeLinear;
                break;
            default:
                Unsafe.SkipInit(out minFilter);
                Unsafe.SkipInit(out magFilter);
                Unsafe.SkipInit(out mipmapMode);
                Illegal.Handle<SamplerFilter>();
                break;
        }
    }

    internal static VkImageUsageFlagBits VdToVkTextureUsage(TextureUsage vdUsage)
    {
        VkImageUsageFlagBits vkUsage = VkImageUsageFlagBits.ImageUsageTransferDstBit | VkImageUsageFlagBits.ImageUsageTransferSrcBit;
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
        return type switch
        {
            TextureType.Texture1D => ImageType1d,
            TextureType.Texture2D => ImageType2d,
            TextureType.Texture3D => ImageType3d,
            _ => Illegal.Handle<TextureType, VkImageType>(),
        };
    }

    [SuppressMessage("Style", "IDE0066:Convert switch statement to expression", Justification = "<Pending>")]
    internal static VkDescriptorType VdToVkDescriptorType(ResourceKind kind, ResourceLayoutElementOptions options)
    {
        bool dynamicBinding = (options & ResourceLayoutElementOptions.DynamicBinding) != 0;
        switch (kind)
        {
            case ResourceKind.UniformBuffer:
                return dynamicBinding ? DescriptorTypeUniformBufferDynamic : DescriptorTypeUniformBuffer;
            case ResourceKind.StructuredBufferReadWrite:
            case ResourceKind.StructuredBufferReadOnly:
                return dynamicBinding ? DescriptorTypeStorageBufferDynamic : DescriptorTypeStorageBuffer;
            case ResourceKind.TextureReadOnly:
                return DescriptorTypeSampledImage;
            case ResourceKind.TextureReadWrite:
                return DescriptorTypeStorageImage;
            case ResourceKind.Sampler:
                return DescriptorTypeSampler;
            default:
                return Illegal.Handle<ResourceKind, VkDescriptorType>();
        }
    }

    internal static VkAccessFlagBits VdToVkAccess(ResourceKind kind)
    {
        return kind switch
        {
            ResourceKind.UniformBuffer => VkAccessFlagBits.AccessUniformReadBit,
            ResourceKind.StructuredBufferReadOnly => VkAccessFlagBits.AccessShaderReadBit,
            ResourceKind.StructuredBufferReadWrite => VkAccessFlagBits.AccessShaderReadBit | VkAccessFlagBits.AccessShaderWriteBit,
            ResourceKind.TextureReadOnly => VkAccessFlagBits.AccessShaderReadBit,
            ResourceKind.TextureReadWrite => VkAccessFlagBits.AccessShaderReadBit | VkAccessFlagBits.AccessShaderWriteBit,
            ResourceKind.Sampler => 0,
            _ => Illegal.Handle<ResourceKind, VkAccessFlagBits>()
        };
    }

    internal static VkSampleCountFlagBits VdToVkSampleCount(TextureSampleCount sampleCount)
    {
        return sampleCount switch
        {
            TextureSampleCount.Count1 => SampleCount1Bit,
            TextureSampleCount.Count2 => SampleCount2Bit,
            TextureSampleCount.Count4 => SampleCount4Bit,
            TextureSampleCount.Count8 => SampleCount8Bit,
            TextureSampleCount.Count16 => SampleCount16Bit,
            TextureSampleCount.Count32 => SampleCount32Bit,
            TextureSampleCount.Count64 => SampleCount64Bit,
            _ => Illegal.Handle<TextureSampleCount, VkSampleCountFlagBits>(),
        };
    }

    internal static VkStencilOp VdToVkStencilOp(StencilOperation op)
    {
        return op switch
        {
            StencilOperation.Keep => StencilOpKeep,
            StencilOperation.Zero => StencilOpZero,
            StencilOperation.Replace => StencilOpReplace,
            StencilOperation.IncrementAndClamp => StencilOpIncrementAndClamp,
            StencilOperation.DecrementAndClamp => StencilOpDecrementAndClamp,
            StencilOperation.Invert => StencilOpInvert,
            StencilOperation.IncrementAndWrap => StencilOpIncrementAndWrap,
            StencilOperation.DecrementAndWrap => StencilOpDecrementAndWrap,
            _ => Illegal.Handle<StencilOperation, VkStencilOp>(),
        };
    }

    internal static VkPolygonMode VdToVkPolygonMode(PolygonFillMode fillMode)
    {
        return fillMode switch
        {
            PolygonFillMode.Solid => PolygonModeFill,
            PolygonFillMode.Wireframe => PolygonModeLine,
            _ => Illegal.Handle<PolygonFillMode, VkPolygonMode>(),
        };
    }

    internal static VkCullModeFlagBits VdToVkCullMode(FaceCullMode cullMode)
    {
        return cullMode switch
        {
            FaceCullMode.Back => CullModeBackBit,
            FaceCullMode.Front => CullModeFrontBit,
            FaceCullMode.None => CullModeNone,
            _ => Illegal.Handle<FaceCullMode, VkCullModeFlagBits>(),
        };
    }

    internal static VkBlendOp VdToVkBlendOp(BlendFunction func)
    {
        return func switch
        {
            BlendFunction.Add => BlendOpAdd,
            BlendFunction.Subtract => BlendOpSubtract,
            BlendFunction.ReverseSubtract => BlendOpReverseSubtract,
            BlendFunction.Minimum => BlendOpMin,
            BlendFunction.Maximum => BlendOpMax,
            _ => Illegal.Handle<BlendFunction, VkBlendOp>(),
        };
    }

    internal static VkColorComponentFlagBits VdToVkColorWriteMask(ColorWriteMask mask)
    {
        VkColorComponentFlagBits flags = default;

        if ((mask & ColorWriteMask.Red) == ColorWriteMask.Red)
            flags |= VkColorComponentFlagBits.ColorComponentRBit;
        if ((mask & ColorWriteMask.Green) == ColorWriteMask.Green)
            flags |= VkColorComponentFlagBits.ColorComponentGBit;
        if ((mask & ColorWriteMask.Blue) == ColorWriteMask.Blue)
            flags |= VkColorComponentFlagBits.ColorComponentBBit;
        if ((mask & ColorWriteMask.Alpha) == ColorWriteMask.Alpha)
            flags |= VkColorComponentFlagBits.ColorComponentABit;

        return flags;
    }

    internal static VkPrimitiveTopology VdToVkPrimitiveTopology(PrimitiveTopology topology)
    {
        return topology switch
        {
            PrimitiveTopology.TriangleList => PrimitiveTopologyTriangleList,
            PrimitiveTopology.TriangleStrip => PrimitiveTopologyTriangleStrip,
            PrimitiveTopology.LineList => PrimitiveTopologyLineList,
            PrimitiveTopology.LineStrip => PrimitiveTopologyLineStrip,
            PrimitiveTopology.PointList => PrimitiveTopologyPointList,
            _ => Illegal.Handle<PrimitiveTopology, VkPrimitiveTopology>(),
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
            _ => Illegal.Handle<ShaderConstantType, uint>(),
        };
    }

    internal static VkBlendFactor VdToVkBlendFactor(BlendFactor factor)
    {
        return factor switch
        {
            BlendFactor.Zero => BlendFactorZero,
            BlendFactor.One => BlendFactorOne,
            BlendFactor.SourceAlpha => BlendFactorSrcAlpha,
            BlendFactor.InverseSourceAlpha => BlendFactorOneMinusSrcAlpha,
            BlendFactor.DestinationAlpha => BlendFactorDstAlpha,
            BlendFactor.InverseDestinationAlpha => BlendFactorOneMinusDstAlpha,
            BlendFactor.SourceColor => BlendFactorSrcColor,
            BlendFactor.InverseSourceColor => BlendFactorOneMinusSrcColor,
            BlendFactor.DestinationColor => BlendFactorDstColor,
            BlendFactor.InverseDestinationColor => BlendFactorOneMinusDstColor,
            BlendFactor.BlendFactor => BlendFactorConstantColor,
            BlendFactor.InverseBlendFactor => BlendFactorOneMinusConstantColor,
            _ => Illegal.Handle<BlendFactor, VkBlendFactor>(),
        };
    }

    internal static VkFormat VdToVkVertexElementFormat(VertexElementFormat format)
    {
        return format switch
        {
            VertexElementFormat.Float1 => FormatR32Sfloat,
            VertexElementFormat.Float2 => FormatR32g32Sfloat,
            VertexElementFormat.Float3 => FormatR32g32b32Sfloat,
            VertexElementFormat.Float4 => FormatR32g32b32a32Sfloat,
            VertexElementFormat.Byte2Norm => FormatR8g8Unorm,
            VertexElementFormat.Byte2 => FormatR8g8Uint,
            VertexElementFormat.Byte4Norm => FormatR8g8b8a8Unorm,
            VertexElementFormat.Byte4 => FormatR8g8b8a8Uint,
            VertexElementFormat.SByte2Norm => FormatR8g8Snorm,
            VertexElementFormat.SByte2 => FormatR8g8Sint,
            VertexElementFormat.SByte4Norm => FormatR8g8b8a8Snorm,
            VertexElementFormat.SByte4 => FormatR8g8b8a8Sint,
            VertexElementFormat.UShort2Norm => FormatR16g16Unorm,
            VertexElementFormat.UShort2 => FormatR16g16Uint,
            VertexElementFormat.UShort4Norm => FormatR16g16b16a16Unorm,
            VertexElementFormat.UShort4 => FormatR16g16b16a16Uint,
            VertexElementFormat.Short2Norm => FormatR16g16Snorm,
            VertexElementFormat.Short2 => FormatR16g16Sint,
            VertexElementFormat.Short4Norm => FormatR16g16b16a16Snorm,
            VertexElementFormat.Short4 => FormatR16g16b16a16Sint,
            VertexElementFormat.UInt1 => FormatR32Uint,
            VertexElementFormat.UInt2 => FormatR32g32Uint,
            VertexElementFormat.UInt3 => FormatR32g32b32Uint,
            VertexElementFormat.UInt4 => FormatR32g32b32a32Uint,
            VertexElementFormat.Int1 => FormatR32Sint,
            VertexElementFormat.Int2 => FormatR32g32Sint,
            VertexElementFormat.Int3 => FormatR32g32b32Sint,
            VertexElementFormat.Int4 => FormatR32g32b32a32Sint,
            VertexElementFormat.Half1 => FormatR16Sfloat,
            VertexElementFormat.Half2 => FormatR16g16Sfloat,
            VertexElementFormat.Half4 => FormatR16g16b16a16Sfloat,
            _ => Illegal.Handle<VertexElementFormat, VkFormat>(),
        };
    }

    internal static VkShaderStageFlagBits VdToVkShaderStages(ShaderStages stage)
    {
        VkShaderStageFlagBits ret = 0;

        if ((stage & ShaderStages.Vertex) == ShaderStages.Vertex)
            ret |= ShaderStageVertexBit;

        if ((stage & ShaderStages.Geometry) == ShaderStages.Geometry)
            ret |= ShaderStageGeometryBit;

        if ((stage & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
            ret |= ShaderStageTessellationControlBit;

        if ((stage & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
            ret |= ShaderStageTessellationEvaluationBit;

        if ((stage & ShaderStages.Fragment) == ShaderStages.Fragment)
            ret |= ShaderStageFragmentBit;

        if ((stage & ShaderStages.Compute) == ShaderStages.Compute)
            ret |= ShaderStageComputeBit;

        return ret;
    }

    internal static VkPipelineStageFlagBits ShaderStagesToPipelineStages(VkShaderStageFlagBits flags)
    {
        VkPipelineStageFlagBits ret = 0;

        if ((flags & ShaderStageVertexBit) != 0)
            ret |= VkPipelineStageFlagBits.PipelineStageVertexShaderBit;

        if ((flags & ShaderStageFragmentBit) != 0)
            ret |= VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;

        if ((flags & ShaderStageGeometryBit) != 0)
            ret |= VkPipelineStageFlagBits.PipelineStageGeometryShaderBit;

        if ((flags & ShaderStageTessellationControlBit) != 0)
            ret |= VkPipelineStageFlagBits.PipelineStageTessellationControlShaderBit;

        if ((flags & ShaderStageTessellationEvaluationBit) != 0)
            ret |= VkPipelineStageFlagBits.PipelineStageTessellationEvaluationShaderBit;

        if ((flags & ShaderStageComputeBit) != 0)
            ret |= VkPipelineStageFlagBits.PipelineStageComputeShaderBit;

        return ret;
    }

    internal static VkBorderColor VdToVkSamplerBorderColor(SamplerBorderColor borderColor)
    {
        return borderColor switch
        {
            SamplerBorderColor.TransparentBlack => BorderColorFloatTransparentBlack,
            SamplerBorderColor.OpaqueBlack => BorderColorFloatOpaqueBlack,
            SamplerBorderColor.OpaqueWhite => BorderColorFloatOpaqueWhite,
            _ => Illegal.Handle<SamplerBorderColor, VkBorderColor>(),
        };
    }

    internal static VkIndexType VdToVkIndexFormat(IndexFormat format)
    {
        return format switch
        {
            IndexFormat.UInt16 => IndexTypeUint16,
            IndexFormat.UInt32 => IndexTypeUint32,
            _ => Illegal.Handle<IndexFormat, VkIndexType>(),
        };
    }

    internal static VkCompareOp VdToVkCompareOp(ComparisonKind comparisonKind)
    {
        return comparisonKind switch
        {
            ComparisonKind.Never => CompareOpNever,
            ComparisonKind.Less => CompareOpLess,
            ComparisonKind.Equal => CompareOpEqual,
            ComparisonKind.LessEqual => CompareOpLessOrEqual,
            ComparisonKind.Greater => CompareOpGreater,
            ComparisonKind.NotEqual => CompareOpNotEqual,
            ComparisonKind.GreaterEqual => CompareOpGreaterOrEqual,
            ComparisonKind.Always => CompareOpAlways,
            _ => Illegal.Handle<ComparisonKind, VkCompareOp>(),
        };
    }

    internal static PixelFormat VkToVdPixelFormat(VkFormat vkFormat)
    {
        return vkFormat switch
        {
            FormatR8Unorm => PixelFormat.R8UNorm,
            FormatR8Snorm => PixelFormat.R8SNorm,
            FormatR8Uint => PixelFormat.R8UInt,
            FormatR8Sint => PixelFormat.R8SInt,
            FormatR16Unorm => PixelFormat.R16UNorm,
            FormatR16Snorm => PixelFormat.R16SNorm,
            FormatR16Uint => PixelFormat.R16UInt,
            FormatR16Sint => PixelFormat.R16SInt,
            FormatR16Sfloat => PixelFormat.R16Float,
            FormatR32Uint => PixelFormat.R32UInt,
            FormatR32Sint => PixelFormat.R32SInt,
            FormatR32Sfloat or FormatD32Sfloat => PixelFormat.R32Float,
            FormatR8g8Unorm => PixelFormat.R8G8UNorm,
            FormatR8g8Snorm => PixelFormat.R8G8SNorm,
            FormatR8g8Uint => PixelFormat.R8G8UInt,
            FormatR8g8Sint => PixelFormat.R8G8SInt,
            FormatR16g16Unorm => PixelFormat.R16G16UNorm,
            FormatR16g16Snorm => PixelFormat.R16G16SNorm,
            FormatR16g16Uint => PixelFormat.R16G16UInt,
            FormatR16g16Sint => PixelFormat.R16G16SInt,
            FormatR16g16Sfloat => PixelFormat.R16G16Float,
            FormatR32g32Uint => PixelFormat.R32G32UInt,
            FormatR32g32Sint => PixelFormat.R32G32SInt,
            FormatR32g32Sfloat => PixelFormat.R32G32Float,
            FormatR8g8b8a8Unorm => PixelFormat.R8G8B8A8UNorm,
            FormatR8g8b8a8Srgb => PixelFormat.R8G8B8A8UNormSRgb,
            FormatB8g8r8a8Unorm => PixelFormat.B8G8R8A8UNorm,
            FormatB8g8r8a8Srgb => PixelFormat.B8G8R8A8UNormSRgb,
            FormatR8g8b8a8Snorm => PixelFormat.R8G8B8A8SNorm,
            FormatR8g8b8a8Uint => PixelFormat.R8G8B8A8UInt,
            FormatR8g8b8a8Sint => PixelFormat.R8G8B8A8SInt,
            FormatR16g16b16a16Unorm => PixelFormat.R16G16B16A16UNorm,
            FormatR16g16b16a16Snorm => PixelFormat.R16G16B16A16SNorm,
            FormatR16g16b16a16Uint => PixelFormat.R16G16B16A16UInt,
            FormatR16g16b16a16Sint => PixelFormat.R16G16B16A16SInt,
            FormatR16g16b16a16Sfloat => PixelFormat.R16G16B16A16Float,
            FormatR32g32b32a32Uint => PixelFormat.R32G32B32A32UInt,
            FormatR32g32b32a32Sint => PixelFormat.R32G32B32A32SInt,
            FormatR32g32b32a32Sfloat => PixelFormat.R32G32B32A32Float,
            FormatBc1RgbUnormBlock => PixelFormat.Bc1RgbUNorm,
            FormatBc1RgbSrgbBlock => PixelFormat.Bc1RgbUNormSRgb,
            FormatBc1RgbaUnormBlock => PixelFormat.Bc1RgbaUNorm,
            FormatBc1RgbaSrgbBlock => PixelFormat.Bc1RgbaUNormSRgb,
            FormatBc2UnormBlock => PixelFormat.Bc2UNorm,
            FormatBc2SrgbBlock => PixelFormat.Bc2UNormSRgb,
            FormatBc3UnormBlock => PixelFormat.Bc3UNorm,
            FormatBc3SrgbBlock => PixelFormat.Bc3UNormSRgb,
            FormatBc4UnormBlock => PixelFormat.Bc4UNorm,
            FormatBc4SnormBlock => PixelFormat.Bc4SNorm,
            FormatBc5UnormBlock => PixelFormat.Bc5UNorm,
            FormatBc5SnormBlock => PixelFormat.Bc5SNorm,
            FormatBc7UnormBlock => PixelFormat.Bc7UNorm,
            FormatBc7SrgbBlock => PixelFormat.Bc7UNormSRgb,
            FormatA2b10g10r10UnormPack32 => PixelFormat.R10G10B10A2UNorm,
            FormatA2b10g10r10UintPack32 => PixelFormat.R10G10B10A2UInt,
            FormatB10g11r11UfloatPack32 => PixelFormat.R11G11B10Float,
            _ => Illegal.Handle<VkFormat, PixelFormat>(),
        };
    }
}
