using static VKGraphics.Vulkan.VulkanUtil;

namespace VKGraphics.Vulkan;

internal unsafe class VkResourceLayout : ResourceLayout
{
    public VkDescriptorSetLayout DescriptorSetLayout => dsl;
    public VkDescriptorType[] DescriptorTypes { get; }

    public DescriptorResourceCounts DescriptorResourceCounts { get; }
    public new int DynamicBufferCount { get; }

    public override bool IsDisposed => disposed;

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
    private readonly VkDescriptorSetLayout dsl;
    private bool disposed;
    private string name;

    public VkResourceLayout(VkGraphicsDevice gd, ref ResourceLayoutDescription description)
        : base(ref description)
    {
        this.gd = gd;
        var dslCi = new VkDescriptorSetLayoutCreateInfo();
        var elements = description.Elements;
        DescriptorTypes = new VkDescriptorType[elements.Length];
        var bindings = stackalloc VkDescriptorSetLayoutBinding[elements.Length];

        uint uniformBufferCount = 0;
        uint uniformBufferDynamicCount = 0;
        uint sampledImageCount = 0;
        uint samplerCount = 0;
        uint storageBufferCount = 0;
        uint storageBufferDynamicCount = 0;
        uint storageImageCount = 0;

        for (uint i = 0; i < elements.Length; i++)
        {
            bindings[i].binding = i;
            bindings[i].descriptorCount = 1;
            var descriptorType = VkFormats.VdToVkDescriptorType(elements[i].Kind, elements[i].Options);
            bindings[i].descriptorType = descriptorType;
            bindings[i].stageFlags = VkFormats.VdToVkShaderStages(elements[i].Stages);
            if ((elements[i].Options & ResourceLayoutElementOptions.DynamicBinding) != 0)
            {
                DynamicBufferCount += 1;
            }

            DescriptorTypes[i] = descriptorType;

            switch (descriptorType)
            {
                case VkDescriptorType.DescriptorTypeSampler:
                    samplerCount += 1;
                    break;

                case VkDescriptorType.DescriptorTypeSampledImage:
                    sampledImageCount += 1;
                    break;

                case VkDescriptorType.DescriptorTypeStorageImage:
                    storageImageCount += 1;
                    break;

                case VkDescriptorType.DescriptorTypeUniformBuffer:
                    uniformBufferCount += 1;
                    break;

                case VkDescriptorType.DescriptorTypeUniformBufferDynamic:
                    uniformBufferDynamicCount += 1;
                    break;

                case VkDescriptorType.DescriptorTypeStorageBuffer:
                    storageBufferCount += 1;
                    break;

                case VkDescriptorType.DescriptorTypeStorageBufferDynamic:
                    storageBufferDynamicCount += 1;
                    break;
            }
        }

        DescriptorResourceCounts = new DescriptorResourceCounts(
            uniformBufferCount,
            uniformBufferDynamicCount,
            sampledImageCount,
            samplerCount,
            storageBufferCount,
            storageBufferDynamicCount,
            storageImageCount);

        dslCi.bindingCount = (uint)elements.Length;
        dslCi.pBindings = bindings;
        VkDescriptorSetLayout vkDescriptorSetLayout;
        var result = Vk.CreateDescriptorSetLayout(this.gd.Device, &dslCi, null, &vkDescriptorSetLayout);
        dsl = vkDescriptorSetLayout;
        CheckResult(result);
    }

    #region Disposal

    public override void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            Vk.DestroyDescriptorSetLayout(gd.Device, dsl, null);
        }
    }

    #endregion
}
