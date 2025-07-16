using OpenTK.Graphics.Vulkan;
using System.Diagnostics;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal unsafe static class VulkanUtil
{
    private static int _mayHaveThreadException;

    [ThreadStatic]
    private static Exception? _threadDebugCallbackException;

    public static void CheckResult(VkResult result)
    {
        if (result != VkResult.Success || _mayHaveThreadException != 0)
        {
            ThrowResult(result);
        }
    }

    public static void SetDebugCallbackException(Exception exception)
    {
        if (_threadDebugCallbackException is { } exc)
        {
            exception = new AggregateException([exc, exception]).Flatten();
        }
        else
        {
            Interlocked.Increment(ref _mayHaveThreadException);
        }
        _threadDebugCallbackException = exception;
    }

    public static void ThrowResult(VkResult result)
    {
        if (_threadDebugCallbackException is { } exc)
        {
            Interlocked.Decrement(ref _mayHaveThreadException);
            _threadDebugCallbackException = null;
            throw exc;
        }

        if (result is VkResult.Success)
        {
            return;
        }

        if (result is VkResult.ErrorOutOfDeviceMemory or VkResult.ErrorOutOfHostMemory)
        {
            throw new VeldridOutOfMemoryException(GetExceptionMessage(result));
        }
        else
        {
            throw new VeldridException(GetExceptionMessage(result));
        }
    }

    private static string GetExceptionMessage(VkResult result)
    {
        return "Unsuccessful VkResult: " + result;
    }

    public static bool TryFindMemoryType(
        VkPhysicalDeviceMemoryProperties memProperties, uint typeFilter, VkMemoryPropertyFlagBits properties, out uint typeIndex)
    {
        for (uint i = 0; i < memProperties.memoryTypeCount; i++)
        {
            if (((typeFilter & (1u << (int)i)) != 0)
                && (memProperties.memoryTypes[(int)i].propertyFlags & properties) == properties)
            {
                typeIndex = i;
                return true;
            }
        }

        typeIndex = 0;
        return false;
    }

    public static string[] EnumerateInstanceLayers()
    {
        uint propCount = 0;
        VkResult result = EnumerateInstanceLayerProperties(&propCount, null);
        CheckResult(result);
        if (propCount == 0)
        {
            return Array.Empty<string>();
        }

        VkLayerProperties[] props = new VkLayerProperties[propCount];
        string[] ret = new string[propCount];

        fixed (VkLayerProperties* propPtr = props)
        {
            EnumerateInstanceLayerProperties(&propCount, propPtr);

            for (int i = 0; i < propCount; i++)
            {
                ReadOnlySpan<byte> layerName = propPtr[i].layerName;
                ret[i] = Util.GetString(layerName);
            }
        }

        return ret;
    }

    public static string[] EnumerateInstanceExtensions()
    {
        uint propCount = 0;
        VkResult result = EnumerateInstanceExtensionProperties(null, &propCount, null);
        if (result != VkResult.Success)
        {
            return Array.Empty<string>();
        }

        if (propCount == 0)
        {
            return Array.Empty<string>();
        }

        VkExtensionProperties[] props = new VkExtensionProperties[propCount];
        string[] ret = new string[propCount];

        fixed (VkExtensionProperties* propPtr = props)
        {
            EnumerateInstanceExtensionProperties(null, &propCount, propPtr);

            for (int i = 0; i < propCount; i++)
            {
                ReadOnlySpan<byte> extensionName = propPtr[i].extensionName;
                ret[i] = Util.GetString(extensionName);
            }
        }

        return ret;
    }

    public static IntPtr GetInstanceProcAddr(VkInstance instance, string name)
    {
        Span<byte> byteBuffer = stackalloc byte[1024];

        Util.GetNullTerminatedUtf8(name, ref byteBuffer);
        fixed (byte* utf8Ptr = byteBuffer)
        {
            return (IntPtr)Vk.GetInstanceProcAddr(instance, utf8Ptr);
        }
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
        VkImageMemoryBarrier barrier = new()
        {
            oldLayout = oldLayout,
            newLayout = newLayout,
            srcQueueFamilyIndex = QueueFamilyIgnored,
            dstQueueFamilyIndex = QueueFamilyIgnored,
            image = image,
            subresourceRange = new VkImageSubresourceRange()
            {
                aspectMask = aspectMask,
                baseMipLevel = baseMipLevel,
                levelCount = levelCount,
                baseArrayLayer = baseArrayLayer,
                layerCount = layerCount
            }
        };

        VkPipelineStageFlagBits srcStageFlags = VkPipelineStageFlagBits.PipelineStageNoneKhr;
        VkPipelineStageFlagBits dstStageFlags = VkPipelineStageFlagBits.PipelineStageNoneKhr;

        switch (oldLayout)
        {
            case VkImageLayout.ImageLayoutUndefined:
            case VkImageLayout.ImageLayoutPreinitialized:
                barrier.srcAccessMask = VkAccessFlagBits.AccessNoneKhr;
                srcStageFlags = VkPipelineStageFlagBits.PipelineStageTopOfPipeBit;
                break;

            case VkImageLayout.ImageLayoutGeneral:
                if (newLayout == VkImageLayout.ImageLayoutTransferSrcOptimal ||
                    newLayout == VkImageLayout.ImageLayoutTransferDstOptimal)
                {
                    barrier.srcAccessMask = VkAccessFlagBits.AccessShaderWriteBit;
                    srcStageFlags = VkPipelineStageFlagBits.PipelineStageComputeShaderBit;
                    break;
                }
                else if (newLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal)
                {
                    goto case VkImageLayout.ImageLayoutTransferSrcOptimal;
                }
                else if (newLayout == VkImageLayout.ImageLayoutColorAttachmentOptimal)
                {
                    goto case VkImageLayout.ImageLayoutColorAttachmentOptimal;
                }
                else if (newLayout == VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal)
                {
                    goto case VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;
                }
                goto default;

            case VkImageLayout.ImageLayoutTransferSrcOptimal:
                barrier.srcAccessMask = VkAccessFlagBits.AccessTransferReadBit;
                srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
                break;

            case VkImageLayout.ImageLayoutTransferDstOptimal:
                barrier.srcAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
                srcStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
                break;

            case VkImageLayout.ImageLayoutShaderReadOnlyOptimal:
                barrier.srcAccessMask = VkAccessFlagBits.AccessShaderReadBit;
                srcStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
                break;

            case VkImageLayout.ImageLayoutColorAttachmentOptimal:
                barrier.srcAccessMask = VkAccessFlagBits.AccessColorAttachmentWriteBit;
                srcStageFlags = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
                break;

            case VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal:
                barrier.srcAccessMask = VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit;
                srcStageFlags = VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit;
                break;

            case VkImageLayout.ImageLayoutPresentSrcKhr:
                barrier.srcAccessMask = VkAccessFlagBits.AccessMemoryReadBit;
                srcStageFlags = VkPipelineStageFlagBits.PipelineStageBottomOfPipeBit;
                break;

            default:
                Debug.Fail($"Invalid old image layout transition ({oldLayout} -> {newLayout})");
                break;
        }

        switch (newLayout)
        {
            case VkImageLayout.ImageLayoutGeneral:
                if (oldLayout == VkImageLayout.ImageLayoutPreinitialized ||
                    oldLayout == VkImageLayout.ImageLayoutShaderReadOnlyOptimal)
                {
                    barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
                    dstStageFlags = VkPipelineStageFlagBits.PipelineStageComputeShaderBit;
                    break;
                }
                else if (oldLayout == VkImageLayout.ImageLayoutTransferSrcOptimal)
                {
                    goto case VkImageLayout.ImageLayoutTransferSrcOptimal;
                }
                else if (oldLayout == VkImageLayout.ImageLayoutTransferDstOptimal)
                {
                    goto case VkImageLayout.ImageLayoutTransferDstOptimal;
                }
                else if (oldLayout == VkImageLayout.ImageLayoutColorAttachmentOptimal)
                {
                    goto case VkImageLayout.ImageLayoutColorAttachmentOptimal;
                }
                else if (oldLayout == VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal)
                {
                    goto case VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal;
                }
                goto default;

            case VkImageLayout.ImageLayoutTransferSrcOptimal:
                barrier.dstAccessMask = VkAccessFlagBits.AccessTransferReadBit;
                dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
                break;

            case VkImageLayout.ImageLayoutTransferDstOptimal:
                barrier.dstAccessMask = VkAccessFlagBits.AccessTransferWriteBit;
                dstStageFlags = VkPipelineStageFlagBits.PipelineStageTransferBit;
                break;

            case VkImageLayout.ImageLayoutShaderReadOnlyOptimal:
                barrier.dstAccessMask = VkAccessFlagBits.AccessShaderReadBit;
                dstStageFlags = VkPipelineStageFlagBits.PipelineStageFragmentShaderBit;
                break;

            case VkImageLayout.ImageLayoutColorAttachmentOptimal:
                barrier.dstAccessMask = VkAccessFlagBits.AccessColorAttachmentWriteBit;
                dstStageFlags = VkPipelineStageFlagBits.PipelineStageColorAttachmentOutputBit;
                break;

            case VkImageLayout.ImageLayoutDepthStencilAttachmentOptimal:
                barrier.dstAccessMask = VkAccessFlagBits.AccessDepthStencilAttachmentWriteBit;
                dstStageFlags = VkPipelineStageFlagBits.PipelineStageLateFragmentTestsBit;
                break;

            case VkImageLayout.ImageLayoutPresentSrcKhr:
                barrier.dstAccessMask = VkAccessFlagBits.AccessMemoryReadBit;
                dstStageFlags = VkPipelineStageFlagBits.PipelineStageBottomOfPipeBit;
                break;

            default:
                Debug.Fail($"Invalid new image layout transition ({oldLayout} -> {newLayout})");
                break;
        }

        CmdPipelineBarrier(
            cb,
            srcStageFlags,
            dstStageFlags,
            0,
            0, null,
            0, null,
            1, &barrier);
    }
}

internal unsafe static class VkPhysicalDeviceMemoryPropertiesEx
{
    public static VkMemoryType GetMemoryType(this VkPhysicalDeviceMemoryProperties memoryProperties, uint index)
    {
        return memoryProperties.memoryTypes[(int)index];
    }
}
