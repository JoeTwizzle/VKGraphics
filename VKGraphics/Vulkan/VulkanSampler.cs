using OpenTK.Graphics.Vulkan;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal unsafe sealed class VulkanSampler : Sampler, IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly VkSampler _sampler;
    private string? _name;

    public VkSampler DeviceSampler => _sampler;

    public ResourceRefCount RefCount { get; }
    public override bool IsDisposed => RefCount.IsDisposed;

    internal VulkanSampler(VulkanGraphicsDevice gd, VkSampler sampler)
    {
        _gd = gd;
        _sampler = sampler;
        RefCount = new(this);
    }

    public override void Dispose() => RefCount?.DecrementDispose();

    void IResourceRefCountTarget.RefZeroed()
    {
        DestroySampler(_gd.Device, _sampler, null);
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeSamplerExt, _sampler.Handle, value);
        }
    }
}
