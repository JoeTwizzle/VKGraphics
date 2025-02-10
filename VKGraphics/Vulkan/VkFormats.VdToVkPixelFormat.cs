

namespace VKGraphics.Vulkan;

internal static partial class VkFormats
{
    internal static VkFormat VdToVkPixelFormat(PixelFormat format, bool toDepthFormat = false)
    {
        switch (format)
        {
            case PixelFormat.R8UNorm:
                return VkFormat.FormatR8Unorm;

            case PixelFormat.R8SNorm:
                return VkFormat.FormatR8Snorm;

            case PixelFormat.R8UInt:
                return VkFormat.FormatR8Uint;

            case PixelFormat.R8SInt:
                return VkFormat.FormatR8Sint;

            case PixelFormat.R16UNorm:
                return toDepthFormat ? VkFormat.FormatD16Unorm : VkFormat.FormatR16Unorm;

            case PixelFormat.R16SNorm:
                return VkFormat.FormatR16Snorm;

            case PixelFormat.R16UInt:
                return VkFormat.FormatR16Uint;

            case PixelFormat.R16SInt:
                return VkFormat.FormatR16Sint;

            case PixelFormat.R16Float:
                return VkFormat.FormatR16Sfloat;

            case PixelFormat.R32UInt:
                return VkFormat.FormatR32Uint;

            case PixelFormat.R32SInt:
                return VkFormat.FormatR32Sint;

            case PixelFormat.R32Float:
                return toDepthFormat ? VkFormat.FormatD32Sfloat : VkFormat.FormatR32Sfloat;

            case PixelFormat.R8G8UNorm:
                return VkFormat.FormatR8g8Unorm;

            case PixelFormat.R8G8SNorm:
                return VkFormat.FormatR8g8Snorm;

            case PixelFormat.R8G8UInt:
                return VkFormat.FormatR8g8Uint;

            case PixelFormat.R8G8SInt:
                return VkFormat.FormatR8g8Sint;

            case PixelFormat.R16G16UNorm:
                return VkFormat.FormatR16g16Unorm;

            case PixelFormat.R16G16SNorm:
                return VkFormat.FormatR16g16Snorm;

            case PixelFormat.R16G16UInt:
                return VkFormat.FormatR16g16Uint;

            case PixelFormat.R16G16SInt:
                return VkFormat.FormatR16g16Sint;

            case PixelFormat.R16G16Float:
                return VkFormat.FormatR16g16b16a16Sfloat;

            case PixelFormat.R32G32UInt:
                return VkFormat.FormatR32g32Uint;

            case PixelFormat.R32G32SInt:
                return VkFormat.FormatR32g32Sint;

            case PixelFormat.R32G32Float:
                return VkFormat.FormatR32g32b32a32Sfloat;

            case PixelFormat.R8G8B8A8UNorm:
                return VkFormat.FormatR8g8b8a8Unorm;

            case PixelFormat.R8G8B8A8UNormSRgb:
                return VkFormat.FormatR8g8b8a8Srgb;

            case PixelFormat.B8G8R8A8UNorm:
                return VkFormat.FormatB8g8r8a8Unorm;

            case PixelFormat.B8G8R8A8UNormSRgb:
                return VkFormat.FormatB8g8r8a8Srgb;

            case PixelFormat.R8G8B8A8SNorm:
                return VkFormat.FormatR8g8b8a8Snorm;

            case PixelFormat.R8G8B8A8UInt:
                return VkFormat.FormatR8g8b8a8Uint;

            case PixelFormat.R8G8B8A8SInt:
                return VkFormat.FormatR8g8b8a8Sint;

            case PixelFormat.R16G16B16A16UNorm:
                return VkFormat.FormatR16g16b16a16Unorm;

            case PixelFormat.R16G16B16A16SNorm:
                return VkFormat.FormatR16g16b16a16Snorm;

            case PixelFormat.R16G16B16A16UInt:
                return VkFormat.FormatR16g16b16a16Uint;

            case PixelFormat.R16G16B16A16SInt:
                return VkFormat.FormatR16g16b16a16Sint;

            case PixelFormat.R16G16B16A16Float:
                return VkFormat.FormatR16g16b16a16Sfloat;

            case PixelFormat.R32G32B32A32UInt:
                return VkFormat.FormatR32g32b32a32Uint;

            case PixelFormat.R32G32B32A32SInt:
                return VkFormat.FormatR32g32b32a32Sint;

            case PixelFormat.R32G32B32A32Float:
                return VkFormat.FormatR32g32b32a32Sfloat;

            case PixelFormat.Bc1RgbUNorm:
                return VkFormat.FormatBc1RgbUnormBlock;

            case PixelFormat.Bc1RgbUNormSRgb:
                return VkFormat.FormatBc1RgbSrgbBlock;

            case PixelFormat.Bc1RgbaUNorm:
                return VkFormat.FormatBc1RgbaUnormBlock;

            case PixelFormat.Bc1RgbaUNormSRgb:
                return VkFormat.FormatBc1RgbaSrgbBlock;

            case PixelFormat.Bc2UNorm:
                return VkFormat.FormatBc2UnormBlock;

            case PixelFormat.Bc2UNormSRgb:
                return VkFormat.FormatBc2SrgbBlock;

            case PixelFormat.Bc3UNorm:
                return VkFormat.FormatBc3UnormBlock;

            case PixelFormat.Bc3UNormSRgb:
                return VkFormat.FormatBc3SrgbBlock;

            case PixelFormat.Bc4UNorm:
                return VkFormat.FormatBc4UnormBlock;

            case PixelFormat.Bc4SNorm:
                return VkFormat.FormatBc4SnormBlock;

            case PixelFormat.Bc5UNorm:
                return VkFormat.FormatBc5UnormBlock;

            case PixelFormat.Bc5SNorm:
                return VkFormat.FormatBc5SnormBlock;

            case PixelFormat.Bc7UNorm:
                return VkFormat.FormatBc7UnormBlock;

            case PixelFormat.Bc7UNormSRgb:
                return VkFormat.FormatBc7SrgbBlock;

            case PixelFormat.Etc2R8G8B8UNorm:
                return VkFormat.FormatEtc2R8g8b8UnormBlock;

            case PixelFormat.Etc2R8G8B8A1UNorm:
                return VkFormat.FormatEtc2R8g8b8a1UnormBlock;

            case PixelFormat.Etc2R8G8B8A8UNorm:
                return VkFormat.FormatEtc2R8g8b8a8UnormBlock;

            case PixelFormat.D32FloatS8UInt:
                return VkFormat.FormatD32SfloatS8Uint;

            case PixelFormat.D24UNormS8UInt:
                return VkFormat.FormatD24UnormS8Uint;

            case PixelFormat.R10G10B10A2UNorm:
                return VkFormat.FormatA2b10g10r10UnormPack32;

            case PixelFormat.R10G10B10A2UInt:
                return VkFormat.FormatA2b10g10r10UintPack32;

            case PixelFormat.R11G11B10Float:
                return VkFormat.FormatB10g11r11UfloatPack32;

            default:
                throw new VeldridException($"Invalid {nameof(PixelFormat)}: {format}");
        }
    }
}
