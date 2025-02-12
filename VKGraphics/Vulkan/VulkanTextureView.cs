using OpenTK.Graphics.Vulkan;
using static OpenTK.Graphics.Vulkan.Vk;

namespace VKGraphics.Vulkan;

internal sealed unsafe class VulkanTextureView : TextureView, IResourceRefCountTarget
{
    private readonly VulkanGraphicsDevice _gd;
    private readonly VkImageView _imageView;
    private string? _name;

    public VkImageView ImageView => _imageView;
    public new VulkanTexture Target => (VulkanTexture)base.Target;

    public ResourceRefCount RefCount { get; }
    public override bool IsDisposed => RefCount.IsDisposed;

    public uint RealArrayLayers
        => (Target.Usage & TextureUsage.Cubemap) != 0 ? ArrayLayers * 6 : ArrayLayers;

    internal VulkanTextureView(VulkanGraphicsDevice gd, in TextureViewDescription description, VkImageView imageView) : base(description)
    {
        _gd = gd;
        _imageView = imageView;

        Target.RefCount.Increment();
        RefCount = new(this);
    }

    public override void Dispose() => RefCount?.DecrementDispose();
    void IResourceRefCountTarget.RefZeroed()
    {
        if (_imageView != VkImageView.Zero)
        {
            DestroyImageView(_gd.Device, _imageView, null);
        }

        Target.RefCount.Decrement();
    }

    public override string? Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetDebugMarkerName(VkDebugReportObjectTypeEXT.DebugReportObjectTypeImageViewExt, _imageView.Handle, value);
        }
    }
}
