﻿
namespace VKGraphics.Vulkan;

internal unsafe class VkFence : Fence
{
    public OpenTK.Graphics.Vulkan.VkFence DeviceFence => fence;

    public override bool Signaled => Vk.GetFenceStatus(gd.Device, fence) == VkResult.Success;
    public override bool IsDisposed => destroyed;

    public override string? Name
    {
        get => name;
        set
        {
            name = value;
            gd.SetResourceName(this, value);
        }
    }

    private readonly VkGraphicsDevice gd;
    private readonly OpenTK.Graphics.Vulkan.VkFence fence;
    private string? name;
    private bool destroyed;

    public VkFence(VkGraphicsDevice gd, bool signaled)
    {
        this.gd = gd;
        var fenceCi = new VkFenceCreateInfo();
        fenceCi.flags = signaled ? VkFenceCreateFlagBits.FenceCreateSignaledBit : 0;
        OpenTK.Graphics.Vulkan.VkFence vkfence;
        var result = Vk.CreateFence(this.gd.Device, &fenceCi, null, &vkfence);
        fence = vkfence;
        VulkanUtil.CheckResult(result);
    }

    #region Disposal

    public override void Dispose()
    {
        if (!destroyed)
        {
            Vk.DestroyFence(gd.Device, fence, null);
            destroyed = true;
        }
    }

    #endregion

    public override void Reset()
    {
        gd.ResetFence(this);
    }
}
