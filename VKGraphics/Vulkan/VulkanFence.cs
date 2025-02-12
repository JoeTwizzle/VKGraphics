using OpenTK.Graphics.Vulkan;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal sealed class VulkanFence : Fence, Vulkan.IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _device;
    private string? _name;
    public VkFence DeviceFence { get; }
    public Vulkan.ResourceRefCount RefCount { get; }

    public VulkanFence(VulkanGraphicsDevice device, VkFence fence)
    {
        _device = device;
        DeviceFence = fence;
        RefCount = new(this);
    }

    public override void Dispose() => RefCount?.DecrementDispose();
    unsafe void Vulkan.IResourceRefCountTarget.RefZeroed()
    {
        DestroyFence(_device.Device, DeviceFence, null);
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _device.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeFenceExt, DeviceFence.Handle, value);
        }
    }

    public override bool Signaled => GetFenceStatus(_device.Device, DeviceFence) is VkResult.Success;
    public override bool IsDisposed => RefCount.IsDisposed;

    public override unsafe void Reset()
    {
        var fence = DeviceFence;
        VulkanUtil.CheckResult(ResetFences(_device.Device, 1, &fence));
    }
}
