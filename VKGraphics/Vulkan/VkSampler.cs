
namespace VKGraphics.Vulkan;

internal unsafe class VkSampler : Sampler
{
    public OpenTK.Graphics.Vulkan.VkSampler DeviceSampler => sampler;

    public ResourceRefCount RefCount { get; }

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
    private readonly OpenTK.Graphics.Vulkan.VkSampler sampler;
    private bool disposed;
    private string? name;

    public VkSampler(VkGraphicsDevice gd, ref SamplerDescription description)
    {
        this.gd = gd;
        VkFormats.GetFilterParams(description.Filter, out var minFilter, out var magFilter, out var mipmapMode);

        var samplerCi = new VkSamplerCreateInfo
        {
            addressModeU = VkFormats.VdToVkSamplerAddressMode(description.AddressModeU),
            addressModeV = VkFormats.VdToVkSamplerAddressMode(description.AddressModeV),
            addressModeW = VkFormats.VdToVkSamplerAddressMode(description.AddressModeW),
            minFilter = minFilter,
            magFilter = magFilter,
            mipmapMode = mipmapMode,
            compareEnable = description.ComparisonKind != null ? 1 : 0,
            compareOp = description.ComparisonKind != null
                ? VkFormats.VdToVkCompareOp(description.ComparisonKind.Value)
                : VkCompareOp.CompareOpNever,
            anisotropyEnable = description.Filter == SamplerFilter.Anisotropic ? 1 : 0,
            maxAnisotropy = description.MaximumAnisotropy,
            minLod = description.MinimumLod,
            maxLod = description.MaximumLod,
            mipLodBias = description.LodBias,
            borderColor = VkFormats.VdToVkSamplerBorderColor(description.BorderColor)
        };
        OpenTK.Graphics.Vulkan.VkSampler vksampler;
        Vk.CreateSampler(this.gd.Device, &samplerCi, null, &vksampler);
        sampler = vksampler;
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
        if (!disposed)
        {
            Vk.DestroySampler(gd.Device, sampler, null);
            disposed = true;
        }
    }
}
