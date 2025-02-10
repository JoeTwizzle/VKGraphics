global using OpenTK.Graphics.Vulkan;
using System.Diagnostics;

namespace VKGraphics.Vulkan;

public unsafe delegate uint PFN_vkDebugReportCallbackEXT(uint flags, VkDebugReportObjectTypeEXT objectType, ulong @object, UIntPtr location, int messageCode, byte* pLayerPrefix, byte* pMessage, void* pUserData);
internal static unsafe class VulkanUtil
{
    private static readonly Lazy<bool> s_is_vulkan_loaded = new Lazy<bool>(tryLoadVulkan);
    private static readonly Lazy<string[]> s_instance_extensions = new Lazy<string[]>(enumerateInstanceExtensions);

    [Conditional("DEBUG")]
    public static void CheckResult(VkResult result)
    {
        if (result != VkResult.Success)
        {
            throw new VeldridException("Unsuccessful VkResult: " + result);
        }
    }

    public static bool TryFindMemoryType(VkPhysicalDeviceMemoryProperties memProperties, uint typeFilter, VkMemoryPropertyFlagBits properties, out uint typeIndex)
    {
        typeIndex = 0;

        for (int i = 0; i < memProperties.memoryTypeCount; i++)
        {
            if ((typeFilter & (1 << i)) != 0
                && (memProperties.GetMemoryType((uint)i).propertyFlags & properties) == properties)
            {
                typeIndex = (uint)i;
                return true;
            }
        }

        return false;
    }

    public static string[] EnumerateInstanceLayers()
    {
        uint propCount = 0;
        var result = Vk.EnumerateInstanceLayerProperties(&propCount, null);
        CheckResult(result);
        if (propCount == 0)
        {
            return Array.Empty<string>();
        }

        var props = stackalloc VkLayerProperties[(int)propCount];
        Vk.EnumerateInstanceLayerProperties(&propCount, props);

        string[] ret = new string[propCount];

        for (int i = 0; i < propCount; i++)
        {
            ret[i] = Util.GetString(&props[i].layerName[0]);
        }

        return ret;
    }

    public static string[] GetInstanceExtensions()
    {
        return s_instance_extensions.Value;
    }

    public static bool IsVulkanLoaded()
    {
        return s_is_vulkan_loaded.Value;
    }

    public static void TransitionImageLayout(
        VkCommandBuffer cb,
        VkImage image,
        uint baseMipLevel,
        uint levelCount,
        uint baseArrayLayer,
        uint layerCount,
        VkImageAspectFlagBits aspectMask,
        VkImageLayout oldLayout,
        VkImageLayout newLayout)
    {

        Debug.Assert(oldLayout != newLayout);
        var barrier = new VkImageMemoryBarrier
        {
            oldLayout = oldLayout,
            newLayout = newLayout,
            srcQueueFamilyIndex = Vk.QueueFamilyIgnored,
            dstQueueFamilyIndex = Vk.QueueFamilyIgnored,
            image = image
        };
        barrier.subresourceRange.aspectMask = aspectMask;
        barrier.subresourceRange.baseMipLevel = baseMipLevel;
        barrier.subresourceRange.levelCount = levelCount;
        barrier.subresourceRange.baseArrayLayer = baseArrayLayer;
        barrier.subresourceRange.layerCount = layerCount;

        var srcStageFlags = VkPipelineStageFlagBits.PipelineStageNoneKhr;
        var dstStageFlags = VkPipelineStageFlagBits.PipelineStageNoneKhr;

        if ((oldLayout == VkImageLayout.ImageLayoutUndefined || oldLayout == VkImageLayout.ImageLayoutPreinitialized) && newLayout == VkImageLayout.ImageLayoutTransferDstOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessNone;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTopOfPipeBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal && newLayout == VkImageLayout.ImageLayoutTransferSrcOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal && newLayout == VkImageLayout.ImageLayoutTransferDstOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutPreinitialized && newLayout == VkImageLayout.ImageLayoutTransferSrcOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessNone;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTopOfPipeBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutPreinitialized && newLayout == VkImageLayout.ImageLayoutGeneral)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessNone;
            barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTopOfPipeBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageComputeShaderBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutPreinitialized && newLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessNone;
            barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTopOfPipeBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutGeneral && newLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessTransferReadBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal && newLayout == VkImageLayout.ImageLayoutGeneral)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageComputeShaderBit;
        }

        else if (oldLayout == VkImageLayout.ImageLayoutTransferSrcOptimal && newLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessTransferReadBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutTransferDstOptimal && newLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutTransferSrcOptimal && newLayout == VkImageLayout.ImageLayoutTransferDstOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessTransferReadBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutTransferDstOptimal && newLayout == VkImageLayout.ImageLayoutTransferSrcOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutColorAttachmentOptimal && newLayout == VkImageLayout.ImageLayoutTransferSrcOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessColorAttachmentWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutColorAttachmentOptimal && newLayout == VkImageLayout.ImageLayoutTransferDstOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessColorAttachmentWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutColorAttachmentOptimal && newLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessColorAttachmentWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal && newLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutColorAttachmentOptimal && newLayout == VkImageLayout.ImageLayoutPresentSrcKhr)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessColorAttachmentWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessMemoryReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageBottomOfPipeBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutTransferDstOptimal && newLayout == VkImageLayout.ImageLayoutPresentSrcKhr)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessMemoryReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageBottomOfPipeBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutTransferDstOptimal && newLayout == VkImageLayout.ImageLayoutColorAttachmentOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessColorAttachmentWriteBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutTransferDstOptimal && newLayout == VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutGeneral && newLayout == VkImageLayout.ImageLayoutTransferSrcOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessShaderWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageComputeShaderBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutGeneral && newLayout == VkImageLayout.ImageLayoutTransferDstOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessShaderWriteBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageComputeShaderBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else if (oldLayout == VkImageLayout.ImageLayoutPresentSrcKhr && newLayout == VkImageLayout.ImageLayoutTransferSrcOptimal)
        {
            barrier.srcAccessMask = VkAccessFlagBits.AccessMemoryReadBit;
            barrier.dstAccessMask = VkAccessFlagBits.AccessTransferReadBit;
            srcStageFlags = VkPipelineStageFlagBits.PipelineStageBottomOfPipeBit;
            dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
        }
        else
        {
            Debug.Fail("Invalid image layout transition.");
        }

        Vk.CmdPipelineBarrier(
            cb,
            srcStageFlags,
            dstStageFlags,
            (VkDependencyFlagBits)0,
            0, null,
            0, null,
            1, &barrier);
    }

    private static string[] enumerateInstanceExtensions()
    {
        if (!IsVulkanLoaded())
        {
            return Array.Empty<string>();
        }

        uint propCount = 0;
        var result = Vk.EnumerateInstanceExtensionProperties((byte*)null, &propCount, null);
        if (result != VkResult.Success)
        {
            return Array.Empty<string>();
        }

        if (propCount == 0)
        {
            return Array.Empty<string>();
        }

        var props = stackalloc VkExtensionProperties[(int)propCount];
        Vk.EnumerateInstanceExtensionProperties((byte*)null, &propCount, &props[0]);

        string[] ret = new string[propCount];

        for (int i = 0; i < propCount; i++)
        {
            ret[i] = Util.GetString(&props[i].extensionName[0]);
        }

        return ret;
    }

    private static bool tryLoadVulkan()
    {
        try
        {
            uint propCount;
            Vk.EnumerateInstanceExtensionProperties((byte*)null, &propCount, null);
            return true;
        }
        catch { return false; }
    }
}

internal static unsafe class VkPhysicalDeviceMemoryPropertiesEx
{
    public static VkMemoryType GetMemoryType(this VkPhysicalDeviceMemoryProperties memoryProperties, uint index)
    {
        return memoryProperties.memoryTypes[(int)index];
    }
}
