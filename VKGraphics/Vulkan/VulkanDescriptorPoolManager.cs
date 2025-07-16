using OpenTK.Graphics.Vulkan;
using System.Diagnostics;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal sealed class VulkanDescriptorPoolManager
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly List<PoolInfo> _pools = new();
    private readonly object _lock = new();

    public VulkanDescriptorPoolManager(VulkanGraphicsDevice gd)
    {
        _gd = gd;
        _pools.Add(CreateNewPool());
    }

    public unsafe DescriptorAllocationToken Allocate(DescriptorResourceCounts counts, VkDescriptorSetLayout setLayout)
    {
        lock (_lock)
        {
            VkDescriptorPool pool = GetPool(counts);
            VkDescriptorSetAllocateInfo dsAI = new()
            {
                descriptorSetCount = 1,
                pSetLayouts = &setLayout,
                descriptorPool = pool
            };

            VkDescriptorSet set;
            VulkanUtil.CheckResult(AllocateDescriptorSets(_gd.Device, &dsAI, &set));

            return new DescriptorAllocationToken(set, pool);
        }
    }

    public void Free(DescriptorAllocationToken token, DescriptorResourceCounts counts)
    {
        lock (_lock)
        {
            foreach (PoolInfo poolInfo in _pools)
            {
                if (poolInfo.Pool == token.Pool)
                {
                    poolInfo.Free(_gd.Device, token, counts);
                }
            }
        }
    }

    private VkDescriptorPool GetPool(DescriptorResourceCounts counts)
    {
        foreach (PoolInfo poolInfo in _pools)
        {
            if (poolInfo.Allocate(counts))
            {
                return poolInfo.Pool;
            }
        }

        PoolInfo newPool = CreateNewPool();
        _pools.Add(newPool);
        bool result = newPool.Allocate(counts);
        Debug.Assert(result);
        return newPool.Pool;
    }

    private unsafe PoolInfo CreateNewPool()
    {
        uint totalSets = 1000;
        uint descriptorCount = 100;
        uint poolSizeCount = 7;
        VkDescriptorPoolSize* sizes = stackalloc VkDescriptorPoolSize[(int)poolSizeCount];
        sizes[0].type = VkDescriptorType.DescriptorTypeUniformBuffer;
        sizes[0].descriptorCount = descriptorCount;
        sizes[1].type = VkDescriptorType.DescriptorTypeSampledImage;
        sizes[1].descriptorCount = descriptorCount;
        sizes[2].type = VkDescriptorType.DescriptorTypeSampler;
        sizes[2].descriptorCount = descriptorCount;
        sizes[3].type = VkDescriptorType.DescriptorTypeStorageBuffer;
        sizes[3].descriptorCount = descriptorCount;
        sizes[4].type = VkDescriptorType.DescriptorTypeStorageImage;
        sizes[4].descriptorCount = descriptorCount;
        sizes[5].type = VkDescriptorType.DescriptorTypeUniformBufferDynamic;
        sizes[5].descriptorCount = descriptorCount;
        sizes[6].type = VkDescriptorType.DescriptorTypeStorageBufferDynamic;
        sizes[6].descriptorCount = descriptorCount;

        VkDescriptorPoolCreateInfo poolCI = new()
        {
            flags = VkDescriptorPoolCreateFlagBits.DescriptorPoolCreateFreeDescriptorSetBit,
            maxSets = totalSets,
            pPoolSizes = sizes,
            poolSizeCount = poolSizeCount
        };

        VkDescriptorPool descriptorPool;
        VulkanUtil.CheckResult(CreateDescriptorPool(_gd.Device, &poolCI, null, &descriptorPool));

        return new PoolInfo(descriptorPool, totalSets, descriptorCount);
    }

    internal unsafe void DestroyAll()
    {
        foreach (PoolInfo poolInfo in _pools)
        {
            DestroyDescriptorPool(_gd.Device, poolInfo.Pool, null);
        }
    }

    private sealed class PoolInfo
    {
        public readonly VkDescriptorPool Pool;

        public uint RemainingSets;

        public uint UniformBufferCount;
        public uint UniformBufferDynamicCount;
        public uint SampledImageCount;
        public uint SamplerCount;
        public uint StorageBufferCount;
        public uint StorageBufferDynamicCount;
        public uint StorageImageCount;

        public PoolInfo(VkDescriptorPool pool, uint totalSets, uint descriptorCount)
        {
            Pool = pool;
            RemainingSets = totalSets;
            UniformBufferCount = descriptorCount;
            UniformBufferDynamicCount = descriptorCount;
            SampledImageCount = descriptorCount;
            SamplerCount = descriptorCount;
            StorageBufferCount = descriptorCount;
            StorageBufferDynamicCount = descriptorCount;
            StorageImageCount = descriptorCount;
        }

        internal bool Allocate(DescriptorResourceCounts counts)
        {
            if (RemainingSets > 0
                && UniformBufferCount >= counts.UniformBufferCount
                && UniformBufferDynamicCount >= counts.UniformBufferDynamicCount
                && SampledImageCount >= counts.SampledImageCount
                && SamplerCount >= counts.SamplerCount
                && StorageBufferCount >= counts.StorageBufferCount
                && StorageBufferDynamicCount >= counts.StorageBufferDynamicCount
                && StorageImageCount >= counts.StorageImageCount)
            {
                RemainingSets -= 1;
                UniformBufferCount -= counts.UniformBufferCount;
                UniformBufferDynamicCount -= counts.UniformBufferDynamicCount;
                SampledImageCount -= counts.SampledImageCount;
                SamplerCount -= counts.SamplerCount;
                StorageBufferCount -= counts.StorageBufferCount;
                StorageBufferDynamicCount -= counts.StorageBufferDynamicCount;
                StorageImageCount -= counts.StorageImageCount;
                return true;
            }
            else
            {
                return false;
            }
        }

        internal unsafe void Free(VkDevice device, DescriptorAllocationToken token, DescriptorResourceCounts counts)
        {
            VkDescriptorSet set = token.Set;
            FreeDescriptorSets(device, Pool, 1, &set);

            RemainingSets += 1;

            UniformBufferCount += counts.UniformBufferCount;
            UniformBufferDynamicCount += counts.UniformBufferDynamicCount;
            SampledImageCount += counts.SampledImageCount;
            SamplerCount += counts.SamplerCount;
            StorageBufferCount += counts.StorageBufferCount;
            StorageBufferDynamicCount += counts.StorageBufferDynamicCount;
            StorageImageCount += counts.StorageImageCount;
        }
    }
}

internal struct DescriptorAllocationToken
{
    public readonly VkDescriptorSet Set;
    public readonly VkDescriptorPool Pool;

    public DescriptorAllocationToken(VkDescriptorSet set, VkDescriptorPool pool)
    {
        Set = set;
        Pool = pool;
    }
}
