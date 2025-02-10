using static VKGraphics.Vulkan.VulkanUtil;

namespace VKGraphics.Vulkan;

internal unsafe class VkBuffer : DeviceBuffer
{
    public ResourceRefCount RefCount { get; }
    public override bool IsDisposed => destroyed;

    public override uint SizeInBytes { get; }
    public override BufferUsage Usage { get; }

    public OpenTK.Graphics.Vulkan.VkBuffer DeviceBuffer => deviceBuffer;
    public VkMemoryBlock Memory => memory;

    public VkMemoryRequirements BufferMemoryRequirements => bufferMemoryRequirements;

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
    private readonly OpenTK.Graphics.Vulkan.VkBuffer deviceBuffer;
    private readonly VkMemoryBlock memory;
    private readonly VkMemoryRequirements bufferMemoryRequirements;
    private bool destroyed;
    private string name;

    public VkBuffer(VkGraphicsDevice gd, uint sizeInBytes, BufferUsage usage, string callerMember = null)
    {
        this.gd = gd;
        SizeInBytes = sizeInBytes;
        Usage = usage;

        var vkUsage = VkBufferUsageFlagBits.BufferUsageTransferSrcBit | VkBufferUsageFlagBits.BufferUsageTransferDstBit;
        if ((usage & BufferUsage.VertexBuffer) == BufferUsage.VertexBuffer)
        {
            vkUsage |= VkBufferUsageFlagBits.BufferUsageVertexBufferBit;
        }

        if ((usage & BufferUsage.IndexBuffer) == BufferUsage.IndexBuffer)
        {
            vkUsage |= VkBufferUsageFlagBits.BufferUsageIndexBufferBit;
        }

        if ((usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer)
        {
            vkUsage |= VkBufferUsageFlagBits.BufferUsageUniformBufferBit;
        }

        if ((usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite
            || (usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly)
        {
            vkUsage |= VkBufferUsageFlagBits.BufferUsageStorageBufferBit;
        }

        if ((usage & BufferUsage.IndirectBuffer) == BufferUsage.IndirectBuffer)
        {
            vkUsage |= VkBufferUsageFlagBits.BufferUsageIndirectBufferBit;
        }

        var bufferCi = new VkBufferCreateInfo();
        bufferCi.size = sizeInBytes;
        bufferCi.usage = vkUsage;
        OpenTK.Graphics.Vulkan.VkBuffer vkdeviceBuffer;
        var result = Vk.CreateBuffer(gd.Device, &bufferCi, null, &vkdeviceBuffer);
        deviceBuffer = vkdeviceBuffer;
        CheckResult(result);

        bool prefersDedicatedAllocation;

        if (this.gd.GetBufferMemoryRequirements2 != null)
        {
            var memReqInfo2 = new VkBufferMemoryRequirementsInfo2
            {
                buffer = deviceBuffer
            };
            var memReqs2 = new VkMemoryRequirements2();
            var dedicatedReqs = new VkMemoryDedicatedRequirements();
            memReqs2.pNext = &dedicatedReqs;
            this.gd.GetBufferMemoryRequirements2(this.gd.Device, &memReqInfo2, &memReqs2);
            bufferMemoryRequirements = memReqs2.memoryRequirements;
            prefersDedicatedAllocation = (dedicatedReqs.prefersDedicatedAllocation | dedicatedReqs.requiresDedicatedAllocation) != 0;
        }
        else
        {
            VkMemoryRequirements vkbufferMemoryRequirements;
            Vk.GetBufferMemoryRequirements(gd.Device, deviceBuffer, &vkbufferMemoryRequirements);
            bufferMemoryRequirements = vkbufferMemoryRequirements;
            prefersDedicatedAllocation = false;
        }

        bool isStaging = (usage & BufferUsage.Staging) == BufferUsage.Staging;
        bool hostVisible = isStaging || (usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;

        var memoryPropertyFlags =
            hostVisible
                ? VkMemoryPropertyFlagBits.MemoryPropertyHostVisibleBit | VkMemoryPropertyFlagBits.MemoryPropertyHostCoherentBit
                : VkMemoryPropertyFlagBits.MemoryPropertyDeviceLocalBit;

        if (isStaging)
        {
            // Use "host cached" memory for staging when available, for better performance of GPU -> CPU transfers
            bool hostCachedAvailable = TryFindMemoryType(
                gd.PhysicalDeviceMemProperties,
                bufferMemoryRequirements.memoryTypeBits,
                memoryPropertyFlags | VkMemoryPropertyFlagBits.MemoryPropertyHostCachedBit,
                out _);
            if (hostCachedAvailable)
            {
                memoryPropertyFlags |= VkMemoryPropertyFlagBits.MemoryPropertyHostCachedBit;
            }
        }

        var memoryToken = gd.MemoryManager.Allocate(
            gd.PhysicalDeviceMemProperties,
            bufferMemoryRequirements.memoryTypeBits,
            memoryPropertyFlags,
            hostVisible,
            bufferMemoryRequirements.size,
            bufferMemoryRequirements.alignment,
            prefersDedicatedAllocation,
            VkImage.Zero,
            deviceBuffer);
        memory = memoryToken;
        result = Vk.BindBufferMemory(gd.Device, deviceBuffer, memory.DeviceMemory, memory.Offset);
        CheckResult(result);

        RefCount = new ResourceRefCount(disposeCore);
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
            Vk.DestroyBuffer(gd.Device, deviceBuffer, null);
            gd.MemoryManager.Free(Memory);
        }
    }
}
