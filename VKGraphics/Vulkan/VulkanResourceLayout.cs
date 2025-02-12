using OpenTK.Graphics.Vulkan;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal sealed class VulkanResourceLayout : ResourceLayout, IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly VkDescriptorSetLayout _dsl;
    private readonly VkDescriptorType[] _descriptorTypes;
    private readonly VkShaderStageFlagBits[] _shaderStages;
    private readonly VkAccessFlagBits[] _accessFlags;
    private string? _name;

    public ResourceRefCount RefCount { get; }
    public override bool IsDisposed => RefCount.IsDisposed;

    public VkDescriptorSetLayout DescriptorSetLayout => _dsl;
    public VkDescriptorType[] DescriptorTypes => _descriptorTypes;
    public VkShaderStageFlagBits[] ShaderStages => _shaderStages;
    public VkAccessFlagBits[] AccessFlags => _accessFlags;
    public DescriptorResourceCounts ResourceCounts { get; }
    public new int DynamicBufferCount { get; }

    internal VulkanResourceLayout(VulkanGraphicsDevice gd, in ResourceLayoutDescription description,
        VkDescriptorSetLayout dsl,
        VkDescriptorType[] descriptorTypes, VkShaderStageFlagBits[] shaderStages, VkAccessFlagBits[] access,
        in DescriptorResourceCounts resourceCounts, int dynamicBufferCount)
        : base(description)
    {
        _gd = gd;
        _dsl = dsl;
        _descriptorTypes = descriptorTypes;
        _shaderStages = shaderStages;
        _accessFlags = access;
        ResourceCounts = resourceCounts;
        DynamicBufferCount = dynamicBufferCount;

        RefCount = new(this);
    }

    public override void Dispose() => RefCount?.DecrementDispose();
    unsafe void IResourceRefCountTarget.RefZeroed()
    {
        DestroyDescriptorSetLayout(_gd.Device, _dsl, null);
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeDescriptorSetLayoutExt, _dsl.Handle, value);
        }
    }
}
